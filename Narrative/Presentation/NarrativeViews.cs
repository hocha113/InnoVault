using System;
using System.Collections.Generic;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 叙事视图注册表。视图在加载时把自己注册进来，<see cref="NarrativeRunner"/> 通过本表广播会话状态，<br/>
    /// 从而与具体视图类型解耦——既支持框架默认视图，也支持消费者自定义视图
    /// </summary>
    public static class NarrativeViews
    {
        private static readonly List<INarrativeView> _views = [];

        /// <summary>注册一个视图</summary>
        public static void Register(INarrativeView view) {
            if (view != null && !_views.Contains(view)) {
                _views.Add(view);
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
        internal static void Clear() => _views.Clear();
    }
}
