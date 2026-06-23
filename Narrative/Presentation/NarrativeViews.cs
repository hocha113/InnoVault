using InnoVault.Narrative.Runtime;
using System;
using System.Collections.Generic;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 叙事视图注册表。视图在加载时把自己注册进来，<see cref="NarrativeRunner"/> 通过本表广播会话状态，<br/>
    /// 从而与具体视图类型解耦——既支持框架默认视图，也支持消费者自定义视图
    /// </summary>
    public static class NarrativeViews
    {
        private static readonly List<INarrativeView> _views = [];

        /// <summary>
        /// 是否允许框架内置默认视图自注册。复杂 consumer 可以在加载早期关闭对应默认视图，
        /// 再注册自己的 <see cref="INarrativeView"/> 实现，避免 UI 叠加显示
        /// </summary>
        public static bool UseDefaultDialogueView { get; set; } = true;
        /// <summary>是否允许框架内置默认选择框视图自注册</summary>
        public static bool UseDefaultChoiceView { get; set; } = true;
        /// <summary>是否允许框架内置默认弹窗视图自注册</summary>
        public static bool UseDefaultPopupView { get; set; } = true;

        /// <summary>
        /// 是否允许框架内置默认 backlog 视图注册为生效视图。复杂 consumer 可关闭它，
        /// 再注册自己的 <see cref="INarrativeBacklogView"/> 实现
        /// </summary>
        public static bool UseDefaultBacklogView { get; set; } = true;

        /// <summary>注册一个视图</summary>
        public static void Register(INarrativeView view) {
            if (view != null && !_views.Contains(view)) {
                _views.Add(view);
            }
        }

        /// <summary>注销一个视图，用于替换默认视图或关闭临时 consumer 视图</summary>
        public static void Unregister(INarrativeView view) {
            if (view != null) {
                _views.Remove(view);
            }
        }

        /// <summary>把当前会话同步给所有视图，单个视图异常不影响其它视图</summary>
        public static void Sync(NarrativeSession active) {
            for (int i = 0; i < _views.Count; i++) {
                try {
                    _views[i].Sync(active);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"Narrative view {_views[i]} Sync threw: {ex}");
                }
            }
        }

        /// <summary>清空注册（卸载时调用）</summary>
        internal static void Clear() {
            _views.Clear();
            UseDefaultDialogueView = true;
            UseDefaultChoiceView = true;
            UseDefaultPopupView = true;
            UseDefaultBacklogView = true;
        }
    }
}
