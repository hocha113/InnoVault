using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Rigs2D.Runtime
{
    //游戏宿主：按模组文件系统加载、按模组资产取贴图、登记热重载
    public sealed partial class Vault2DRig
    {
        /// <summary>
        /// 来源模组（代码直建时为构造方传入的模组，可空）
        /// </summary>
        public Mod Mod { get; }

        private Vault2DRig(string name, Mod mod, string sourcePath) : this(name, sourcePath) {
            Mod = mod;
            SourceHint = $"{mod?.Name}/{SourcePath}";
        }

        /// <summary>
        /// 从模组文件加载骨架定义
        /// <br/>路径可省略扩展名，按 <c>.rig.json</c>、<c>.json</c> 顺序探测；文件缺失或解析失败返回带错误信息的无效资产（已记日志）
        /// <br/>开发机上若 ModSources 里存在同名源文件，会登记进热重载监视
        /// </summary>
        public static Vault2DRig Load(Mod mod, string path) {
            if (mod == null || string.IsNullOrEmpty(path)) {
                return Empty;
            }
            string normalized = path.Replace('\\', '/');
            if (!TryResolvePath(mod, normalized, out string resolved)) {
                VaultMod.LoggerError($"[Rig2D:{mod.Name}/{path}]", $"rig file not found: '{normalized}' (tried .rig.json / .json)");
                Vault2DRig missing = new(FileStem(normalized), mod, normalized);
                missing.LastError = "file not found";
                Rig2DSystem.Register(missing);
                return missing;
            }

            Vault2DRig rig = new(FileStem(resolved), mod, resolved);
            string text = ReadModText(mod, resolved, out string readError);
            if (text == null) {
                rig.LastError = readError;
                VaultMod.LoggerError($"[Rig2D:{mod.Name}/{resolved}]", readError);
            }
            else {
                rig.ApplyText(text);
            }
            Rig2DSystem.Register(rig);
            return rig;
        }

        /// <summary>
        /// 用代码构建的定义包装成资产（定义须已 <see cref="Rig2DDefinition.Resolve"/>；未解析会在此尝试解析）
        /// </summary>
        public static Vault2DRig FromDefinition(Mod mod, Rig2DDefinition def) {
            if (def == null) {
                return Empty;
            }
            Vault2DRig rig = new(def.Name, mod, string.Empty);
            rig.Replace(def);
            return rig;
        }

        partial void ResolveHostTextures(Rig2DDefinition def) {
            PieceTextures = ResolveTextures(Mod, def);
            RibbonTextures = ResolveRibbonTextures(Mod, def);
        }

        private static bool TryResolvePath(Mod mod, string path, out string resolved) {
            resolved = path;
            if (mod.FileExists(path)) {
                return true;
            }
            string a = path + ".rig.json";
            if (mod.FileExists(a)) {
                resolved = a;
                return true;
            }
            string b = path + ".json";
            if (mod.FileExists(b)) {
                resolved = b;
                return true;
            }
            return false;
        }

        private static string ReadModText(Mod mod, string path, out string error) {
            error = string.Empty;
            try {
                byte[] bytes = mod.GetFileBytes(path);
                if (bytes == null) {
                    error = "GetFileBytes returned null";
                    return null;
                }
                //剥掉可能存在的 UTF-8 BOM
                int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
            } catch (Exception ex) {
                error = $"read failed: {ex.Message}";
                return null;
            }
        }

        private static Asset<Texture2D>[] ResolveTextures(Mod mod, Rig2DDefinition def) {
            if (Main.dedServ) {
                return [];
            }
            Asset<Texture2D>[] result = new Asset<Texture2D>[def.Pieces.Count];
            for (int i = 0; i < result.Length; i++) {
                result[i] = ResolveTexture(mod, def.Pieces[i].Texture, def.Name);
            }
            return result;
        }

        private static Asset<Texture2D>[] ResolveRibbonTextures(Mod mod, Rig2DDefinition def) {
            if (Main.dedServ) {
                return [];
            }
            Asset<Texture2D>[] result = new Asset<Texture2D>[def.Ribbons.Count];
            for (int i = 0; i < result.Length; i++) {
                result[i] = ResolveTexture(mod, def.Ribbons[i].Texture, def.Name);
            }
            return result;
        }

        /// <summary>
        /// 解析一条贴图路径：<c>@其他模组/路径</c> 跨模组，否则相对 <paramref name="mod"/>；缺失返回占位并记日志
        /// </summary>
        public static Asset<Texture2D> ResolveTexture(Mod mod, string texturePath, string rigName = null) {
            if (Main.dedServ) {
                return null;
            }
            if (string.IsNullOrEmpty(texturePath)) {
                return VaultAsset.placeholder3;
            }
            string path = texturePath.Replace('\\', '/');
            Mod target = mod;
            if (path.StartsWith('@')) {
                int slash = path.IndexOf('/');
                string modName = slash > 0 ? path[1..slash] : path[1..];
                path = slash > 0 ? path[(slash + 1)..] : string.Empty;
                if (!ModLoader.TryGetMod(modName, out target)) {
                    VaultMod.LoggerError($"[Rig2D:{rigName}]", $"texture mod '{modName}' not loaded for '{texturePath}'");
                    return VaultAsset.placeholder3;
                }
            }
            else if (target != null) {
                //允许写成带模组名前缀的完整路径
                string prefix = target.Name + "/";
                if (path.StartsWith(prefix, StringComparison.Ordinal)) {
                    path = path[prefix.Length..];
                }
            }
            if (target == null || !target.HasAsset(path)) {
                VaultMod.LoggerError($"[Rig2D:{rigName}]", $"texture not found: '{texturePath}'");
                return VaultAsset.placeholder3;
            }
            return target.Assets.Request<Texture2D>(path, AssetRequestMode.AsyncLoad);
        }
    }
}
