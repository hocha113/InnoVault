using InnoVault.Models3D.Gltf;
using InnoVault.Models3D.Wavefront;
using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 格式无关 3D 模型加载器，按文件后缀分发到 OBJ 或 glTF 导入器
    /// </summary>
    public sealed class Model3DLoadenHandle : VaultLoadenHandle
    {
        public override Type TargetType => typeof(Vault3DModel);
        public override int Priority => 10;
        public override bool SupportArrayLoading => true;

        public override bool CanHandle(Type type) {
            return type == typeof(Vault3DModel);
        }

        public override bool CanHandleArrayElement(Type elementType) {
            return SupportArrayLoading && CanHandle(elementType);
        }

        public override object GetDefaultValue(Type type) {
            return Vault3DModel.Empty;
        }

        public override object HandleLoad(MemberInfo member, VaultLoadenAttribute attribute) {
            if (Main.dedServ || attribute?.Mod == null || string.IsNullOrEmpty(attribute.Path)) {
                return Vault3DModel.Empty;
            }
            return Load(attribute.Mod, attribute.Path);
        }

        public static Vault3DModel Load(Mod mod, string path, GltfImportOptions gltfOptions = null, ObjImportOptions objOptions = null) {
            if (Main.dedServ || mod == null || string.IsNullOrEmpty(path)) {
                return Vault3DModel.Empty;
            }

            string normalized = NormalizeSlashes(path);
            string ext = GetExtension(normalized);
            try {
                if (ext == ".obj") {
                    return ObjModelLoadenHandle.Load(mod, normalized, objOptions ?? ObjImportOptions.Default);
                }
                if (ext == ".gltf") {
                    return GltfModelLoader.Load(mod, normalized, gltfOptions ?? GltfImportOptions.Default);
                }

                if (TryResolveObjPath(mod, normalized, out string objPath)) {
                    return ObjModelLoadenHandle.Load(mod, objPath, objOptions ?? ObjImportOptions.Default);
                }
                if (TryResolveGltfPath(mod, normalized, out string gltfPath)) {
                    return GltfModelLoader.Load(mod, gltfPath, gltfOptions ?? GltfImportOptions.Default);
                }

                VaultMod.LoggerError($"[Model3DLoadenHandle:{mod.Name}/{path}]"
                    , $"3D model file not found: '{normalized}' (tried .obj and .gltf)");
                return Vault3DModel.Empty;
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Model3DLoadenHandle:{mod.Name}/{path}]"
                    , $"Unhandled exception while loading 3D model: {ex.Message}");
                return Vault3DModel.Empty;
            }
        }

        private static bool TryResolveObjPath(Mod mod, string path, out string resolved) {
            resolved = path;
            if (mod.FileExists(path)) {
                return GetExtension(path) == ".obj";
            }
            string withExt = path + ".obj";
            if (mod.FileExists(withExt)) {
                resolved = withExt;
                return true;
            }
            return false;
        }

        private static bool TryResolveGltfPath(Mod mod, string path, out string resolved) {
            resolved = path;
            if (mod.FileExists(path)) {
                return GetExtension(path) == ".gltf";
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

        private static string GetExtension(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            int slash = path.LastIndexOf('/');
            int dot = path.LastIndexOf('.');
            if (dot <= slash) {
                return string.Empty;
            }
            return path.Substring(dot).ToLowerInvariant();
        }
    }
}
#pragma warning restore CS1591
