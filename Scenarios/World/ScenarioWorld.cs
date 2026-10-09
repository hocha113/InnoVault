using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景世界的保存方式
    /// </summary>
    public enum ScenarioWorldPersistence
    {
        /// <summary>按根世界长期保存，再次进入时读回上次离开时的状态；<see cref="ScenarioWorld.Version"/> 变化后重新生成</summary>
        Persistent,
        /// <summary>每次进入都重新生成，离开时删除世界文件</summary>
        Transient,
    }

    /// <summary>
    /// 世界组件：挂上它的场景拥有一个独立世界，只能经世界切换进入。这里只做声明，生成与切换由平台后端执行<br/>
    /// 一个场景最多挂一个世界组件
    /// </summary>
    public class ScenarioWorld : ScenarioComponent
    {
        /// <summary>世界宽度上限（物块），受 <c>Main.tile</c> 启动时的固定分配限制</summary>
        public const int MaxWidth = 8400;
        /// <summary>世界高度上限（物块），受 <c>Main.tile</c> 启动时的固定分配限制</summary>
        public const int MaxHeight = 2400;

        /// <summary>声明一个指定尺寸的场景世界</summary>
        /// <param name="width">宽度（物块），不超过 <see cref="MaxWidth"/></param>
        /// <param name="height">高度（物块），不超过 <see cref="MaxHeight"/></param>
        public ScenarioWorld(int width, int height) {
            Width = width;
            Height = height;
        }

        /// <summary>宽度（物块）</summary>
        public int Width { get; }

        /// <summary>高度（物块）</summary>
        public int Height { get; }

        /// <summary>保存方式，默认长期保存</summary>
        public ScenarioWorldPersistence Persistence { get; init; } = ScenarioWorldPersistence.Persistent;

        /// <summary>生成版本号。长期保存的世界在版本号与上次生成时不同时会重新生成，内容更新后递增它</summary>
        public int Version { get; init; }

        /// <summary>进入时玩家所在的物块坐标；为空时使用世界出生点</summary>
        public Point? EntryTile { get; init; }

        /// <summary>生成步骤。不重写 <see cref="BuildPasses"/> 时由它提供；两者都没有时生成一个空世界</summary>
        public Action<List<GenPass>> Passes { get; init; }

        internal bool BindsRootWorld { get; private init; }

        /// <summary>收集生成步骤，默认调用 <see cref="Passes"/>。只在需要生成时由平台后端调用</summary>
        /// <param name="passes">按执行顺序填入的生成步骤</param>
        protected virtual void BuildPasses(List<GenPass> passes) => Passes?.Invoke(passes);

        internal void CollectPasses(List<GenPass> passes) => BuildPasses(passes);

        //根场景绑定菜单选中的世界文件，尺寸由文件决定
        internal static ScenarioWorld ForRoot() => new(0, 0) { BindsRootWorld = true };

        internal string Validate() {
            if (BindsRootWorld) {
                return null;
            }
            if (Width <= 0 || Height <= 0 || Width > MaxWidth || Height > MaxHeight) {
                return $"world size {Width}x{Height} is outside 1..{MaxWidth} x 1..{MaxHeight}";
            }
            return null;
        }
    }
}
