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
    /// 一个场景实例是一棵<b>节点树</b>：Compose 里挂上的节点（<see cref="ScenarioNode"/>）可以再带子节点，挂在区域下面的节点只在那片区域里生效；
    /// 子场景是挂在它下面的另一棵树。所有处于激活中的场景合起来就是场景树，由 <see cref="ScenarioDirector"/> 管理<br/>
    /// tML 加载出的那个对象是<b>场景模板</b>，只用于查询与创建实例，永远不会被激活；
    /// 每次激活都会新建一个<b>场景实例</b>，所以写在字段里的状态天然只属于这一次激活<br/>
    /// 进入、离开与查询都经 <see cref="ScenarioDirector"/><br/>
    /// 多人模式下场景树由服务器裁决，每个客户端上有一份同样的实例，所有钩子在每一端都会执行：只该发生一次的事先判断 <see cref="IsAuthority"/>。
    /// 要让其他端也拿到的激活参数写在标了 <see cref="SyncVarAttribute"/> 的成员里（它们在 Compose 之前套用），其余运行状态经
    /// <see cref="SendExtraData"/> 与 <see cref="NetUpdate"/> 同步
    /// </summary>
    public abstract partial class VaultScenario : VaultType<VaultScenario>
    {
        private static readonly Dictionary<string, VaultScenario> _byKey = new(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Func<VaultScenario>> _factories = [];
        //实例由工厂新建，不经过 tML 的注册流程，Mod 需要从模板补上（其 setter 为 internal）
        private static readonly PropertyInfo _modProperty = typeof(ModType).GetProperty(nameof(Mod));

        //直接挂在场景上的节点；整棵节点树按先序摊平在 _flat 里，生命周期派发与网络状态都按它的顺序
        private readonly List<ScenarioNode> _roots = [];
        private List<ScenarioNode> _flat = [];
        private List<ScenarioComponent> _flatComponents = [];
        private bool _flatDirty;
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

        /// <summary>直接挂在场景上的节点，按挂载顺序排列；它们各自的子节点在 <see cref="ScenarioNode.Children"/> 里</summary>
        public IReadOnlyList<ScenarioNode> Nodes => _roots;

        /// <summary>Compose 时挂上的全部组件（含挂在其他节点下面的），按节点树的先序排列</summary>
        public IReadOnlyList<ScenarioComponent> Components {
            get {
                EnsureFlat();
                return _flatComponents;
            }
        }

        //整棵节点树，先序排列（父节点在子节点之前）
        internal IReadOnlyList<ScenarioNode> AllNodes {
            get {
                EnsureFlat();
                return _flat;
            }
        }

        //Compose 已经结束，节点树的形状不再变化
        internal bool Composed { get; private set; }

        /// <summary>这个实例的舞台组件，不拥有舞台时为空；模板上始终为空</summary>
        public ScenarioStage Stage { get; private set; }

        /// <summary>这个实例是否拥有舞台</summary>
        public bool OwnsStage => Stage != null;

        /// <summary>
        /// 这个实例的主区域：第一个直接挂在场景上、又不是局部区域（<see cref="ScenarioArea.Local"/>）的 <see cref="ScenarioArea"/>，没有时为空；模板上始终为空。
        /// 没有挂在别的区域下面的节点都以它为作用范围（<see cref="ScenarioNode.ScopeArea"/>）
        /// </summary>
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
        /// 作为换舞台的目标：旧舞台即将停用、尚未存盘时调用，此刻旧舞台还完整地在内存里（每一端各一次，主线程）。
        /// 要趁旧舞台还在时做的准备放在这里，例如按主世界的进度定下这次进入的档位、给主世界里的入口收尾；
        /// 之后依次是旧舞台的停用、舞台切换、本场景的 OnActivate。多人客户端没赶上离场通知时不调用
        /// </summary>
        /// <param name="transition">这次换舞台，<see cref="ScenarioTransition.From"/> 是即将离开的舞台场景</param>
        protected virtual void OnApproach(ScenarioTransition transition) { }

        /// <summary>
        /// 激活时调用，组件的 OnActivate 已经执行。经换舞台进入的舞台场景此时舞台已读入、玩家尚未出生，运行在读档线程上，
        /// 只做数据与世界状态的工作；换舞台的数据包在这里读取。会话开始时的根场景例外：它在玩家出生之后、主线程上激活<br/>
        /// 多人模式下每一端各调用一次：服务器在读完舞台之后（主线程，此时玩家都还在路上），客户端在出生于这块舞台并收到服务器的场景树之后（主线程）。
        /// 数据包不经网络传输，每一端读到的是这一端自己的停用钩子写下的内容；世界状态由服务器改，客户端经原版的同步拿到
        /// </summary>
        /// <param name="transition">引起这次激活的转场</param>
        protected virtual void OnActivate(ScenarioTransition transition) { }

        /// <summary>
        /// 进入激活状态后、第一次更新之前调用，主线程，玩家已经在世界里；演出与 UI 从这里开始<br/>
        /// 多人模式下换舞台之后要等所有玩家到齐（或等待超时）、幕布开始拉开时才调用，生成首领这类开场动作放在这里就不会抢在玩家到场之前。
        /// 中途加入服务器的客户端在收到场景树之后调用，它的 <see cref="ActiveTicks"/> 从 0 开始，不与服务器对齐
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>激活期间每个游戏帧调用，在组件的 Update 之后，主线程</summary>
        protected virtual void Update() { }

        /// <summary>
        /// 停用时调用，子场景已经先行停用，组件的 OnDeactivate 随后执行。舞台场景此时舞台仍在、尚未保存；
        /// 换舞台的数据包在这里写入<br/>
        /// 多人模式下每一端各调用一次，服务器先于客户端。客户端掉线时它的实例也会收到一次会话结束的停用，那时 <see cref="IsAuthority"/> 仍为假
        /// </summary>
        /// <param name="transition">引起这次停用的转场</param>
        protected virtual void OnDeactivate(ScenarioTransition transition) { }

        /// <summary>按类型取第一个挂载的组件（节点树先序，含挂在其他节点下面的），没有时返回空</summary>
        public T GetComponent<T>() where T : ScenarioComponent => FindNode<T>();

        /// <summary>按类型（与名字）在整棵节点树里找第一个节点（先序），没有时返回空</summary>
        /// <param name="name">节点名（<see cref="ScenarioNode.Name"/>），为空时只按类型</param>
        public T FindNode<T>(string name = null) where T : ScenarioNode {
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i] is T match && (name == null || match.Name == name)) {
                    return match;
                }
            }
            return null;
        }

        /// <summary>按路径取节点：各级节点名用 <c>/</c> 连接，从直接挂在场景上的节点开始；找不到或类型不符时返回空</summary>
        /// <param name="path">路径，例如 <c>BossRoom/Rules</c></param>
        public T GetNode<T>(string path) where T : ScenarioNode => ScenarioNode.Resolve(_roots, path) as T;

        /// <summary>取整棵节点树里所有指定类型的节点（先序），返回新建的列表</summary>
        public List<T> GetNodes<T>() where T : ScenarioNode {
            List<T> results = [];
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i] is T match) {
                    results.Add(match);
                }
            }
            return results;
        }

        /// <summary>取某个分组里的所有节点（先序），返回新建的列表</summary>
        /// <param name="group">分组名（<see cref="ScenarioNode.Groups"/>）</param>
        public List<ScenarioNode> GetNodesInGroup(string group) {
            List<ScenarioNode> results = [];
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i].IsInGroup(group)) {
                    results.Add(nodes[i]);
                }
            }
            return results;
        }

        /// <summary>
        /// 成批开关某个分组里的节点（<see cref="ScenarioNode.Enabled"/>），返回状态发生变化的节点数。适合按阶段切换一组规则、区域与镜头；
        /// 多人模式下在裁决端调用，结果随场景的网络状态同步
        /// </summary>
        /// <param name="group">分组名</param>
        /// <param name="enabled">要设成的状态</param>
        public int SetGroupEnabled(string group, bool enabled) {
            int changed = 0;
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i].IsInGroup(group) && nodes[i].SetEnabledSilently(enabled)) {
                    changed++;
                }
            }
            if (changed > 0) {
                NotifyNodeToggled();
            }
            return changed;
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
        internal void InvokeApproach(ScenarioTransition transition) => OnApproach(transition);
        internal void InvokeActivate(ScenarioTransition transition) => OnActivate(transition);
        internal void InvokeStart() => OnStart();
        internal void InvokeUpdate() => Update();
        internal void InvokeDeactivate(ScenarioTransition transition) => OnDeactivate(transition);

        //把一个节点（连同它已经挂好的子树）直接挂到场景上
        internal void AttachComponent(ScenarioNode node) {
            if (node.Scenario != null || node.Parent != null) {
                throw new InvalidOperationException($"{node.GetType().Name} is already attached to {node.Scenario?.Key ?? node.Parent.GetType().Name}");
            }
            _roots.Add(node);
            AttachSubtree(node);
        }

        //登记一棵子树：先节点自己，再按顺序是它的子节点
        internal void AttachSubtree(ScenarioNode node) {
            if (node.Scenario != null) {
                throw new InvalidOperationException($"{node.GetType().Name} is already attached to {node.Scenario.Key}");
            }
            if (node is ScenarioStage stage) {
                if (Stage != null) {
                    throw new InvalidOperationException($"{Key} declares more than one ScenarioStage");
                }
                if (node.Parent != null) {
                    throw new InvalidOperationException($"{Key}: ScenarioStage must be attached directly to the scenario");
                }
                Stage = stage;
            }
            //主区域是第一个直接挂在场景上、又没有声明为局部的区域，其余区域只管自己的子节点
            if (node is ScenarioArea { Local: false } area && node.Parent == null && Area == null) {
                Area = area;
            }
            _flatDirty = true;
            node.Attach(this);
            IReadOnlyList<ScenarioNode> children = node.Children;
            for (int i = 0; i < children.Count; i++) {
                AttachSubtree(children[i]);
            }
        }

        internal void FinishCompose() {
            EnsureFlat();
            Composed = true;
        }

        //按类型取第一个启用着的节点：幕布、加载画面、演出挂点这类“取一个来用”的节点，停用的等于没挂
        internal T FindEnabled<T>() where T : ScenarioNode {
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i] is T match && match.EnabledInTree) {
                    return match;
                }
            }
            return null;
        }

        internal string ValidateComposition() {
            foreach (ScenarioNode node in AllNodes) {
                string error = node.InvokeValidate();
                if (error != null) {
                    return $"{node.GetType().Name}: {error}";
                }
            }
            return null;
        }

        internal bool HasStageBoundComponents {
            get {
                foreach (ScenarioNode node in AllNodes) {
                    if (node.BindsToStage) {
                        return true;
                    }
                }
                return false;
            }
        }

        //节点的开关变了：处于激活中时让导演层重建合成快照，并在裁决端排一次网络同步
        internal void NotifyNodeToggled() {
            if (Status == ScenarioStatus.Active) {
                ScenarioDirector.NodeToggled(this);
            }
        }

        //所有区域节点清空参与状态（场景停用时）
        internal void ClearAreaEngagement() {
            foreach (ScenarioNode node in AllNodes) {
                if (node is ScenarioArea area) {
                    area.ClearEngaged();
                }
            }
        }

        //场景进入激活状态时调用：各节点此刻的启停记为基线，激活前写好的开关不算变化
        internal void SyncNodeBaseline() {
            foreach (ScenarioNode node in AllNodes) {
                node.SyncAnnouncedState();
            }
        }

        private void EnsureFlat() {
            if (!_flatDirty) {
                return;
            }
            List<ScenarioNode> flat = [];
            List<ScenarioComponent> components = [];
            foreach (ScenarioNode root in _roots) {
                Flatten(root, flat, components);
            }
            //整体替换：别的线程读到的始终是完整的一份
            _flat = flat;
            _flatComponents = components;
            _flatDirty = false;
        }

        private static void Flatten(ScenarioNode node, List<ScenarioNode> flat, List<ScenarioComponent> components) {
            flat.Add(node);
            if (node is ScenarioComponent component) {
                components.Add(component);
            }
            IReadOnlyList<ScenarioNode> children = node.Children;
            for (int i = 0; i < children.Count; i++) {
                Flatten(children[i], flat, components);
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
