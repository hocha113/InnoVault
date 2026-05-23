using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 让 <see cref="VaultLoadenAttribute"/> 支持 <see cref="VaultObjModel"/> 类型字段的自动加载
    /// <br/>实际加载工作流：读取 OBJ -> 解析 -> 读取 MTL -> 加载贴图 -> 构造 GPU 顶点
    /// </summary>
    public sealed class ObjModelLoadenHandle : VaultLoadenHandle
    {
        /// <inheritdoc/>
        public override Type TargetType => typeof(VaultObjModel);
        /// <inheritdoc/>
        public override int Priority => 0;
        /// <inheritdoc/>
        public override bool SupportArrayLoading => true;

        /// <inheritdoc/>
        public override object GetDefaultValue(Type type) {
            return VaultObjModel.Empty;
        }

        /// <inheritdoc/>
        public override object HandleLoad(MemberInfo member, VaultLoadenAttribute attribute) {
            if (Main.dedServ) {
                //服务器无需 GPU 资源
                return VaultObjModel.Empty;
            }

            if (attribute?.Mod == null || string.IsNullOrEmpty(attribute.Path)) {
                return VaultObjModel.Empty;
            }

            try {
                return LoadInternal(attribute.Mod, attribute.Path, ObjImportOptions.Default);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[ObjModelLoadenHandle:{attribute.Mod.Name}/{attribute.Path}]"
                    , $"Unhandled exception while loading OBJ model: {ex.Message}");
                return VaultObjModel.Empty;
            }
        }

        /// <summary>
        /// 直接从模组加载 OBJ 模型，便于其他模组在不使用标签的场景下复用
        /// </summary>
        /// <param name="mod">目标模组</param>
        /// <param name="path">OBJ 路径，可省略 <c>.obj</c> 扩展名</param>
        /// <param name="options">导入选项，<see langword="null"/> 时使用默认值</param>
        /// <returns>加载完成的模型，失败时返回 <see cref="VaultObjModel.Empty"/></returns>
        public static VaultObjModel Load(Mod mod, string path, ObjImportOptions options = null) {
            if (Main.dedServ || mod == null || string.IsNullOrEmpty(path)) {
                return VaultObjModel.Empty;
            }
            try {
                return LoadInternal(mod, path, options ?? ObjImportOptions.Default);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[ObjModelLoadenHandle:{mod.Name}/{path}]"
                    , $"Unhandled exception while loading OBJ model: {ex.Message}");
                return VaultObjModel.Empty;
            }
        }

        private static VaultObjModel LoadInternal(Mod mod, string path, ObjImportOptions options) {
            string normalized = NormalizeSlashes(path);

            if (!TryResolveObjPath(mod, normalized, out string objPath)) {
                VaultMod.LoggerError($"[ObjModelLoadenHandle:{mod.Name}/{path}]"
                    , $"OBJ file not found: '{mod.Name}/{normalized}' (also tried '{normalized}.obj')");
                return VaultObjModel.Empty;
            }

            string objDirectory = GetDirectory(objPath);
            ObjDiagnostic diagnostic = new ObjDiagnostic();

            ObjRawData rawData;
            using (Stream objStream = mod.GetFileStream(objPath, true)) {
                if (objStream == null) {
                    diagnostic.Error(objPath, 0, "Failed to open OBJ stream");
                    LogDiagnostic(mod, objPath, diagnostic);
                    return VaultObjModel.Empty;
                }
                using StreamReader reader = new StreamReader(objStream);
                rawData = ObjParser.Parse(reader, diagnostic, objPath, options);
            }

            //解析所有 MTL 库
            Dictionary<string, ObjMaterial> materials = new();
            foreach (string mtlRef in rawData.MaterialLibraries) {
                string mtlPath = ResolveRelativePath(objDirectory, mtlRef);
                if (!mod.FileExists(mtlPath)) {
                    diagnostic.Warn(objPath, 0, $"Referenced MTL not found: '{mtlPath}'");
                    continue;
                }
                using Stream mtlStream = mod.GetFileStream(mtlPath, true);
                if (mtlStream == null) {
                    diagnostic.Warn(objPath, 0, $"Failed to open MTL stream: '{mtlPath}'");
                    continue;
                }
                using StreamReader mtlReader = new StreamReader(mtlStream);
                Dictionary<string, ObjMaterial> parsed = MtlParser.Parse(mtlReader, diagnostic, mtlPath);
                foreach (KeyValuePair<string, ObjMaterial> kv in parsed) {
                    if (!materials.ContainsKey(kv.Key)) {
                        materials[kv.Key] = kv.Value;
                    }
                    else {
                        diagnostic.Warn(mtlPath, 0, $"Duplicate material name '{kv.Key}' across MTL libraries");
                        materials[kv.Key] = kv.Value;
                    }
                }

                //解析每个材质的贴图
                foreach (ObjMaterial material in parsed.Values) {
                    if (string.IsNullOrEmpty(material.DiffuseTexturePath)) {
                        continue;
                    }
                    string mtlDirectory = GetDirectory(mtlPath);
                    string textureRel = NormalizeSlashes(material.DiffuseTexturePath);
                    string texturePath = ResolveRelativePath(mtlDirectory, textureRel);
                    material.DiffuseTexturePath = texturePath;
                    material.DiffuseTexture = TryLoadTexture(mod, texturePath, mtlPath, diagnostic);
                }
            }

            List<ObjMeshGroup> groups = ObjMeshBuilder.Build(rawData, materials, options, diagnostic, objPath, out BoundingBox bounds);

            VaultObjModel model = new VaultObjModel(GetDisplayName(objPath), objPath, groups, materials, diagnostic) {
                Bounds = bounds,
            };
            int totalVertices = 0;
            int totalTriangles = 0;
            for (int i = 0; i < groups.Count; i++) {
                totalVertices += groups[i].Vertices.Length;
                totalTriangles += groups[i].TriangleCount;
            }
            model.VertexCount = totalVertices;
            model.TriangleCount = totalTriangles;

            LogDiagnostic(mod, objPath, diagnostic);
            return model;
        }

        private static Texture2D TryLoadTexture(Mod mod, string texturePath, string mtlPath, ObjDiagnostic diagnostic) {
            string assetPath = StripExtension(texturePath);

            try {
                if (!mod.HasAsset(assetPath)) {
                    diagnostic.Warn(mtlPath, 0, $"Diffuse texture not found as asset: '{assetPath}', using placeholder");
                    return VaultAsset.placeholder3?.Value;
                }
                Asset<Texture2D> asset = mod.Assets.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad);
                return asset?.Value ?? VaultAsset.placeholder3?.Value;
            } catch (Exception ex) {
                diagnostic.Warn(mtlPath, 0, $"Failed to request diffuse texture '{assetPath}': {ex.Message}");
                return VaultAsset.placeholder3?.Value;
            }
        }

        private static bool TryResolveObjPath(Mod mod, string path, out string resolved) {
            resolved = path;
            if (mod.FileExists(path)) {
                return true;
            }
            string withExt = path + ".obj";
            if (mod.FileExists(withExt)) {
                resolved = withExt;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 把可能含 <c>\\</c> 的路径规范成 <c>/</c> 分隔
        /// </summary>
        private static string NormalizeSlashes(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// 取一个文件路径的目录部分（含末尾斜杠），如果是顶层则返回空串
        /// </summary>
        private static string GetDirectory(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                return string.Empty;
            }
            int idx = filePath.LastIndexOf('/');
            if (idx < 0) {
                return string.Empty;
            }
            return filePath.Substring(0, idx + 1);
        }

        /// <summary>
        /// 解析相对路径，正确处理 <c>./</c> 和 <c>../</c> 段
        /// </summary>
        private static string ResolveRelativePath(string baseDir, string relativePath) {
            string normalizedRel = NormalizeSlashes(relativePath);
            if (normalizedRel.StartsWith("/")) {
                normalizedRel = normalizedRel.Substring(1);
            }

            string combined = baseDir + normalizedRel;
            string[] parts = combined.Split('/');
            List<string> stack = new();
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
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < stack.Count; i++) {
                if (i > 0) {
                    sb.Append('/');
                }
                sb.Append(stack[i]);
            }
            return sb.ToString();
        }

        private static string StripExtension(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            if (dot > slash && dot >= 0) {
                return path.Substring(0, dot);
            }
            return path;
        }

        private static string GetDisplayName(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            int slash = path.LastIndexOf('/');
            string fileName = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = fileName.LastIndexOf('.');
            if (dot >= 0) {
                fileName = fileName.Substring(0, dot);
            }
            return fileName;
        }

        private static void LogDiagnostic(Mod mod, string source, ObjDiagnostic diagnostic) {
            if (diagnostic == null || diagnostic.Entries.Count == 0) {
                return;
            }

            //错误级别的信息走错误日志（带节流），警告/信息走 Debug 日志
            if (diagnostic.HasErrors) {
                VaultMod.LoggerError($"[ObjModelLoadenHandle:{mod.Name}/{source}]"
                    , $"OBJ load reported {diagnostic.ErrorCount} error(s), {diagnostic.WarningCount} warning(s):\n{diagnostic.Format()}");
            }
            else if (diagnostic.WarningCount > 0) {
                VaultMod.Instance?.Logger.Debug($"[ObjModelLoadenHandle:{mod.Name}/{source}] "
                    + $"{diagnostic.WarningCount} warning(s):\n{diagnostic.Format()}");
            }
        }
    }
}
