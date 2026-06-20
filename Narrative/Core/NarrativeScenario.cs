using System;
using System.Collections.Generic;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 叙事场景基类，是内容作者定义剧情的载体。继承它并实现 <see cref="Build"/> 即可，<br/>
    /// 由 tModLoader 自动加载、由本框架自动注册。场景只描述"播什么"，<br/>
    /// "怎么播 / 怎么记录 / 何时发奖励"由框架与宿主服务负责。<br/>
    /// 每次启动都会重新 <see cref="Build"/>，因此构建期条件（<see cref="NarrativeComposer.When"/>）能反映当前世界状态
    /// </summary>
    public abstract class NarrativeScenario : VaultType<NarrativeScenario>
    {
        private static readonly Dictionary<string, NarrativeScenario> _byKey = new(StringComparer.Ordinal);

        /// <summary>场景唯一 Key，默认取类名</summary>
        public virtual string Key => Name;

        /// <summary>默认样式 id，对话框 / 选择框 / 弹窗据此取皮肤</summary>
        public virtual StyleId DefaultStyle => StyleId.Default;

        /// <summary>声明式触发策略，由 <see cref="ConfigurePolicy"/> 生成；<see langword="null"/> 表示不参与自动调度</summary>
        public NarrativePolicy Policy { get; private set; }

        /// <summary>构建剧情内容</summary>
        protected abstract void Build(NarrativeComposer composer);

        /// <summary>配置触发策略，返回 <see langword="null"/> 表示仅手动启动</summary>
        protected virtual NarrativePolicy ConfigurePolicy() => null;

        /// <summary>场景开始播放时回调</summary>
        protected virtual void OnStarted() { }

        /// <summary>场景完整播放完成时回调（在框架写入完成标记之后）</summary>
        protected virtual void OnCompleted() { }

        /// <inheritdoc/>
        protected override void VaultRegister() => _byKey[Key] = this;

        /// <inheritdoc/>
        public override void VaultSetup()
        {
            SetStaticDefaults();
            Policy = ConfigurePolicy();
        }

        internal NarrativeGraph BuildGraph()
        {
            NarrativeGraph graph = new();
            Build(new NarrativeComposer(graph));
            return graph;
        }

        internal void InvokeStarted() => OnStarted();
        internal void InvokeCompleted() => OnCompleted();

        /// <summary>启动本场景（忙时会自动入队）</summary>
        public bool Begin() => NarrativeRunner.Begin(this);

        /// <summary>按 Key 查找已注册场景</summary>
        public static NarrativeScenario Find(string key)
            => key != null && _byKey.TryGetValue(key, out var scenario) ? scenario : null;

        /// <summary>所有已注册场景</summary>
        public static IReadOnlyCollection<NarrativeScenario> All => _byKey.Values;

        /// <summary>清空场景注册表（卸载时调用）</summary>
        internal static void ClearRegistry() => _byKey.Clear();
    }
}
