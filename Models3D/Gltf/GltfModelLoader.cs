using InnoVault.Models3D.Animation;
using InnoVault.Models3D.Runtime;
using InnoVault.Models3D.Skinning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria.ModLoader;

namespace InnoVault.Models3D.Gltf
{
    /// <summary>
    /// 本地最小 glTF 2.0 模型导入器
    /// <br/>支持外部 bin、三角网格、基础材质和节点变换；蒙皮（JOINTS/WEIGHTS）与
    /// 动画（translation/rotation/scale 通道）会被解析为 <see cref="Model3DSkeleton"/>
    /// 与 <see cref="Model3DAnimationClip"/>，运行时由 <see cref="AnimationPlayer"/> 驱动
    /// </summary>
    public static class GltfModelLoader
    {
        private const int ComponentByte = 5120;
        private const int ComponentUnsignedByte = 5121;
        private const int ComponentShort = 5122;
        private const int ComponentUnsignedShort = 5123;
        private const int ComponentUnsignedInt = 5125;
        private const int ComponentFloat = 5126;
        private const int ModeTriangles = 4;

        /// <summary>
        /// 加载 glTF 模型
        /// <br/>失败时返回 <see cref="Vault3DModel.Empty"/> 并写入日志
        /// </summary>
        /// <param name="mod">目标模组</param>
        /// <param name="path">glTF 路径</param>
        /// <param name="options">导入选项</param>
        /// <returns>加载完成的模型</returns>
        public static Vault3DModel Load(Mod mod, string path, GltfImportOptions options = null) {
            options ??= GltfImportOptions.Default;
            string normalized = NormalizeSlashes(path);
            Model3DDiagnostic diagnostic = new Model3DDiagnostic();

            if (!TryResolveGltfPath(mod, normalized, out string gltfPath)) {
                VaultMod.LoggerError($"[GltfModelLoader:{mod?.Name}/{path}]"
                    , $"glTF file not found: '{normalized}' (also tried '{normalized}.gltf')");
                return Vault3DModel.Empty;
            }

            string json;
            using (Stream stream = mod.GetFileStream(gltfPath, true)) {
                if (stream == null) {
                    diagnostic.Error(gltfPath, 0, "Failed to open glTF stream");
                    LogDiagnostic(mod, gltfPath, diagnostic);
                    return Vault3DModel.Empty;
                }
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                json = reader.ReadToEnd();
            }

            GltfDocument doc;
            try {
                doc = GltfDocument.Parse(json, diagnostic, gltfPath);
            } catch (Exception ex) {
                diagnostic.Error(gltfPath, 0, $"Failed to parse glTF JSON: {ex.Message}");
                LogDiagnostic(mod, gltfPath, diagnostic);
                return Vault3DModel.Empty;
            }

            string baseDir = GetDirectory(gltfPath);
            byte[][] buffers = LoadBuffers(mod, doc, baseDir, diagnostic, gltfPath);
            AccessorReader accessorReader = new AccessorReader(doc, buffers, diagnostic, gltfPath);

            //计算每个节点的"祖先到自身"的世界矩阵，构造时需要用到
            Matrix[] nodeWorld = BuildNodeWorldMatrices(doc);

            Dictionary<string, Model3DMaterial> materials = BuildMaterials(doc, mod, baseDir, diagnostic, gltfPath);

            //是否走"蒙皮模式"——只要导入开关开启且文档里有 skin 就启用，
            //此时所有 primitive 都不会把 R = AxisFlip * ImportScale 烘焙到顶点，改成 RootTransform 应用
            bool hasSkins = options.ImportSkins && doc.Skins.Count > 0;

            List<Model3DSkeleton> skeletons = new List<Model3DSkeleton>();
            Dictionary<int, List<(int skeletonIndex, int jointIndex)>> nodeToJoint
                = new Dictionary<int, List<(int, int)>>();
            if (hasSkins) {
                BuildSkeletons(doc, accessorReader, diagnostic, gltfPath, nodeWorld, skeletons, nodeToJoint);
            }

            List<Model3DMeshGroup> groups = new List<Model3DMeshGroup>();
            Vector3 min = new Vector3(float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity);
            bool boundsTouched = false;

            Matrix rootTransform = hasSkins ? BuildRootTransform(options) : Matrix.Identity;

            TraverseScene(doc, accessorReader, materials, options, diagnostic, gltfPath
                , groups, Matrix.Identity, ref min, ref max, ref boundsTouched, hasSkins, rootTransform);

            List<Model3DAnimationClip> clips = new List<Model3DAnimationClip>();
            if (hasSkins && options.ImportAnimations) {
                BuildAnimationClips(doc, accessorReader, diagnostic, gltfPath, nodeToJoint, clips);
            }

            BoundingBox bounds = boundsTouched ? new BoundingBox(min, max) : new BoundingBox(Vector3.Zero, Vector3.Zero);
            Vector3 pivot = options.CenterPivot && boundsTouched ? (min + max) * 0.5f : Vector3.Zero;
            Vault3DModel model = new Vault3DModel(GetDisplayName(gltfPath), gltfPath, groups, materials, diagnostic
                , skeletons, clips) {
                Bounds = bounds,
                Pivot = pivot,
                RootTransform = rootTransform,
            };
            int vertexCount = 0;
            int triangleCount = 0;
            for (int i = 0; i < groups.Count; i++) {
                vertexCount += groups[i].Vertices.Length;
                triangleCount += groups[i].TriangleCount;
            }
            model.VertexCount = vertexCount;
            model.TriangleCount = triangleCount;

            LogDiagnostic(mod, gltfPath, diagnostic);
            return model;
        }

        private static byte[][] LoadBuffers(Mod mod, GltfDocument doc, string baseDir, Model3DDiagnostic diagnostic, string source) {
            byte[][] buffers = new byte[doc.Buffers.Count][];
            for (int i = 0; i < doc.Buffers.Count; i++) {
                GltfBuffer buffer = doc.Buffers[i];
                if (string.IsNullOrEmpty(buffer.Uri)) {
                    diagnostic.Error(source, 0, $"Buffer {i} has no uri; binary .glb chunks are not supported yet");
                    continue;
                }
                if (buffer.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
                    diagnostic.Error(source, 0, $"Buffer {i} uses data URI; embedded buffers are not supported yet");
                    continue;
                }

                string bufferPath = ResolveRelativePath(baseDir, buffer.Uri);
                if (!mod.FileExists(bufferPath)) {
                    diagnostic.Error(source, 0, $"Referenced buffer not found: '{bufferPath}'");
                    continue;
                }

                using Stream stream = mod.GetFileStream(bufferPath, true);
                if (stream == null) {
                    diagnostic.Error(source, 0, $"Failed to open buffer stream: '{bufferPath}'");
                    continue;
                }
                using MemoryStream memory = new MemoryStream();
                stream.CopyTo(memory);
                buffers[i] = memory.ToArray();
                if (buffer.ByteLength > 0 && buffers[i].Length < buffer.ByteLength) {
                    diagnostic.Warn(source, 0, $"Buffer '{bufferPath}' is shorter than declared byteLength");
                }
            }
            return buffers;
        }

        private static Dictionary<string, Model3DMaterial> BuildMaterials(GltfDocument doc, Mod mod, string baseDir
            , Model3DDiagnostic diagnostic, string source) {
            Dictionary<string, Model3DMaterial> materials = new Dictionary<string, Model3DMaterial>();
            for (int i = 0; i < doc.Materials.Count; i++) {
                GltfMaterial src = doc.Materials[i];
                string name = string.IsNullOrEmpty(src.Name) ? $"material_{i}" : src.Name;
                Model3DMaterial material = new Model3DMaterial(name);

                //颜色优先级：标准 baseColorFactor > KHR_materials_pbrSpecularGlossiness.diffuseFactor > 默认白
                float[] factor = src.BaseColorFactor ?? src.SpecGlossDiffuseFactor;
                float r = factor != null ? Clamp01(factor[0]) : 1f;
                float g = factor != null ? Clamp01(factor[1]) : 1f;
                float b = factor != null ? Clamp01(factor[2]) : 1f;
                float a = factor != null && factor.Length > 3 ? Clamp01(factor[3]) : 1f;
                material.DiffuseColor = new Color(ToByte(r), ToByte(g), ToByte(b), 255);
                material.Opacity = a;
                if (src.DoubleSided) {
                    material.RenderStateOverride = new Model3DRenderState {
                        Rasterizer = RasterizerState.CullNone,
                    };
                }

                //贴图优先级：标准 baseColorTexture > KHR_materials_pbrSpecularGlossiness.diffuseTexture
                int textureIndex = src.BaseColorTextureIndex >= 0 ? src.BaseColorTextureIndex
                    : src.SpecGlossDiffuseTextureIndex;
                if (textureIndex >= 0) {
                    TryAssignDiffuseTexture(doc, mod, baseDir, diagnostic, source, textureIndex, material);
                }

                materials[name] = material;
            }
            return materials;
        }

        //依据 texture/image 索引解析 mod 内资源路径并加载贴图；找不到则只警告，颜色作为兜底
        private static void TryAssignDiffuseTexture(GltfDocument doc, Mod mod, string baseDir, Model3DDiagnostic diagnostic
            , string source, int textureIndex, Model3DMaterial material) {
            if ((uint)textureIndex >= (uint)doc.Textures.Count) {
                diagnostic.Warn(source, 0, $"Material '{material.Name}' references invalid texture index {textureIndex}");
                return;
            }
            GltfTexture texture = doc.Textures[textureIndex];
            if ((uint)texture.Source >= (uint)doc.Images.Count) {
                diagnostic.Warn(source, 0, $"Texture {textureIndex} references invalid image index {texture.Source}");
                return;
            }
            GltfImage image = doc.Images[texture.Source];
            if (string.IsNullOrEmpty(image.Uri)) {
                diagnostic.Warn(source, 0
                    , $"Image {texture.Source} is embedded (mime '{image.MimeType}'); embedded images are not supported");
                return;
            }
            if (image.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
                diagnostic.Warn(source, 0, $"Image {texture.Source} uses data URI; embedded images are not supported");
                return;
            }

            string texturePath = ResolveRelativePath(baseDir, image.Uri);
            string assetPath = StripExtension(texturePath);
            try {
                if (!mod.HasAsset(assetPath)) {
                    diagnostic.Warn(source, 0
                        , $"Diffuse texture not found as asset: '{assetPath}' (referenced by '{image.Uri}')");
                    return;
                }
                Asset<Texture2D> asset = mod.Assets.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad);
                if (asset?.Value == null) {
                    diagnostic.Warn(source, 0, $"Failed to resolve diffuse texture asset: '{assetPath}'");
                    return;
                }
                material.DiffuseTexture = asset.Value;
                material.DiffuseTexturePath = assetPath;
            } catch (Exception ex) {
                diagnostic.Warn(source, 0, $"Failed to request diffuse texture '{assetPath}': {ex.Message}");
            }
        }

        private static string StripExtension(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            return dot > slash && dot >= 0 ? path.Substring(0, dot) : path;
        }

        //预先把每个节点的"无 R/无 ImportScale"世界矩阵计算出来，供非蒙皮 primitive 和动画的可能参考使用
        private static Matrix[] BuildNodeWorldMatrices(GltfDocument doc) {
            int n = doc.Nodes.Count;
            Matrix[] worlds = new Matrix[n];
            bool[] computed = new bool[n];
            HashSet<int> children = new HashSet<int>();
            for (int i = 0; i < n; i++) {
                foreach (int c in doc.Nodes[i].Children) {
                    children.Add(c);
                }
            }
            //从根节点开始 DFS；非节点根直接以 local 作为 world
            for (int i = 0; i < n; i++) {
                if (children.Contains(i)) {
                    continue;
                }
                Compute(doc, worlds, computed, i, Matrix.Identity);
            }
            //残留未计算的节点（被循环引用排除掉的）兜底为本地矩阵
            for (int i = 0; i < n; i++) {
                if (!computed[i]) {
                    worlds[i] = BuildNodeMatrix(doc.Nodes[i]);
                    computed[i] = true;
                }
            }
            return worlds;

            static void Compute(GltfDocument doc, Matrix[] worlds, bool[] computed, int node, Matrix parent) {
                if ((uint)node >= (uint)worlds.Length || computed[node]) {
                    return;
                }
                Matrix local = BuildNodeMatrix(doc.Nodes[node]);
                Matrix world = local * parent;
                worlds[node] = world;
                computed[node] = true;
                foreach (int child in doc.Nodes[node].Children) {
                    Compute(doc, worlds, computed, child, world);
                }
            }
        }

        private static Matrix BuildRootTransform(GltfImportOptions options) {
            Matrix flip = options.FlipYForTerraria
                ? new Matrix(1f, 0f, 0f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f)
                : Matrix.Identity;
            return flip * Matrix.CreateScale(options.ImportScale);
        }

        private static void BuildSkeletons(GltfDocument doc, AccessorReader reader
            , Model3DDiagnostic diagnostic, string source, Matrix[] nodeWorld
            , List<Model3DSkeleton> skeletons
            , Dictionary<int, List<(int skeletonIndex, int jointIndex)>> nodeToJoint) {

            //先建立 node -> parent map（仅在 skin.joints 集合内有意义）
            Dictionary<int, int> nodeParent = new Dictionary<int, int>();
            for (int n = 0; n < doc.Nodes.Count; n++) {
                foreach (int child in doc.Nodes[n].Children) {
                    nodeParent[child] = n;
                }
            }

            for (int s = 0; s < doc.Skins.Count; s++) {
                GltfSkin skin = doc.Skins[s];
                int jointCount = skin.Joints.Count;
                if (jointCount == 0) {
                    diagnostic.Warn(source, 0, $"Skin {s} has no joints, skipped");
                    skeletons.Add(new Model3DSkeleton(skin.Name, s, Array.Empty<Model3DJoint>(), Array.Empty<int>(), Array.Empty<Matrix>()));
                    continue;
                }
                //节点 -> 在 skin.joints 中的位置
                Dictionary<int, int> jointSet = new Dictionary<int, int>(jointCount);
                for (int j = 0; j < jointCount; j++) {
                    jointSet[skin.Joints[j]] = j;
                }

                Model3DJoint[] joints = new Model3DJoint[jointCount];
                int[] parents = new int[jointCount];
                Matrix[] rootAncestors = new Matrix[jointCount];
                for (int j = 0; j < jointCount; j++) {
                    int nodeIndex = skin.Joints[j];
                    GltfNode node = (uint)nodeIndex < (uint)doc.Nodes.Count
                        ? doc.Nodes[nodeIndex] : new GltfNode();
                    DecomposeNodeTRS(node, out Vector3 t, out Quaternion r, out Vector3 sc);
                    joints[j] = new Model3DJoint {
                        Name = node.Name ?? string.Empty,
                        BindTranslation = t,
                        BindRotation = r,
                        BindScale = sc,
                    };
                    int parentIndex = -1;
                    if (nodeParent.TryGetValue(nodeIndex, out int parentNode)
                        && jointSet.TryGetValue(parentNode, out int parentJoint)) {
                        parentIndex = parentJoint;
                    }
                    parents[j] = parentIndex;

                    //记录场景图父节点（注意是 glTF 节点，不是 joint 集合内）的世界矩阵
                    //——只在 root joint（parents[j] < 0）时被消费，但所有 joint 都填，方便后续扩展
                    Matrix prefix = Matrix.Identity;
                    if (nodeParent.TryGetValue(nodeIndex, out int parentNodeIdx)
                        && nodeWorld != null
                        && (uint)parentNodeIdx < (uint)nodeWorld.Length) {
                        prefix = nodeWorld[parentNodeIdx];
                    }
                    rootAncestors[j] = prefix;

                    if (!nodeToJoint.TryGetValue(nodeIndex, out var list)) {
                        list = new List<(int, int)>(1);
                        nodeToJoint[nodeIndex] = list;
                    }
                    list.Add((s, j));
                }

                Matrix[] ibm = new Matrix[jointCount];
                if (skin.InverseBindMatrices >= 0) {
                    Matrix[] read = reader.ReadMat4(skin.InverseBindMatrices);
                    if (read == null || read.Length < jointCount) {
                        diagnostic.Warn(source, 0, $"Skin {s} inverseBindMatrices length mismatch, using identity");
                        for (int j = 0; j < jointCount; j++) {
                            ibm[j] = Matrix.Identity;
                        }
                    }
                    else {
                        Array.Copy(read, ibm, jointCount);
                    }
                }
                else {
                    for (int j = 0; j < jointCount; j++) {
                        ibm[j] = Matrix.Identity;
                    }
                }

                skeletons.Add(new Model3DSkeleton(skin.Name, s, joints, parents, ibm, rootAncestors));
            }
        }

        private static void BuildAnimationClips(GltfDocument doc, AccessorReader reader
            , Model3DDiagnostic diagnostic, string source
            , Dictionary<int, List<(int skeletonIndex, int jointIndex)>> nodeToJoint
            , List<Model3DAnimationClip> clips) {

            for (int a = 0; a < doc.Animations.Count; a++) {
                GltfAnimation anim = doc.Animations[a];
                string clipName = string.IsNullOrEmpty(anim.Name) ? $"clip_{a}" : anim.Name;

                //先把所有 sampler 转换好
                Model3DAnimationSampler[] samplers = new Model3DAnimationSampler[anim.Samplers.Count];
                for (int sm = 0; sm < anim.Samplers.Count; sm++) {
                    GltfAnimationSampler src = anim.Samplers[sm];
                    samplers[sm] = BuildAnimationSampler(reader, diagnostic, source, src, clipName, sm);
                }

                List<Model3DAnimationChannel> channelList = new List<Model3DAnimationChannel>(anim.Channels.Count);
                float duration = 0f;
                bool warnedCubic = false;
                bool warnedWeights = false;

                for (int c = 0; c < anim.Channels.Count; c++) {
                    GltfAnimationChannel src = anim.Channels[c];
                    if (src.Sampler < 0 || src.Sampler >= samplers.Length) {
                        continue;
                    }
                    Model3DAnimationSampler sampler = samplers[src.Sampler];
                    if (sampler == null) {
                        continue;
                    }
                    if (sampler.Interpolation == Model3DInterpolation.CubicSpline && !warnedCubic) {
                        diagnostic.Warn(source, 0, $"Animation '{clipName}' uses CubicSpline interpolation; fallback to LINEAR");
                        warnedCubic = true;
                    }
                    if (!TryParseAnimationPath(src.TargetPath, out Model3DAnimationPath path)) {
                        diagnostic.Warn(source, 0, $"Animation '{clipName}' channel {c} has unknown path '{src.TargetPath}', skipped");
                        continue;
                    }
                    if (path == Model3DAnimationPath.Weights) {
                        if (!warnedWeights) {
                            diagnostic.Warn(source, 0, $"Animation '{clipName}' targets morph weights; not supported, channels skipped");
                            warnedWeights = true;
                        }
                        continue;
                    }
                    if (src.TargetNode < 0 || !nodeToJoint.TryGetValue(src.TargetNode, out var bindings) || bindings == null) {
                        //目标节点不属于任何 skin，无法驱动蒙皮
                        continue;
                    }
                    //节点若属于多个 skin（不常见），为每个 binding 复制一份 channel
                    for (int b = 0; b < bindings.Count; b++) {
                        (int skeletonIndex, int jointIndex) = bindings[b];
                        channelList.Add(new Model3DAnimationChannel(skeletonIndex, jointIndex, path, sampler));
                    }
                    if (sampler.EndTime > duration) {
                        duration = sampler.EndTime;
                    }
                }

                clips.Add(new Model3DAnimationClip(clipName, duration, channelList.ToArray()));
            }
        }

        private static Model3DAnimationSampler BuildAnimationSampler(AccessorReader reader
            , Model3DDiagnostic diagnostic, string source, GltfAnimationSampler src, string clipName, int samplerIndex) {

            if (src.Input < 0 || src.Output < 0) {
                diagnostic.Warn(source, 0, $"Animation '{clipName}' sampler {samplerIndex} missing input/output, ignored");
                return null;
            }
            float[] times = reader.ReadScalarFloat(src.Input);
            if (times == null || times.Length == 0) {
                diagnostic.Warn(source, 0, $"Animation '{clipName}' sampler {samplerIndex} input accessor empty, ignored");
                return null;
            }
            int stride = reader.GetAccessorStrideAsFloats(src.Output);
            if (stride <= 0) {
                diagnostic.Warn(source, 0, $"Animation '{clipName}' sampler {samplerIndex} output accessor has unknown stride, ignored");
                return null;
            }
            float[] values = reader.ReadOutputFloats(src.Output);
            if (values == null || values.Length == 0) {
                diagnostic.Warn(source, 0, $"Animation '{clipName}' sampler {samplerIndex} output accessor empty, ignored");
                return null;
            }
            Model3DInterpolation interpolation = ParseInterpolation(src.Interpolation);
            return new Model3DAnimationSampler(times, values, stride, interpolation);
        }

        private static bool TryParseAnimationPath(string path, out Model3DAnimationPath result) {
            switch (path) {
                case "translation": result = Model3DAnimationPath.Translation; return true;
                case "rotation": result = Model3DAnimationPath.Rotation; return true;
                case "scale": result = Model3DAnimationPath.Scale; return true;
                case "weights": result = Model3DAnimationPath.Weights; return true;
                default: result = Model3DAnimationPath.Translation; return false;
            }
        }

        private static Model3DInterpolation ParseInterpolation(string raw) {
            switch (raw) {
                case "STEP": return Model3DInterpolation.Step;
                case "CUBICSPLINE": return Model3DInterpolation.CubicSpline;
                default: return Model3DInterpolation.Linear;
            }
        }

        private static void DecomposeNodeTRS(GltfNode node, out Vector3 translation, out Quaternion rotation, out Vector3 scale) {
            if (node.Matrix != null) {
                //节点用 matrix 指定时仍然 decompose 一次，作为 bind pose
                Matrix m = MatrixFromArray(node.Matrix);
                if (!m.Decompose(out scale, out rotation, out translation)) {
                    translation = Vector3.Zero;
                    rotation = Quaternion.Identity;
                    scale = Vector3.One;
                }
                return;
            }
            translation = node.Translation != null ? new Vector3(node.Translation[0], node.Translation[1], node.Translation[2]) : Vector3.Zero;
            rotation = node.Rotation != null
                ? new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
                : Quaternion.Identity;
            scale = node.Scale != null ? new Vector3(node.Scale[0], node.Scale[1], node.Scale[2]) : Vector3.One;
        }

        private static void TraverseScene(GltfDocument doc, AccessorReader reader, Dictionary<string, Model3DMaterial> materials
            , GltfImportOptions options, Model3DDiagnostic diagnostic, string source
            , List<Model3DMeshGroup> groups, Matrix root
            , ref Vector3 min, ref Vector3 max, ref bool boundsTouched
            , bool hasSkins, Matrix rootTransform) {

            if (doc.Scenes.Count == 0) {
                diagnostic.Warn(source, 0, "glTF has no scenes; importing all root-level nodes");
                HashSet<int> childNodes = new HashSet<int>();
                for (int i = 0; i < doc.Nodes.Count; i++) {
                    foreach (int child in doc.Nodes[i].Children) {
                        childNodes.Add(child);
                    }
                }
                for (int i = 0; i < doc.Nodes.Count; i++) {
                    if (!childNodes.Contains(i)) {
                        TraverseNode(doc, reader, materials, options, diagnostic, source, i, root, groups
                            , ref min, ref max, ref boundsTouched, hasSkins, rootTransform);
                    }
                }
                return;
            }

            int sceneIndex = doc.SceneIndex >= 0 && doc.SceneIndex < doc.Scenes.Count ? doc.SceneIndex : 0;
            GltfScene scene = doc.Scenes[sceneIndex];
            for (int i = 0; i < scene.Nodes.Count; i++) {
                TraverseNode(doc, reader, materials, options, diagnostic, source, scene.Nodes[i], root, groups
                    , ref min, ref max, ref boundsTouched, hasSkins, rootTransform);
            }
        }

        private static void TraverseNode(GltfDocument doc, AccessorReader reader, Dictionary<string, Model3DMaterial> materials
            , GltfImportOptions options, Model3DDiagnostic diagnostic, string source
            , int nodeIndex, Matrix parentWorld, List<Model3DMeshGroup> groups
            , ref Vector3 min, ref Vector3 max, ref bool boundsTouched
            , bool hasSkins, Matrix rootTransform) {

            if (nodeIndex < 0 || nodeIndex >= doc.Nodes.Count) {
                diagnostic.Warn(source, 0, $"Scene references invalid node index {nodeIndex}");
                return;
            }

            GltfNode node = doc.Nodes[nodeIndex];
            Matrix local = options.ApplyNodeTransforms ? BuildNodeMatrix(node) : Matrix.Identity;
            Matrix world = local * parentWorld;

            if (node.Mesh >= 0) {
                if (node.Mesh >= doc.Meshes.Count) {
                    diagnostic.Warn(source, 0, $"Node '{node.Name}' references invalid mesh index {node.Mesh}");
                }
                else {
                    int skinIndex = hasSkins && node.Skin >= 0 && node.Skin < doc.Skins.Count ? node.Skin : -1;
                    //glTF 规则：蒙皮 mesh 容器节点的变换在蒙皮时被忽略，故 skinned primitive 不使用 world 矩阵
                    Matrix effectiveWorld = skinIndex >= 0 ? Matrix.Identity : world;
                    BuildMesh(doc, reader, materials, options, diagnostic, source, doc.Meshes[node.Mesh], effectiveWorld, groups
                        , ref min, ref max, ref boundsTouched, hasSkins, rootTransform, skinIndex);
                }
            }

            for (int i = 0; i < node.Children.Count; i++) {
                TraverseNode(doc, reader, materials, options, diagnostic, source, node.Children[i], world, groups
                    , ref min, ref max, ref boundsTouched, hasSkins, rootTransform);
            }
        }

        private static void BuildMesh(GltfDocument doc, AccessorReader reader, Dictionary<string, Model3DMaterial> materials
            , GltfImportOptions options, Model3DDiagnostic diagnostic, string source, GltfMesh mesh, Matrix world
            , List<Model3DMeshGroup> groups, ref Vector3 min, ref Vector3 max, ref bool boundsTouched
            , bool hasSkins, Matrix rootTransform, int skinIndex) {

            for (int p = 0; p < mesh.Primitives.Count; p++) {
                GltfPrimitive primitive = mesh.Primitives[p];
                if (primitive.Mode != ModeTriangles) {
                    diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} uses unsupported mode {primitive.Mode}, skipped");
                    continue;
                }
                if (!primitive.Attributes.TryGetValue("POSITION", out int positionAccessor)) {
                    diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} has no POSITION accessor, skipped");
                    continue;
                }

                Vector3[] positions = reader.ReadVec3(positionAccessor, ComponentFloat, "VEC3");
                if (positions == null || positions.Length == 0) {
                    diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} has empty POSITION accessor, skipped");
                    continue;
                }

                Vector3[] normals = null;
                if (primitive.Attributes.TryGetValue("NORMAL", out int normalAccessor)) {
                    normals = reader.ReadVec3(normalAccessor, ComponentFloat, "VEC3");
                    if (normals != null && normals.Length != positions.Length) {
                        diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} NORMAL count differs from POSITION count, ignored");
                        normals = null;
                    }
                }

                Vector2[] texCoords = null;
                if (primitive.Attributes.TryGetValue("TEXCOORD_0", out int uvAccessor)) {
                    texCoords = reader.ReadVec2(uvAccessor, ComponentFloat, "VEC2");
                    if (texCoords != null && texCoords.Length != positions.Length) {
                        diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} TEXCOORD_0 count differs from POSITION count, ignored");
                        texCoords = null;
                    }
                }

                Joint4[] jointIndices = null;
                Vector4[] jointWeights = null;
                if (skinIndex >= 0) {
                    if (primitive.Attributes.TryGetValue("JOINTS_0", out int jointsAccessor)
                        && primitive.Attributes.TryGetValue("WEIGHTS_0", out int weightsAccessor)) {
                        jointIndices = reader.ReadJoint4(jointsAccessor);
                        jointWeights = reader.ReadVec4Float(weightsAccessor);
                        if (jointIndices == null || jointWeights == null
                            || jointIndices.Length != positions.Length || jointWeights.Length != positions.Length) {
                            diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} JOINTS/WEIGHTS length mismatch, skinning disabled for this primitive");
                            jointIndices = null;
                            jointWeights = null;
                        }
                        else {
                            NormalizeWeights(jointWeights);
                        }
                    }
                    else {
                        diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} references skin {skinIndex} but lacks JOINTS_0/WEIGHTS_0, skinning disabled");
                    }
                }

                int[] indices = primitive.Indices >= 0 ? reader.ReadIndices(primitive.Indices) : BuildSequentialIndices(positions.Length);
                if (indices == null || indices.Length == 0) {
                    diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} has no drawable indices, skipped");
                    continue;
                }

                VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[positions.Length];
                bool isSkinned = jointIndices != null && jointWeights != null;
                bool bakeRoot = !hasSkins;   //只有完全非蒙皮模型才把 R 烘焙进顶点

                for (int i = 0; i < positions.Length; i++) {
                    Vector3 pos = Vector3.Transform(positions[i], world);
                    if (bakeRoot) {
                        pos = options.ApplyImportScale(options.ApplyAxis(pos));
                    }
                    Vector3 normal = normals != null
                        ? (bakeRoot ? options.ApplyAxisNormal(Vector3.TransformNormal(normals[i], world))
                                    : Vector3.TransformNormal(normals[i], world))
                        : Vector3.Zero;
                    if (normal.LengthSquared() > 1e-6f) {
                        normal.Normalize();
                    }
                    Vector2 uv = texCoords != null ? texCoords[i] : Vector2.Zero;
                    if (options.FlipTextureV) {
                        uv.Y = 1f - uv.Y;
                    }
                    vertices[i] = new VertexPositionNormalTexture(pos, normal, uv);

                    //包围盒按"最终可见空间"算：bakeRoot 时 pos 已经在最终空间；否则需要把 RootTransform 应用一次再统计
                    Vector3 visPos = bakeRoot ? pos : Vector3.Transform(pos, rootTransform);
                    if (!boundsTouched) {
                        min = visPos;
                        max = visPos;
                        boundsTouched = true;
                    }
                    else {
                        min = Vector3.Min(min, visPos);
                        max = Vector3.Max(max, visPos);
                    }
                }

                ValidateIndices(indices, vertices.Length, diagnostic, source, mesh.Name, p);
                if (normals == null && options.GenerateMissingNormals) {
                    GenerateNormals(vertices, indices);
                }
                NormalizeMissingNormals(vertices);

                Model3DMaterial material = ResolveMaterial(doc, materials, primitive.Material);
                string materialName = material?.Name ?? string.Empty;
                Model3DMeshGroup group = new Model3DMeshGroup(materialName, vertices, indices) {
                    Material = material,
                };
                if (isSkinned) {
                    //bind 顶点与可绘制顶点同源（蒙皮模型下 Vertices 也充当"无动画兜底姿态"）
                    VertexPositionNormalTexture[] bind = new VertexPositionNormalTexture[vertices.Length];
                    Array.Copy(vertices, bind, vertices.Length);
                    group.BindVertices = bind;
                    group.JointIndices = jointIndices;
                    group.JointWeights = jointWeights;
                    group.SkinIndex = skinIndex;
                }
                groups.Add(group);
            }
        }

        //把每个顶点 4 权重归一化为和为 1；权重和小于 epsilon 时回落到 (1,0,0,0)
        private static void NormalizeWeights(Vector4[] weights) {
            for (int i = 0; i < weights.Length; i++) {
                Vector4 w = weights[i];
                float sum = w.X + w.Y + w.Z + w.W;
                if (sum > 1e-5f) {
                    weights[i] = new Vector4(w.X / sum, w.Y / sum, w.Z / sum, w.W / sum);
                }
                else {
                    weights[i] = new Vector4(1f, 0f, 0f, 0f);
                }
            }
        }

        private static Model3DMaterial ResolveMaterial(GltfDocument doc, Dictionary<string, Model3DMaterial> materials, int materialIndex) {
            if (materialIndex < 0 || materialIndex >= doc.Materials.Count) {
                return null;
            }
            string name = string.IsNullOrEmpty(doc.Materials[materialIndex].Name) ? $"material_{materialIndex}" : doc.Materials[materialIndex].Name;
            materials.TryGetValue(name, out Model3DMaterial material);
            return material;
        }

        private static void ValidateIndices(int[] indices, int vertexCount, Model3DDiagnostic diagnostic, string source, string meshName, int primitiveIndex) {
            for (int i = 0; i < indices.Length; i++) {
                if (indices[i] < 0 || indices[i] >= vertexCount) {
                    diagnostic.Warn(source, 0, $"Mesh '{meshName}' primitive {primitiveIndex} contains out-of-range index {indices[i]}");
                    return;
                }
            }
        }

        private static void GenerateNormals(VertexPositionNormalTexture[] vertices, int[] indices) {
            Vector3[] accum = new Vector3[vertices.Length];
            for (int i = 0; i + 2 < indices.Length; i += 3) {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                if ((uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length || (uint)i2 >= (uint)vertices.Length) {
                    continue;
                }
                Vector3 a = vertices[i0].Position;
                Vector3 b = vertices[i1].Position;
                Vector3 c = vertices[i2].Position;
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() <= 1e-6f) {
                    continue;
                }
                accum[i0] += normal;
                accum[i1] += normal;
                accum[i2] += normal;
            }

            for (int i = 0; i < vertices.Length; i++) {
                if (accum[i].LengthSquared() <= 1e-6f) {
                    continue;
                }
                Vector3 n = accum[i];
                n.Normalize();
                VertexPositionNormalTexture v = vertices[i];
                v.Normal = n;
                vertices[i] = v;
            }
        }

        private static void NormalizeMissingNormals(VertexPositionNormalTexture[] vertices) {
            for (int i = 0; i < vertices.Length; i++) {
                VertexPositionNormalTexture v = vertices[i];
                if (v.Normal.LengthSquared() <= 1e-6f) {
                    v.Normal = Vector3.UnitZ;
                }
                else {
                    Vector3 n = v.Normal;
                    n.Normalize();
                    v.Normal = n;
                }
                vertices[i] = v;
            }
        }

        private static int[] BuildSequentialIndices(int count) {
            int[] result = new int[count];
            for (int i = 0; i < count; i++) {
                result[i] = i;
            }
            return result;
        }

        private static Matrix BuildNodeMatrix(GltfNode node) {
            if (node.Matrix != null) {
                return MatrixFromArray(node.Matrix);
            }

            Vector3 scale = node.Scale != null ? new Vector3(node.Scale[0], node.Scale[1], node.Scale[2]) : Vector3.One;
            Quaternion rotation = node.Rotation != null
                ? new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
                : Quaternion.Identity;
            Vector3 translation = node.Translation != null ? new Vector3(node.Translation[0], node.Translation[1], node.Translation[2]) : Vector3.Zero;
            return Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
        }

        private static Matrix MatrixFromArray(float[] m) {
            //glTF stores matrices column-major. XNA's row-vector transform expects the same
            //contiguous values in M11..M44 for equivalent Vector3.Transform behavior.
            return new Matrix(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
        }

        private sealed class AccessorReader
        {
            private readonly GltfDocument _doc;
            private readonly byte[][] _buffers;
            private readonly Model3DDiagnostic _diagnostic;
            private readonly string _source;

            public AccessorReader(GltfDocument doc, byte[][] buffers, Model3DDiagnostic diagnostic, string source) {
                _doc = doc;
                _buffers = buffers;
                _diagnostic = diagnostic;
                _source = source;
            }

            public Vector3[] ReadVec3(int accessorIndex, int componentType, string type) {
                if (!TryGetAccessor(accessorIndex, componentType, type, out GltfAccessor accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                Vector3[] result = new Vector3[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    result[i] = new Vector3(
                        ReadSingle(buffer, offset),
                        ReadSingle(buffer, offset + 4),
                        ReadSingle(buffer, offset + 8));
                }
                return result;
            }

            public Vector2[] ReadVec2(int accessorIndex, int componentType, string type) {
                if (!TryGetAccessor(accessorIndex, componentType, type, out GltfAccessor accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                Vector2[] result = new Vector2[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    result[i] = new Vector2(ReadSingle(buffer, offset), ReadSingle(buffer, offset + 4));
                }
                return result;
            }

            public Vector4[] ReadVec4Float(int accessorIndex) {
                if (!TryGetAccessor(accessorIndex, ComponentFloat, "VEC4", out GltfAccessor accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                Vector4[] result = new Vector4[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    result[i] = new Vector4(
                        ReadSingle(buffer, offset),
                        ReadSingle(buffer, offset + 4),
                        ReadSingle(buffer, offset + 8),
                        ReadSingle(buffer, offset + 12));
                }
                return result;
            }

            public Joint4[] ReadJoint4(int accessorIndex) {
                if (accessorIndex < 0 || accessorIndex >= _doc.Accessors.Count) {
                    _diagnostic.Warn(_source, 0, $"Invalid joints accessor {accessorIndex}");
                    return null;
                }
                GltfAccessor accessor = _doc.Accessors[accessorIndex];
                if (accessor.Type != "VEC4") {
                    _diagnostic.Warn(_source, 0, $"Joints accessor {accessorIndex} type '{accessor.Type}', expected VEC4");
                    return null;
                }
                if (accessor.ComponentType != ComponentUnsignedByte && accessor.ComponentType != ComponentUnsignedShort) {
                    _diagnostic.Warn(_source, 0, $"Joints accessor {accessorIndex} componentType {accessor.ComponentType}, expected UNSIGNED_BYTE or UNSIGNED_SHORT");
                    return null;
                }
                if (!TryGetAccessor(accessorIndex, accessor.ComponentType, "VEC4", out accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                Joint4[] result = new Joint4[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    if (accessor.ComponentType == ComponentUnsignedByte) {
                        result[i] = new Joint4(buffer[offset], buffer[offset + 1], buffer[offset + 2], buffer[offset + 3]);
                    }
                    else {
                        result[i] = new Joint4(
                            BitConverter.ToUInt16(buffer, offset),
                            BitConverter.ToUInt16(buffer, offset + 2),
                            BitConverter.ToUInt16(buffer, offset + 4),
                            BitConverter.ToUInt16(buffer, offset + 6));
                    }
                }
                return result;
            }

            public Matrix[] ReadMat4(int accessorIndex) {
                if (!TryGetAccessor(accessorIndex, ComponentFloat, "MAT4", out GltfAccessor accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                Matrix[] result = new Matrix[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    result[i] = new Matrix(
                        ReadSingle(buffer, offset + 0), ReadSingle(buffer, offset + 4), ReadSingle(buffer, offset + 8), ReadSingle(buffer, offset + 12),
                        ReadSingle(buffer, offset + 16), ReadSingle(buffer, offset + 20), ReadSingle(buffer, offset + 24), ReadSingle(buffer, offset + 28),
                        ReadSingle(buffer, offset + 32), ReadSingle(buffer, offset + 36), ReadSingle(buffer, offset + 40), ReadSingle(buffer, offset + 44),
                        ReadSingle(buffer, offset + 48), ReadSingle(buffer, offset + 52), ReadSingle(buffer, offset + 56), ReadSingle(buffer, offset + 60));
                }
                return result;
            }

            public float[] ReadScalarFloat(int accessorIndex) {
                if (!TryGetAccessor(accessorIndex, ComponentFloat, "SCALAR", out GltfAccessor accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                float[] result = new float[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    result[i] = ReadSingle(buffer, offset);
                }
                return result;
            }

            //把 output accessor 当作连续的 float 数组读出来，无视具体 type（采样器内部用 stride 切分）
            public float[] ReadOutputFloats(int accessorIndex) {
                if (accessorIndex < 0 || accessorIndex >= _doc.Accessors.Count) {
                    _diagnostic.Warn(_source, 0, $"Invalid animation output accessor {accessorIndex}");
                    return null;
                }
                GltfAccessor accessor = _doc.Accessors[accessorIndex];
                if (accessor.ComponentType != ComponentFloat) {
                    _diagnostic.Warn(_source, 0, $"Animation output accessor {accessorIndex} component type {accessor.ComponentType}, expected FLOAT");
                    return null;
                }
                int componentCount = ComponentCount(accessor.Type);
                if (componentCount <= 0) {
                    _diagnostic.Warn(_source, 0, $"Animation output accessor {accessorIndex} unsupported type '{accessor.Type}'");
                    return null;
                }
                if (!TryGetAccessor(accessorIndex, ComponentFloat, accessor.Type, out accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }
                int totalFloats = accessor.Count * componentCount;
                float[] result = new float[totalFloats];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    for (int c = 0; c < componentCount; c++) {
                        result[i * componentCount + c] = ReadSingle(buffer, offset + c * 4);
                    }
                }
                return result;
            }

            public int GetAccessorStrideAsFloats(int accessorIndex) {
                if (accessorIndex < 0 || accessorIndex >= _doc.Accessors.Count) {
                    return 0;
                }
                GltfAccessor accessor = _doc.Accessors[accessorIndex];
                return ComponentCount(accessor.Type);
            }

            public int[] ReadIndices(int accessorIndex) {
                if (accessorIndex < 0 || accessorIndex >= _doc.Accessors.Count) {
                    _diagnostic.Warn(_source, 0, $"Invalid indices accessor {accessorIndex}");
                    return null;
                }
                GltfAccessor accessor = _doc.Accessors[accessorIndex];
                if (accessor.Type != "SCALAR") {
                    _diagnostic.Warn(_source, 0, $"Indices accessor {accessorIndex} is '{accessor.Type}', expected SCALAR");
                    return null;
                }
                if (!TryGetAccessor(accessorIndex, accessor.ComponentType, "SCALAR", out accessor, out GltfBufferView view, out byte[] buffer)) {
                    return null;
                }

                int[] result = new int[accessor.Count];
                int stride = GetStride(accessor, view);
                int start = view.ByteOffset + accessor.ByteOffset;
                for (int i = 0; i < accessor.Count; i++) {
                    int offset = start + i * stride;
                    switch (accessor.ComponentType) {
                        case ComponentUnsignedByte:
                            result[i] = buffer[offset];
                            break;
                        case ComponentUnsignedShort:
                            result[i] = BitConverter.ToUInt16(buffer, offset);
                            break;
                        case ComponentUnsignedInt:
                            uint value = BitConverter.ToUInt32(buffer, offset);
                            result[i] = value > int.MaxValue ? -1 : (int)value;
                            break;
                        default:
                            _diagnostic.Warn(_source, 0, $"Unsupported index component type {accessor.ComponentType}");
                            return null;
                    }
                }
                return result;
            }

            private bool TryGetAccessor(int accessorIndex, int componentType, string type
                , out GltfAccessor accessor, out GltfBufferView view, out byte[] buffer) {
                accessor = null;
                view = null;
                buffer = null;
                if (accessorIndex < 0 || accessorIndex >= _doc.Accessors.Count) {
                    _diagnostic.Warn(_source, 0, $"Invalid accessor index {accessorIndex}");
                    return false;
                }
                accessor = _doc.Accessors[accessorIndex];
                if (accessor.Sparse) {
                    _diagnostic.Warn(_source, 0, $"Sparse accessor {accessorIndex} is not supported");
                    return false;
                }
                if (accessor.ComponentType != componentType) {
                    _diagnostic.Warn(_source, 0, $"Accessor {accessorIndex} component type {accessor.ComponentType}, expected {componentType}");
                    return false;
                }
                if (accessor.Type != type) {
                    _diagnostic.Warn(_source, 0, $"Accessor {accessorIndex} type '{accessor.Type}', expected {type}");
                    return false;
                }
                if (accessor.BufferView < 0 || accessor.BufferView >= _doc.BufferViews.Count) {
                    _diagnostic.Warn(_source, 0, $"Accessor {accessorIndex} references invalid bufferView {accessor.BufferView}");
                    return false;
                }
                view = _doc.BufferViews[accessor.BufferView];
                if (view.Buffer < 0 || view.Buffer >= _buffers.Length || _buffers[view.Buffer] == null) {
                    _diagnostic.Warn(_source, 0, $"bufferView {accessor.BufferView} references unavailable buffer {view.Buffer}");
                    return false;
                }
                buffer = _buffers[view.Buffer];
                int stride = GetStride(accessor, view);
                int needed = view.ByteOffset + accessor.ByteOffset + (accessor.Count - 1) * stride + ElementSize(accessor);
                if (needed > buffer.Length) {
                    _diagnostic.Warn(_source, 0, $"Accessor {accessorIndex} reads beyond buffer length");
                    return false;
                }
                return true;
            }

            private static int GetStride(GltfAccessor accessor, GltfBufferView view) {
                return view.ByteStride > 0 ? view.ByteStride : ElementSize(accessor);
            }

            private static int ElementSize(GltfAccessor accessor) {
                return ComponentSize(accessor.ComponentType) * ComponentCount(accessor.Type);
            }

            private static int ComponentSize(int componentType) {
                return componentType switch {
                    ComponentByte => 1,
                    ComponentUnsignedByte => 1,
                    ComponentShort => 2,
                    ComponentUnsignedShort => 2,
                    ComponentUnsignedInt => 4,
                    ComponentFloat => 4,
                    _ => 0,
                };
            }

            private static int ComponentCount(string type) {
                return type switch {
                    "SCALAR" => 1,
                    "VEC2" => 2,
                    "VEC3" => 3,
                    "VEC4" => 4,
                    "MAT2" => 4,
                    "MAT3" => 9,
                    "MAT4" => 16,
                    _ => 0,
                };
            }

            private static float ReadSingle(byte[] buffer, int offset) {
                return BitConverter.ToSingle(buffer, offset);
            }
        }

        private static bool TryResolveGltfPath(Mod mod, string path, out string resolved) {
            resolved = path;
            if (mod.FileExists(path)) {
                return true;
            }
            string withExt = path + ".gltf";
            if (mod.FileExists(withExt)) {
                resolved = withExt;
                return true;
            }
            return false;
        }

        private static string NormalizeSlashes(string path) {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string GetDirectory(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                return string.Empty;
            }
            int idx = filePath.LastIndexOf('/');
            return idx < 0 ? string.Empty : filePath.Substring(0, idx + 1);
        }

        private static string ResolveRelativePath(string baseDir, string relativePath) {
            string normalizedRel = NormalizeSlashes(relativePath);
            if (normalizedRel.StartsWith("/")) {
                normalizedRel = normalizedRel.Substring(1);
            }
            string[] parts = (baseDir + normalizedRel).Split('/');
            List<string> stack = new List<string>();
            for (int i = 0; i < parts.Length; i++) {
                string part = parts[i];
                if (part.Length == 0 || part == ".") {
                    continue;
                }
                if (part == "..") {
                    if (stack.Count > 0) {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    continue;
                }
                stack.Add(part);
            }
            return string.Join("/", stack);
        }

        private static string GetDisplayName(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            int slash = path.LastIndexOf('/');
            string fileName = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = fileName.LastIndexOf('.');
            return dot >= 0 ? fileName.Substring(0, dot) : fileName;
        }

        private static float Clamp01(float value) {
            return MathHelper.Clamp(value, 0f, 1f);
        }

        private static byte ToByte(float value) {
            int scaled = (int)(value * 255f + 0.5f);
            if (scaled < 0) {
                return 0;
            }
            if (scaled > 255) {
                return 255;
            }
            return (byte)scaled;
        }

        private static void LogDiagnostic(Mod mod, string source, Model3DDiagnostic diagnostic) {
            if (diagnostic == null || diagnostic.Entries.Count == 0) {
                return;
            }
            if (diagnostic.HasErrors) {
                VaultMod.LoggerError($"[GltfModelLoader:{mod.Name}/{source}]"
                    , $"glTF load reported {diagnostic.ErrorCount} error(s), {diagnostic.WarningCount} warning(s):\n{diagnostic.Format()}");
            }
            else if (diagnostic.WarningCount > 0) {
                VaultMod.Instance?.Logger.Debug($"[GltfModelLoader:{mod.Name}/{source}] "
                    + $"{diagnostic.WarningCount} warning(s):\n{diagnostic.Format()}");
            }
        }
    }
}
