using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 2D 骨架资产：一份已解析的 <see cref="Rig2DDefinition"/> + 各贴图件的贴图句柄
    /// <br/>资产是共享的静态数据；每个实体用 <see cref="CreateInstance"/> 拿自己的可变实例。
    /// 热重载时定义被原地替换、<see cref="Version"/> 自增，实例在下一次 <c>Step</c> 自动重绑
    /// <br/>加载入口：<c>[VaultLoaden("Assets/Rigs/SeaShrimp")]</c> 标记 <see cref="Vault2DRig"/> 静态字段（走 <see cref="Rig2DLoadenHandle"/>，
    /// 两端都会填值：服务器只有定义没有贴图），或代码里 <see cref="Load"/> / <see cref="FromDefinition"/>
    /// </summary>
    public sealed class Vault2DRig
    {
        /// <summary>
        /// 空资产占位：加载失败或服务器环境；<see cref="IsValid"/> 为假，实例的 <c>Step</c> / 绘制都是空操作
        /// </summary>
        public static Vault2DRig Empty { get; } = new Vault2DRig("(empty)", null, string.Empty);

        /// <summary>
        /// 资产名（取定义名，缺省取文件名）
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// 来源模组（代码直建时为构造方传入的模组，可空）
        /// </summary>
        public Mod Mod { get; }
        /// <summary>
        /// 模组内相对路径（含扩展名）；代码直建为空
        /// </summary>
        public string SourcePath { get; }
        /// <summary>
        /// 当前定义；无效资产为 <see langword="null"/>
        /// </summary>
        public Rig2DDefinition Definition { get; private set; }
        /// <summary>
        /// 各贴图件的贴图，下标同 <see cref="Rig2DDefinition.Pieces"/>；服务器上为空数组
        /// </summary>
        public Asset<Texture2D>[] PieceTextures { get; private set; } = [];
        /// <summary>
        /// 定义版本号，热重载每次自增
        /// </summary>
        public int Version { get; private set; }
        /// <summary>
        /// 最近一次加载 / 重载的错误文本（成功为空）
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// 是否可用（定义已解析且至少有一根骨骼）
        /// </summary>
        public bool IsValid => Definition != null && Definition.Resolved && Definition.BoneCount > 0;

        private Vault2DRig(string name, Mod mod, string sourcePath) {
            Name = name ?? string.Empty;
            Mod = mod;
            SourcePath = sourcePath ?? string.Empty;
        }

        //==================== 加载 ====================

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

        /// <summary>
        /// 创建一个运行时实例
        /// </summary>
        /// <param name="seed">确定性种子（各端一致，例如 <c>npc.whoAmI</c> 派生量）</param>
        public Rig2DInstance CreateInstance(float seed = 0f) => new(this, seed);

        //==================== 替换 / 热重载 ====================

        /// <summary>
        /// 用新定义替换（解析、重取贴图、版本自增）；解析失败保留旧定义并返回 <see langword="false"/>
        /// </summary>
        public bool Replace(Rig2DDefinition def) {
            if (def == null) {
                LastError = "null definition";
                return false;
            }
            if (!def.Resolved && !def.Resolve(out string error)) {
                LastError = error ?? "resolve failed";
                VaultMod.LoggerError($"[Rig2D:{Name}]", $"definition invalid: {LastError}");
                return false;
            }
            Definition = def;
            if (!string.IsNullOrEmpty(def.Name)) {
                Name = def.Name;
            }
            PieceTextures = ResolveTextures(Mod, def);
            Version++;
            LastError = string.Empty;
            return true;
        }

        /// <summary>
        /// 用 JSON 文本替换定义（热重载用）
        /// </summary>
        internal bool ApplyText(string json) {
            Rig2DDefinition def = Rig2DJson.Parse(json, $"{Mod?.Name}/{SourcePath}");
            if (def == null) {
                LastError = "json parse failed";
                return false;
            }
            if (string.IsNullOrEmpty(def.Name)) {
                def.Name = Name;
            }
            return Replace(def);
        }

        //==================== 内部 ====================

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

        private static string FileStem(string path) {
            string name = Path.GetFileName(path);
            int dot = name.IndexOf('.');
            return dot > 0 ? name[..dot] : name;
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
