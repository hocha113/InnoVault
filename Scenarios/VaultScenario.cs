using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景基类，InnoVault 场景框架的根概念。游戏由一个个场景组合而成：场景可以拥有一整块舞台
    /// （挂一个 <see cref="ScenarioStage"/> 组件），可以只作用于当前舞台里的一片区域（挂一个 <see cref="ScenarioArea"/>），
    /// 也可以不占空间，只是叠加在当前舞台上的一段玩法<br/>
    /// tML 加载出的那个对象是<b>场景模板</b>，只用于查询与创建实例，永远不会被激活；
    /// 每次激活都会新建一个<b>场景实例</b>，所以写在字段里的状态天然只属于这一次激活<br/>
    /// 进入、离开与查询都经 <see cref="ScenarioDirector"/>
    /// </summary>
    public abstract partial class VaultScenario : VaultType<VaultScenario>
    {
        private static readonly Dictionary<string, VaultScenario> _byKey = new(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Func<VaultScenario>> _factories = [];
        //实例由工厂新建，不经过 tML 的注册流程，Mod 需要从模板补上（其 setter 为 internal）
        private static readonly PropertyInfo _modProperty = typeof(ModType).GetProperty(nameof(Mod));

        private readonly List<ScenarioComponent> _components = [];
        private readonly List<VaultScenario> _children = [];
        private readonly List<VaultScenario> _childTemplates = [];

        /// <summary>
        /// 场景唯一 Key，默认使用 <c>ModName/TypeName</c>。它会进入全局注册表，也是舞台文件名的一部分，发布后不要改动
        /// </summary>
        public virtual string Key {
            get {
                Type type = GetType();
                return TypeToMod.TryGetValue(type, out Mod mod)
                    ? GetFullName(mod.Name, type.Name)
                    : type.FullName;
            }
        }

        /// <summary>这个对象对应的场景模板；模板自身的 Template 指向自己</summary>
        public VaultScenario Template { get; private set; }

        /// <summary>这个对象是否是场景模板</summary>
        public bool IsTemplate => ReferenceEquals(Template, this);

        /// <summary>生命周期状态，模板永远是 <see cref="ScenarioStatus.Pending"/></summary>
        public ScenarioStatus Status { get; internal set; }

        /// <summary>父场景实例；舞台场景与会话层场景没有父场景</summary>
        public VaultScenario Parent { get; private set; }

        /// <summary>当前处于激活中的子场景实例</summary>
        public IReadOnlyList<VaultScenario> Children => _children;

        /// <summary>Compose 时挂上的组件，按挂载顺序排列</summary>
        public IReadOnlyList<ScenarioComponent> Components => _components;

        /// <summary>这个实例的舞台组件，不拥有舞台时为空；模板上始终为空</summary>
        public ScenarioStage Stage { get; private set; }

        /// <summary>这个实例是否拥有舞台</summary>
        public bool OwnsStage => Stage != null;

        /// <summary>这个实例的区域组件，没有时为空；模板上始终为空</summary>
        public ScenarioArea Area { get; private set; }

        /// <summary>进入激活状态后经过的游戏帧数</summary>
        public uint ActiveTicks { get; internal set; }

        /// <summary>
        /// 没有父场景、又不拥有舞台时，是否挂在会话层并在换舞台后继续存活。默认为假：挂在当前舞台场景下，随它停用。
        /// 挂了区域、锚点、还原区这类带舞台坐标的组件时不能挂在会话层
        /// </summary>
        public virtual bool SurvivesTravel => false;

        internal bool StartPending { get; set; }

        internal IReadOnlyList<VaultScenario> ChildTemplates => _childTemplates;

        /// <summary>
        /// 声明组件与子场景。实例创建后立即调用，每次激活都对新实例重新执行，只做声明，不要产生副作用
        /// </summary>
        /// <param name="composer">组合构建器</param>
        protected virtual void Compose(ScenarioComposer composer) { }

        /// <summary>
        /// 激活前的否决点，返回 <see langword="false"/> 时请求以 <see cref="ScenarioRejectReason.Vetoed"/> 被拒绝。
        /// 声明式子场景被否决时只是不激活，不影响父场景
        /// </summary>
        /// <param name="transition">将要发生的转场</param>
        protected virtual bool CanActivate(ScenarioTransition transition) => true;

        /// <summary>
        /// 激活时调用，组件的 OnActivate 已经执行。经换舞台进入的舞台场景此时舞台已读入、玩家尚未出生，运行在读档线程上，
        /// 只做数据与世界状态的工作；换舞台的数据包在这里读取。会话开始时的根场景例外：它在玩家出生之后、主线程上激活
        /// </summary>
        /// <param name="transition">引起这次激活的转场</param>
        protected virtual void OnActivate(ScenarioTransition transition) { }

        /// <summary>进入激活状态后、第一次更新之前调用，主线程，玩家已经在世界里；演出与 UI 从这里开始</summary>
        protected virtual void OnStart() { }

        /// <summary>激活期间每个游戏帧调用，在组件的 Update 之后，主线程</summary>
        protected virtual void Update() { }

        /// <summary>
        /// 停用时调用，子场景已经先行停用，组件的 OnDeactivate 随后执行。舞台场景此时舞台仍在、尚未保存；
        /// 换舞台的数据包在这里写入
        /// </summary>
        /// <param name="transition">引起这次停用的转场</param>
        protected virtual void OnDeactivate(ScenarioTransition transition) { }

        /// <summary>按类型取第一个挂载的组件，没有时返回空</summary>
        public T GetComponent<T>() where T : ScenarioComponent {
            foreach (ScenarioComponent component in _components) {
                if (component is T match) {
                    return match;
                }
            }
            return null;
        }

        /// <summary>按类型取第一个挂载的组件</summary>
        public bool TryGetComponent<T>(out T component) where T : ScenarioComponent {
            component = GetComponent<T>();
            return component != null;
        }

        /// <summary>按 Key 查找场景模板（推荐传入完整的 <c>ModName/TypeName</c>）</summary>
        public static VaultScenario FindTemplate(string key)
            => key != null && _byKey.TryGetValue(key, out VaultScenario template) ? template : null;

        /// <summary>按类型查找场景模板，类型未注册时返回空</summary>
        public static T FindTemplate<T>() where T : VaultScenario => FindTemplate(typeof(T)) as T;

        internal static VaultScenario FindTemplate(Type type)
            => type != null && TypeToInstance.TryGetValue(type, out VaultScenario template) ? template : null;

        /// <summary>所有已注册的场景模板</summary>
        public static IReadOnlyCollection<VaultScenario> Templates => _byKey.Values;

        /// <inheritdoc/>
        protected override void VaultRegister() {
            Template = this;
            Type type = GetType();
            if (_byKey.TryGetValue(Key, out VaultScenario existing) && existing.GetType() != type) {
                VaultMod.Instance.Logger.Error($"VaultScenario Key conflict: '{Key}' between {existing.GetType().FullName} and {type.FullName}");
                return;
            }
            Instances.Add(this);
            TypeToInstance[type] = this;
            _byKey[Key] = this;
            try {
                _factories[type] = Expression.Lambda<Func<VaultScenario>>(Expression.New(type)).Compile();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"VaultScenario {type.FullName}: factory compile failed, falling back to Activator ({ex.Message})");
            }
        }

        /// <inheritdoc/>
        public override void VaultSetup() {
            SetStaticDefaults();
        }

        internal VaultScenario CreateInstance() {
            Type type = GetType();
            VaultScenario instance = _factories.TryGetValue(type, out Func<VaultScenario> factory)
                ? factory()
                : (VaultScenario)Activator.CreateInstance(type, nonPublic: true);
            _modProperty?.SetValue(instance, Mod);
            instance.Template = this;
            return instance;
        }

        internal void InvokeCompose(ScenarioComposer composer) => Compose(composer);
        internal bool InvokeCanActivate(ScenarioTransition transition) => CanActivate(transition);
        internal void InvokeActivate(ScenarioTransition transition) => OnActivate(transition);
        internal void InvokeStart() => OnStart();
        internal void InvokeUpdate() => Update();
        internal void InvokeDeactivate(ScenarioTransition transition) => OnDeactivate(transition);

        internal void AttachComponent(ScenarioComponent component) {
            if (component.Scenario != null) {
                throw new InvalidOperationException($"{component.GetType().Name} is already attached to {component.Scenario.Key}");
            }
            if (component is ScenarioStage stage) {
                if (Stage != null) {
                    throw new InvalidOperationException($"{Key} declares more than one ScenarioStage");
                }
                Stage = stage;
            }
            if (component is ScenarioArea area) {
                if (Area != null) {
                    throw new InvalidOperationException($"{Key} declares more than one ScenarioArea");
                }
                Area = area;
            }
            _components.Add(component);
            component.Attach(this);
        }

        internal string ValidateComposition() {
            foreach (ScenarioComponent component in _components) {
                string error = component.InvokeValidate();
                if (error != null) {
                    return $"{component.GetType().Name}: {error}";
                }
            }
            return null;
        }

        internal bool HasStageBoundComponents {
            get {
                foreach (ScenarioComponent component in _components) {
                    if (component.BindsToStage) {
                        return true;
                    }
                }
                return false;
            }
        }

        internal void AddChildTemplate(VaultScenario template) {
            if (ReferenceEquals(template, Template)) {
                throw new InvalidOperationException($"{Key} declares itself as a child scenario");
            }
            _childTemplates.Add(template);
        }

        internal void LinkChild(VaultScenario child) {
            child.Parent = this;
            _children.Add(child);
        }

        internal void UnlinkFromParent() {
            Parent?._children.Remove(this);
            Parent = null;
        }

        internal static void ClearRegistry() {
            _byKey.Clear();
            _factories.Clear();
        }
    }
}
