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
    /// 多人模式下按玩家分别判定（<see cref="IsEngaged(Player)"/>）：刷怪、重力、区域判定这类逐玩家的规则只作用于区域里的玩家；
    /// 时间、天气、事件、物块更新、城镇 NPC 入住是整块舞台共享的状态，服务器只听不带区域的场景，区域场景设置的时间只改区域里玩家自己客户端上的表现<br/>
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

        //按玩家索引记录谁在区域里。每一端都按自己看到的玩家位置判定，不经网络同步
        private readonly bool[] _engaged = new bool[256];

        /// <summary>本地玩家此刻是否在区域里（按回差判定）；专用服务器上没有本地玩家，恒为假</summary>
        public bool Engaged => IsEngaged(ScenarioPlatform.LocalPlayerIndex);

        /// <summary>是否有任何玩家在区域里</summary>
        public bool AnyEngaged => Array.IndexOf(_engaged, true) >= 0;

        /// <summary>某个玩家此刻是否在区域里（按回差判定）</summary>
        /// <param name="player">玩家索引（<c>Main.player</c> 的下标）</param>
        public bool IsEngaged(int player) => (uint)player < (uint)_engaged.Length && _engaged[player];

        /// <summary>某个玩家此刻是否在区域里（按回差判定）</summary>
        public bool IsEngaged(Player player) => player != null && IsEngaged(player.whoAmI);

        internal void SetEngaged(int player, bool value) {
            if ((uint)player < (uint)_engaged.Length) {
                _engaged[player] = value;
            }
        }

        internal void ClearEngaged() => Array.Clear(_engaged);

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

        /// <summary>
        /// 一个玩家进入区域时调用，主线程。多人模式下每一端都会为每个玩家调用（按这一端看到的位置判定）：
        /// 玩法逻辑先看 <see cref="ScenarioComponent.IsAuthority"/>，只给本地玩家看的表现先比较 <c>player.whoAmI == Main.myPlayer</c>
        /// </summary>
        protected virtual void OnPlayerEnter(Player player) { }

        /// <summary>一个玩家离开区域（或离开游戏）时调用，主线程。多人模式下的调用范围与 <see cref="OnPlayerEnter"/> 相同</summary>
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
