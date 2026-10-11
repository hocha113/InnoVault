using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 玩家离开区域时怎么处理
    /// </summary>
    public enum ScenarioAreaExit
    {
        /// <summary>场景的规则、镜头、环境失效，场景保持激活，回来后再次生效</summary>
        Disengage,
        /// <summary>离开即停用场景，适合一次性遭遇</summary>
        Deactivate,
        /// <summary>不让离开，按 <see cref="ScenarioArea.ContainMode"/> 处理</summary>
        Contain,
    }

    /// <summary>
    /// <see cref="ScenarioAreaExit.Contain"/> 时怎样把玩家留在区域里
    /// </summary>
    public enum ScenarioContainMode
    {
        /// <summary>退回到上一帧还在区域里的位置，并清掉速度，像撞上一堵墙</summary>
        Block,
        /// <summary>传回 <see cref="ScenarioArea.ContainAnchor"/> 指定的锚点；锚点不存在时按 <see cref="Block"/> 处理</summary>
        ReturnToAnchor,
    }

    /// <summary>
    /// 区域组件：场景只作用于当前舞台里的一块范围（物块坐标），而不是整块舞台<br/>
    /// <b>跟随玩家</b>的规则（时间、天气、刷怪、重力、镜头、环境、地图策略等）只在本地玩家进入区域后参与合成；
    /// <b>按位置</b>的规则（物块保护、区域还原）只看对象自己的位置，与玩家在不在里面无关<br/>
    /// 进入判定看玩家中心所在的格子；离开时要走出 <see cref="ExitMargin"/> 格才算离开，在边缘来回走不会反复切换<br/>
    /// 一个场景最多挂一个区域组件，挂了它的场景不能放在会话层
    /// </summary>
    public class ScenarioArea : ScenarioComponent
    {
        private readonly Rectangle[] _rectangles;

        /// <summary>由一个或多个矩形组成的区域（物块坐标）</summary>
        /// <param name="rectangles">至少一个宽高为正的矩形</param>
        public ScenarioArea(params Rectangle[] rectangles) {
            _rectangles = rectangles == null ? [] : (Rectangle[])rectangles.Clone();
            Rectangle bounds = Rectangle.Empty;
            for (int i = 0; i < _rectangles.Length; i++) {
                bounds = i == 0 ? _rectangles[i] : Rectangle.Union(bounds, _rectangles[i]);
            }
            Bounds = bounds;
        }

        /// <summary>组成区域的矩形（物块坐标）</summary>
        public IReadOnlyList<Rectangle> Rectangles => _rectangles;

        /// <summary>所有矩形的外接框（物块坐标）</summary>
        public Rectangle Bounds { get; }

        /// <summary>离开判定的回差（格）：玩家要走出区域这么远才算离开，缺省 2</summary>
        public int ExitMargin { get; init; } = 2;

        /// <summary>玩家离开时怎么处理，缺省 <see cref="ScenarioAreaExit.Disengage"/></summary>
        public ScenarioAreaExit ExitBehavior { get; init; }

        /// <summary><see cref="ScenarioAreaExit.Contain"/> 时的处理方式</summary>
        public ScenarioContainMode ContainMode { get; init; }

        /// <summary><see cref="ScenarioContainMode.ReturnToAnchor"/> 用的锚点名，为空时取 <see cref="ScenarioAnchors.Entry"/></summary>
        public string ContainAnchor { get; init; }

        /// <summary>本地玩家此刻是否在区域里（按回差判定）</summary>
        public bool Engaged { get; internal set; }

        internal override bool BindsToStage => true;

        /// <summary>某一格是否在区域里。重写它可以做任意形状，矩形只决定 <see cref="Bounds"/></summary>
        public virtual bool Contains(int i, int j) {
            for (int k = 0; k < _rectangles.Length; k++) {
                if (_rectangles[k].Contains(i, j)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>某一格是否在区域里</summary>
        public bool Contains(Point tile) => Contains(tile.X, tile.Y);

        //回差判定：中心或周围 margin 格处的八个点有一个在区域里就仍算在里面
        internal bool IsNear(int i, int j, int margin) {
            if (Contains(i, j)) {
                return true;
            }
            if (margin <= 0) {
                return false;
            }
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    if ((dx != 0 || dy != 0) && Contains(i + dx * margin, j + dy * margin)) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>本地玩家进入区域时调用，主线程</summary>
        protected virtual void OnPlayerEnter(Player player) { }

        /// <summary>本地玩家离开区域时调用，主线程</summary>
        protected virtual void OnPlayerExit(Player player) { }

        internal void InvokePlayerEnter(Player player) => OnPlayerEnter(player);
        internal void InvokePlayerExit(Player player) => OnPlayerExit(player);

        /// <inheritdoc/>
        protected override string Validate() {
            if (_rectangles.Length == 0) {
                return "ScenarioArea needs at least one rectangle";
            }
            foreach (Rectangle rectangle in _rectangles) {
                if (rectangle.Width <= 0 || rectangle.Height <= 0) {
                    return $"ScenarioArea rectangle {rectangle} has no size";
                }
            }
            return ExitMargin < 0 ? "ScenarioArea.ExitMargin is negative" : null;
        }
    }
}
