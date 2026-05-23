using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// OBJ 静态模型导入器
    /// <br/>实际加载工作流：读取 OBJ -> 解析 -> 读取 MTL -> 加载贴图 -> 构造 GPU 顶点
    /// </summary>
    public static class ObjModelLoader
    {
        /// <summary>
        /// 直接从模组加载 OBJ 模型
        /// </summary>
        /// <param name="mod">目标模组</param>
        /// <param name="path">OBJ 路径，可省略 <c>.obj</c> 扩展名</param>
        /// <param name="options">导入选项，<see langword="null"/> 时使用默认值</param>
        /// <returns>加载完成的模型，失败时返回 <see cref="Vault3DModel.Empty"/></returns>
        public static Vault3DModel Load(Mod mod, string path, ObjImportOptions options = null) {
            if (Main.dedServ || mod == null || string.IsNullOrEmpty(path)) {
                return Vault3DModel.Empty;
            }
            try {
                return LoadInternal(mod, path, options ?? ObjImportOptions.Default);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[ObjModelLoader:{mod.Name}/{path}]"
                    , $"Unhandled exception while loading OBJ model: {ex.Message}");
                return Vault3DModel.Empty;
            }
        }

        private static Vault3DModel LoadInternal(Mod mod, string path, ObjImportOptions options) {
            string normalized = NormalizeSlashes(path);

            if (!TryResolveObjPath(mod, normalized, out string objPath)) {
                VaultMod.LoggerError($"[ObjModelLoader:{mod.Name}/{path}]"
                    , $"OBJ file not found: '{mod.Name}/{normalized}' (also tried '{normalized}.obj')");
                return Vault3DModel.Empty;
            }

            string objDirectory = GetDirectory(objPath);
            ObjDiagnostic diagnostic = new ObjDiagnostic();

            ObjRawData rawData;
            using (Stream objStream = mod.GetFileStream(objPath, true)) {
                if (objStream == null) {
                    diagnostic.Error(objPath, 0, "Failed to open OBJ stream");
                    LogDiagnostic(mod, objPath, diagnostic);
                    return Vault3DModel.Empty;
                }
                using StreamReader reader = new StreamReader(objStream);
                rawData = ObjParser.Parse(reader, diagnostic, objPath, options);
            }

            Dictionary<string, ObjMaterial> objMaterials = new();
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
                    if (!objMaterials.ContainsKey(kv.Key)) {
                        objMaterials[kv.Key] = kv.Value;
                    }
                    else {
                        diagnostic.Warn(mtlPath, 0, $"Duplicate material name '{kv.Key}' across MTL libraries");
                        objMaterials[kv.Key] = kv.Value;
                    }
                }

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

            List<ObjMeshGroup> objGroups = ObjMeshBuilder.Build(rawData, objMaterials, options, diagnostic, objPath, out BoundingBox bounds);
            List<Model3DMeshGroup> groups = ToModelGroups(objGroups);
            Dictionary<string, Model3DMaterial> materials = ToModelMaterials(objMaterials);
            Vault3DModel model = new Vault3DModel(GetDisplayName(objPath), objPath, groups, materials, diagnostic) {
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

        private static List<Model3DMeshGroup> ToModelGroups(List<ObjMeshGroup> objGroups) {
            List<Model3DMeshGroup> groups = new List<Model3DMeshGroup>(objGroups?.Count ?? 0);
            if (objGroups == null) {
                return groups;
            }
            for (int i = 0; i < objGroups.Count; i++) {
                groups.Add(objGroups[i]);
            }
            return groups;
        }

        private static Dictionary<string, Model3DMaterial> ToModelMaterials(Dictionary<string, ObjMaterial> objMaterials) {
            Dictionary<string, Model3DMaterial> materials = new Dictionary<string, Model3DMaterial>(objMaterials?.Count ?? 0);
            if (objMaterials == null) {
                return materials;
            }
            foreach (KeyValuePair<string, ObjMaterial> kv in objMaterials) {
                materials[kv.Key] = kv.Value;
            }
            return materials;
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
            return dot > slash && dot >= 0 ? path.Substring(0, dot) : path;
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

        private static void LogDiagnostic(Mod mod, string source, ObjDiagnostic diagnostic) {
            if (diagnostic == null || diagnostic.Entries.Count == 0) {
                return;
            }

            if (diagnostic.HasErrors) {
                VaultMod.LoggerError($"[ObjModelLoader:{mod.Name}/{source}]"
                    , $"OBJ load reported {diagnostic.ErrorCount} error(s), {diagnostic.WarningCount} warning(s):\n{diagnostic.Format()}");
            }
            else if (diagnostic.WarningCount > 0) {
                VaultMod.Instance?.Logger.Debug($"[ObjModelLoader:{mod.Name}/{source}] "
                    + $"{diagnostic.WarningCount} warning(s):\n{diagnostic.Format()}");
            }
        }
    }
}
