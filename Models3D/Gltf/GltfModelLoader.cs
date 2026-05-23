using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria.ModLoader;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Gltf
{
    /// <summary>
    /// 本地最小 glTF 2.0 静态网格导入器
    /// </summary>
    public static class GltfModelLoader
    {
        private const int ComponentByte = 5121;
        private const int ComponentUnsignedShort = 5123;
        private const int ComponentUnsignedInt = 5125;
        private const int ComponentFloat = 5126;
        private const int ModeTriangles = 4;

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

            Dictionary<string, Model3DMaterial> materials = BuildMaterials(doc);
            List<Model3DMeshGroup> groups = new List<Model3DMeshGroup>();
            Vector3 min = new Vector3(float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity);
            bool boundsTouched = false;

            TraverseScene(doc, accessorReader, materials, options, diagnostic, gltfPath
                , groups, Matrix.Identity, ref min, ref max, ref boundsTouched);

            BoundingBox bounds = boundsTouched ? new BoundingBox(min, max) : new BoundingBox(Vector3.Zero, Vector3.Zero);
            Vector3 pivot = options.CenterPivot && boundsTouched ? (min + max) * 0.5f : Vector3.Zero;
            Vault3DModel model = new Vault3DModel(GetDisplayName(gltfPath), gltfPath, groups, materials, diagnostic) {
                Bounds = bounds,
                Pivot = pivot,
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

        private static Dictionary<string, Model3DMaterial> BuildMaterials(GltfDocument doc) {
            Dictionary<string, Model3DMaterial> materials = new Dictionary<string, Model3DMaterial>();
            for (int i = 0; i < doc.Materials.Count; i++) {
                GltfMaterial src = doc.Materials[i];
                string name = string.IsNullOrEmpty(src.Name) ? $"material_{i}" : src.Name;
                Model3DMaterial material = new Model3DMaterial(name);
                float[] factor = src.BaseColorFactor;
                float r = factor != null ? Clamp01(factor[0]) : 1f;
                float g = factor != null ? Clamp01(factor[1]) : 1f;
                float b = factor != null ? Clamp01(factor[2]) : 1f;
                float a = factor != null ? Clamp01(factor[3]) : 1f;
                material.DiffuseColor = new Color(ToByte(r), ToByte(g), ToByte(b), 255);
                material.Opacity = a;
                if (src.DoubleSided) {
                    material.RenderStateOverride = new Model3DRenderState {
                        Rasterizer = RasterizerState.CullNone,
                    };
                }
                materials[name] = material;
            }
            return materials;
        }

        private static void TraverseScene(GltfDocument doc, AccessorReader reader, Dictionary<string, Model3DMaterial> materials
            , GltfImportOptions options, Model3DDiagnostic diagnostic, string source
            , List<Model3DMeshGroup> groups, Matrix root
            , ref Vector3 min, ref Vector3 max, ref bool boundsTouched) {

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
                            , ref min, ref max, ref boundsTouched);
                    }
                }
                return;
            }

            int sceneIndex = doc.SceneIndex >= 0 && doc.SceneIndex < doc.Scenes.Count ? doc.SceneIndex : 0;
            GltfScene scene = doc.Scenes[sceneIndex];
            for (int i = 0; i < scene.Nodes.Count; i++) {
                TraverseNode(doc, reader, materials, options, diagnostic, source, scene.Nodes[i], root, groups
                    , ref min, ref max, ref boundsTouched);
            }
        }

        private static void TraverseNode(GltfDocument doc, AccessorReader reader, Dictionary<string, Model3DMaterial> materials
            , GltfImportOptions options, Model3DDiagnostic diagnostic, string source
            , int nodeIndex, Matrix parentWorld, List<Model3DMeshGroup> groups
            , ref Vector3 min, ref Vector3 max, ref bool boundsTouched) {

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
                    BuildMesh(doc, reader, materials, options, diagnostic, source, doc.Meshes[node.Mesh], world, groups
                        , ref min, ref max, ref boundsTouched);
                }
            }

            for (int i = 0; i < node.Children.Count; i++) {
                TraverseNode(doc, reader, materials, options, diagnostic, source, node.Children[i], world, groups
                    , ref min, ref max, ref boundsTouched);
            }
        }

        private static void BuildMesh(GltfDocument doc, AccessorReader reader, Dictionary<string, Model3DMaterial> materials
            , GltfImportOptions options, Model3DDiagnostic diagnostic, string source, GltfMesh mesh, Matrix world
            , List<Model3DMeshGroup> groups, ref Vector3 min, ref Vector3 max, ref bool boundsTouched) {

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

                int[] indices = primitive.Indices >= 0 ? reader.ReadIndices(primitive.Indices) : BuildSequentialIndices(positions.Length);
                if (indices == null || indices.Length == 0) {
                    diagnostic.Warn(source, 0, $"Mesh '{mesh.Name}' primitive {p} has no drawable indices, skipped");
                    continue;
                }

                VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[positions.Length];
                for (int i = 0; i < positions.Length; i++) {
                    Vector3 pos = Vector3.Transform(positions[i], world);
                    pos = options.ApplyImportScale(options.ApplyAxis(pos));
                    Vector3 normal = normals != null ? options.ApplyAxisNormal(Vector3.TransformNormal(normals[i], world)) : Vector3.Zero;
                    if (normal.LengthSquared() > 1e-6f) {
                        normal.Normalize();
                    }
                    Vector2 uv = texCoords != null ? texCoords[i] : Vector2.Zero;
                    if (options.FlipTextureV) {
                        uv.Y = 1f - uv.Y;
                    }
                    vertices[i] = new VertexPositionNormalTexture(pos, normal, uv);

                    if (!boundsTouched) {
                        min = pos;
                        max = pos;
                        boundsTouched = true;
                    }
                    else {
                        min = Vector3.Min(min, pos);
                        max = Vector3.Max(max, pos);
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
                groups.Add(group);
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
                float[] m = node.Matrix;
                // glTF stores matrices column-major. XNA's row-vector transform expects the same
                // contiguous values in M11..M44 for equivalent Vector3.Transform behavior.
                return new Matrix(
                    m[0], m[1], m[2], m[3],
                    m[4], m[5], m[6], m[7],
                    m[8], m[9], m[10], m[11],
                    m[12], m[13], m[14], m[15]);
            }

            Vector3 scale = node.Scale != null ? new Vector3(node.Scale[0], node.Scale[1], node.Scale[2]) : Vector3.One;
            Quaternion rotation = node.Rotation != null
                ? new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
                : Quaternion.Identity;
            Vector3 translation = node.Translation != null ? new Vector3(node.Translation[0], node.Translation[1], node.Translation[2]) : Vector3.Zero;
            return Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
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
                        case ComponentByte:
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
#pragma warning restore CS1591
