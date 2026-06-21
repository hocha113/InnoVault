using InnoVault.Narrative.Composition;
using InnoVault.Narrative.Runtime;
using System;
using System.Collections.Generic;

namespace InnoVault.Narrative.Core
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

        /// <summary>
        /// 场景唯一 Key，默认使用 <c>ModName/TypeName</c>。<br/>
        /// Narrative 是跨模组公共框架，场景 Key 会进入全局注册表、pending 队列和进度存储，不能只用类名
        /// </summary>
        public virtual string Key {
            get {
                Type type = GetType();
                return TypeToMod.TryGetValue(type, out var mod)
                    ? GetFullName(mod.Name, type.Name)
                    : type.FullName;
            }
        }

        /// <summary>默认样式 id，对话框 / 选择框 / 弹窗据此取皮肤。可写短名，运行时按所属 Mod 补全</summary>
        public virtual StyleId DefaultStyle => StyleId.Default;

        /// <summary>按所属 Mod 补全后的默认样式 id</summary>
        internal StyleId ResolvedDefaultStyle => NarrativeIdScope.Style(Mod?.Name, DefaultStyle);

        /// <summary>构造本模组作用域内的角色 id（与在 <see cref="Build"/> 中写短名等价）</summary>
        protected CharacterId Speaker(string name) => NarrativeIdScope.Character(Mod?.Name, name);

        /// <summary>构造本模组作用域内的样式 id</summary>
        protected StyleId Style(string name) => NarrativeIdScope.Style(Mod?.Name, name);

        /// <summary>
        /// 声明式触发策略，由 <see cref="ConfigurePolicy"/> 在加载期生成一次；<see langword="null"/> 表示不参与自动调度。<br/>
        /// 策略对象可以长期存在，但其中的谓词必须在被调用时读取实时游戏状态，不能在 <see cref="ConfigurePolicy"/> 内提前采样一次
        /// </summary>
        public NarrativePolicy Policy { get; private set; }

        /// <summary>构建剧情内容</summary>
        protected abstract void Build(NarrativeComposer composer);

        /// <summary>
        /// 配置触发策略，返回 <see langword="null"/> 表示仅手动启动。<br/>
        /// 该方法只在加载 / setup 阶段执行一次；需要随世界、玩家、NPC 等变化的条件应写入返回的委托中实时判断
        /// </summary>
        protected virtual NarrativePolicy ConfigurePolicy() => null;

        /// <summary>场景开始播放时回调</summary>
        protected virtual void OnStarted() { }

        /// <summary>场景完整播放完成时回调（在框架写入完成标记之后）</summary>
        protected virtual void OnCompleted() { }

        /// <inheritdoc/>
        protected override void VaultRegister() {
            Instances.Add(this);
            TypeToInstance[GetType()] = this;

            if (_byKey.TryGetValue(Key, out NarrativeScenario existing) && existing.GetType() != GetType()) {
                VaultMod.Instance.Logger.Error($"NarrativeScenario Key conflict: '{Key}' between {existing.GetType().FullName} and {GetType().FullName}");
                return;
            }
            _byKey[Key] = this;
        }

        /// <inheritdoc/>
        public override void VaultSetup() {
            SetStaticDefaults();
            Policy = ConfigurePolicy();
        }

        internal NarrativeGraph BuildGraph() {
            NarrativeGraph graph = new();
            Build(new NarrativeComposer(graph, Mod?.Name));
            return graph;
        }

        internal void InvokeStarted() => OnStarted();
        internal void InvokeCompleted() => OnCompleted();

        /// <summary>启动本场景（忙时会自动入队）</summary>
        public bool Begin() => NarrativeRunner.Begin(this);

        /// <summary>按 Key 查找已注册场景（推荐传入完整 <c>ModName/TypeName</c>）</summary>
        public static NarrativeScenario Find(string key)
            => key != null && _byKey.TryGetValue(key, out var scenario) ? scenario : null;

        /// <summary>按场景类型查找已注册场景，避免手写字符串 Key</summary>
        public static T Find<T>() where T : NarrativeScenario
            => TypeToInstance.TryGetValue(typeof(T), out var scenario) ? (T)scenario : null;

        /// <summary>按场景类型解析默认 Key，优先返回已注册实例的 Key，未注册时回退为类型全名</summary>
        public static string GetKey<T>() where T : NarrativeScenario
            => Find<T>()?.Key ?? typeof(T).FullName;

        /// <summary>所有已注册场景</summary>
        public static IReadOnlyCollection<NarrativeScenario> All => _byKey.Values;

        /// <summary>清空场景注册表（卸载时调用）</summary>
        internal static void ClearRegistry() => _byKey.Clear();
    }
}
