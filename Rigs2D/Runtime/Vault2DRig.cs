using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.IO;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 2D 骨架资产：一份已解析的 <see cref="Rig2DDefinition"/> + 各贴图件的贴图句柄
    /// <br/>资产是共享的静态数据；每个实体用 <see cref="CreateInstance"/> 拿自己的可变实例。
    /// 热重载时定义被原地替换、<see cref="Version"/> 自增，实例在下一次 <c>Step</c> 自动重绑
    /// <br/>加载入口：<c>[VaultLoaden("Assets/Rigs/SeaShrimp")]</c> 标记 <see cref="Vault2DRig"/> 静态字段（走 <c>Rig2DLoadenHandle</c>，
    /// 两端都会填值：服务器只有定义没有贴图），或代码里 <c>Load</c> / <c>FromDefinition</c>；
    /// 没有 tModLoader 的离线宿主用 <see cref="FromJson"/> + <see cref="UseDirectTextures"/>
    /// </summary>
    public sealed partial class Vault2DRig
    {
        /// <summary>
        /// 空资产占位：加载失败或服务器环境；<see cref="IsValid"/> 为假，实例的 <c>Step</c> / 绘制都是空操作
        /// </summary>
        public static Vault2DRig Empty { get; } = new Vault2DRig("(empty)", string.Empty);

        /// <summary>
        /// 资产名（取定义名，缺省取文件名）
        /// </summary>
        public string Name { get; private set; }
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
        /// 各带状件的贴图，下标同 <see cref="Rig2DDefinition.Ribbons"/>；服务器上为空数组
        /// </summary>
        public Asset<Texture2D>[] RibbonTextures { get; private set; } = [];
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

        /// <summary>
        /// 日志里的来源标注（游戏端为「模组名/路径」）
        /// </summary>
        internal string SourceHint { get; private set; }

        private Func<string, Texture2D> directResolver;
        private Texture2D[] directPieceTextures = [];
        private Texture2D[] directRibbonTextures = [];

        private Vault2DRig(string name, string sourcePath) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            SourceHint = SourcePath;
        }

        //==================== 离线宿主 ====================

        /// <summary>
        /// 直接从 JSON 文本建资产（不经模组文件系统；离线宿主与测试用）。解析失败返回带 <see cref="LastError"/> 的无效资产
        /// </summary>
        /// <param name="json">骨架 JSON 文本</param>
        /// <param name="sourceHint">日志来源标注（通常是文件路径）</param>
        /// <param name="textureResolver">贴图路径 → 贴图；给了就走直接贴图（见 <see cref="UseDirectTextures"/>）</param>
        public static Vault2DRig FromJson(string json, string sourceHint = null, Func<string, Texture2D> textureResolver = null) {
            Vault2DRig rig = new(FileStem(sourceHint ?? string.Empty), sourceHint ?? string.Empty) {
                directResolver = textureResolver,
            };
            if (!rig.ApplyText(json) && string.IsNullOrEmpty(rig.LastError)) {
                rig.LastError = "json parse failed";
            }
            return rig;
        }

        /// <summary>
        /// 改用直接贴图：每件贴图路径交给 <paramref name="resolver"/> 解析成 <see cref="Texture2D"/>，优先于资产句柄；
        /// 之后每次 <see cref="Replace"/>（热重载）自动重解析。传 <see langword="null"/> 取消
        /// </summary>
        public void UseDirectTextures(Func<string, Texture2D> resolver) {
            directResolver = resolver;
            if (Definition != null) {
                ResolveDirectTextures(Definition);
            }
            else {
                directPieceTextures = [];
                directRibbonTextures = [];
            }
        }

        /// <summary>
        /// 用 JSON 文本替换当前定义（等同热重载一次）；解析失败保留旧定义并返回 <see langword="false"/>
        /// </summary>
        public bool ReplaceText(string json) => ApplyText(json);

        /// <summary>
        /// 第 <paramref name="index"/> 件的设计贴图：直接贴图优先，否则取资产句柄的值；没有返回 <see langword="null"/>
        /// </summary>
        public Texture2D PieceTexture(int index) {
            if (index >= 0 && index < directPieceTextures.Length && directPieceTextures[index] != null) {
                return directPieceTextures[index];
            }
            Asset<Texture2D>[] textures = PieceTextures;
            if (textures == null || index < 0 || index >= textures.Length) {
                return null;
            }
            return textures[index]?.Value;
        }

        /// <summary>
        /// 第 <paramref name="index"/> 条带状件的设计贴图：直接贴图优先，否则取资产句柄的值
        /// </summary>
        public Texture2D RibbonTexture(int index) {
            if (index >= 0 && index < directRibbonTextures.Length && directRibbonTextures[index] != null) {
                return directRibbonTextures[index];
            }
            Asset<Texture2D>[] textures = RibbonTextures;
            if (textures == null || index < 0 || index >= textures.Length) {
                return null;
            }
            return textures[index]?.Value;
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
                Rig2DPlatform.LogError($"[Rig2D:{Name}]", $"definition invalid: {LastError}");
                return false;
            }
            Definition = def;
            if (!string.IsNullOrEmpty(def.Name)) {
                Name = def.Name;
            }
            ResolveHostTextures(def);
            ResolveDirectTextures(def);
            Version++;
            LastError = string.Empty;
            return true;
        }

        /// <summary>
        /// 用 JSON 文本替换定义（热重载用）
        /// </summary>
        internal bool ApplyText(string json) {
            Rig2DDefinition def = Rig2DJson.Parse(json, SourceHint);
            if (def == null) {
                LastError = "json parse failed";
                return false;
            }
            if (string.IsNullOrEmpty(def.Name)) {
                def.Name = Name;
            }
            return Replace(def);
        }

        /// <summary>
        /// 宿主取贴图句柄（游戏构建里按模组资产解析；离线构建没有实现）
        /// </summary>
        partial void ResolveHostTextures(Rig2DDefinition def);

        private void ResolveDirectTextures(Rig2DDefinition def) {
            if (directResolver == null) {
                directPieceTextures = [];
                directRibbonTextures = [];
                return;
            }
            directPieceTextures = new Texture2D[def.Pieces.Count];
            for (int i = 0; i < directPieceTextures.Length; i++) {
                directPieceTextures[i] = directResolver(def.Pieces[i].Texture);
            }
            directRibbonTextures = new Texture2D[def.Ribbons.Count];
            for (int i = 0; i < directRibbonTextures.Length; i++) {
                directRibbonTextures[i] = directResolver(def.Ribbons[i].Texture);
            }
        }

        private static string FileStem(string path) {
            string name = Path.GetFileName(path);
            int dot = name.IndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }
    }
}
