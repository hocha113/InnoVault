using System;
using System.Collections.Generic;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 舞台的保存方式
    /// </summary>
    public enum StagePersistence
    {
        /// <summary>按根世界长期保存，再次进入时读回上次离开时的状态；<see cref="ScenarioStage.Version"/> 变化后重新生成</summary>
        Persistent,
        /// <summary>每次进入都重新生成，离开时删除舞台文件</summary>
        Transient,
    }

    /// <summary>
    /// 舞台组件：挂上它的场景拥有一整块独立的物块空间（舞台），只能经 <see cref="ScenarioDirector.Travel(VaultScenario, string, Action{VaultScenario})"/> 进入。
    /// 这里只做声明，生成与切换由平台后端执行<br/>
    /// 一个场景最多挂一个舞台组件。入口位置由 <see cref="ScenarioAnchors"/> 的 <see cref="ScenarioAnchors.Entry"/> 决定，
    /// 没有时落在舞台出生点（生成步骤写的 <c>Main.spawnTileX/Y</c>）
    /// </summary>
    public class ScenarioStage : ScenarioComponent
    {
        /// <summary>舞台宽度上限（物块），受 <c>Main.tile</c> 启动时的固定分配限制</summary>
        public const int MaxWidth = 8400;
        /// <summary>舞台高度上限（物块），受 <c>Main.tile</c> 启动时的固定分配限制</summary>
        public const int MaxHeight = 2400;

        /// <summary>声明一块指定尺寸的舞台</summary>
        /// <param name="width">宽度（物块），不超过 <see cref="MaxWidth"/></param>
        /// <param name="height">高度（物块），不超过 <see cref="MaxHeight"/></param>
        public ScenarioStage(int width, int height) {
            Width = width;
            Height = height;
        }

        /// <summary>宽度（物块）</summary>
        public int Width { get; }

        /// <summary>高度（物块）</summary>
        public int Height { get; }

        /// <summary>保存方式，默认长期保存</summary>
        public StagePersistence Persistence { get; init; } = StagePersistence.Persistent;

        /// <summary>生成版本号。长期保存的舞台在版本号与上次生成时不同时会重新生成，内容更新后递增它</summary>
        public int Version { get; init; }

        /// <summary>
        /// 生成前写入 <c>Main.worldSurface</c> 的行号，决定地表与地下背景、天光、天空区域、雨区与低重力范围；
        /// 为空时取高度的 30%。生成步骤里可以再改
        /// </summary>
        public int? SurfaceLevel { get; init; }

        /// <summary>生成前写入 <c>Main.rockLayer</c> 的行号；为空时取高度的 45%（且不高于地表层）。生成步骤里可以再改</summary>
        public int? RockLevel { get; init; }

        /// <summary>生成种子；为空时由根世界身份与场景 Key 确定性派生，同一个根世界里每次生成结果相同</summary>
        public int? Seed { get; init; }

        /// <summary>生成步骤。不重写 <see cref="BuildPasses"/> 时由它提供；两者都没有时生成一块空舞台</summary>
        public Action<List<GenPass>> Passes { get; init; }

        internal bool BindsRootWorld { get; private init; }

        /// <summary>收集生成步骤，默认调用 <see cref="Passes"/>。只在需要生成时由平台后端调用</summary>
        /// <param name="passes">按执行顺序填入的生成步骤</param>
        protected virtual void BuildPasses(List<GenPass> passes) => Passes?.Invoke(passes);

        internal void CollectPasses(List<GenPass> passes) => BuildPasses(passes);

        //根场景绑定菜单选中的世界文件，尺寸由文件决定
        internal static ScenarioStage ForRoot() => new(0, 0) { BindsRootWorld = true };

        internal string ValidateStage() {
            if (BindsRootWorld) {
                return null;
            }
            if (Width <= 0 || Height <= 0 || Width > MaxWidth || Height > MaxHeight) {
                return $"stage size {Width}x{Height} is outside 1..{MaxWidth} x 1..{MaxHeight}";
            }
            if (SurfaceLevel is int surface && (surface <= 0 || surface >= Height)) {
                return $"SurfaceLevel {surface} is outside 1..{Height - 1}";
            }
            if (RockLevel is int rock && (rock <= 0 || rock >= Height)) {
                return $"RockLevel {rock} is outside 1..{Height - 1}";
            }
            if (Scenario?.GetComponent<ScenarioAnchors>() is ScenarioAnchors anchors) {
                foreach (var pair in anchors.Points) {
                    if (pair.Value.X < 0 || pair.Value.Y < 0 || pair.Value.X >= Width || pair.Value.Y >= Height) {
                        return $"anchor '{pair.Key}' {pair.Value} is outside the stage";
                    }
                }
            }
            return null;
        }

        internal int ResolveSurfaceLevel() => SurfaceLevel ?? (int)(Height * 0.3);

        internal int ResolveRockLevel() => Math.Max(RockLevel ?? (int)(Height * 0.45), ResolveSurfaceLevel());
    }
}
