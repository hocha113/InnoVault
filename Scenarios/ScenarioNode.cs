using System;
using System.Collections.Generic;
using System.IO;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景节点：场景树的基本单位。一个场景实例就是一棵节点树，规则、镜头、环境、区域、舞台这些能力各是一个节点，
    /// 在 <see cref="VaultScenario"/> 的 Compose 里经 <see cref="ScenarioComposer.Add(ScenarioNode)"/> 挂到场景上，
    /// 或者经 <see cref="AddChild"/> 挂到另一个节点下面<br/>
    /// <b>树的形状在 Compose 时定下</b>：运行期间不增删节点，只开关（<see cref="Enabled"/>）；需要运行时增删的玩法做成子场景<br/>
    /// <b>作用范围随树继承</b>：挂在 <see cref="ScenarioArea"/> 下面的节点只对身在那片区域里的玩家生效（<see cref="ScopeArea"/>），
    /// 一个场景里可以有多片区域，各带各的规则、镜头与环境<br/>
    /// 生命周期钩子按树的先序执行（父节点先于子节点），停用时反过来；一个节点对象只属于一个场景实例
    /// </summary>
    public abstract class ScenarioNode
    {
        private static readonly ScenarioNode[] _noChildren = [];
        private static readonly string[] _noGroups = [];

        private List<ScenarioNode> _children;
        private string _name;
        private string[] _groups;
        private bool _enabled = true;
        private bool _announcedInTree = true;
        private ScenarioArea _scope;
        private bool _scopeResolved;

        /// <summary>节点所属的场景实例，挂载后才有值</summary>
        public VaultScenario Scenario { get; private set; }

        /// <summary>父节点；直接挂在场景上的节点没有父节点</summary>
        public ScenarioNode Parent { get; private set; }

        /// <summary>子节点，按挂载顺序排列</summary>
        public IReadOnlyList<ScenarioNode> Children => _children ?? (IReadOnlyList<ScenarioNode>)_noChildren;

        /// <summary>
        /// 节点名，用于按路径查找（<see cref="VaultScenario.GetNode{T}(string)"/>）；不设置时是类型名。同一个父节点下重名时按挂载顺序取第一个
        /// </summary>
        public string Name {
            get => _name ?? GetType().Name;
            init => _name = value;
        }

        /// <summary>
        /// 节点所在的分组，用来成批开关（<see cref="VaultScenario.SetGroupEnabled"/>）与查找（<see cref="VaultScenario.GetNodesInGroup"/>）。
        /// 分组名区分大小写
        /// </summary>
        public IReadOnlyList<string> Groups {
            get => _groups ?? _noGroups;
            init => _groups = value == null || value.Count == 0 ? null : [.. value];
        }

        /// <summary>
        /// 多个处于激活中的场景挂了同类节点时的叠加层级：越大越靠上，同级按激活顺序，后激活的在上。
        /// 每个属性取最上层已设置的值，没设置的看下一层
        /// </summary>
        public int LayerPriority { get; set; }

        /// <summary>
        /// 这个节点是否启用，缺省为真。停用的节点连同它下面的整棵子树都不再更新、不参与规则合成、区域判定、物块保护与锚点查找；
        /// 激活、开始与停用这三个生命周期钩子照常执行。运行期间可以随时改，即时生效<br/>
        /// 多人模式下在裁决端改：它会随场景的网络状态发给客户端（自动置位 <see cref="VaultScenario.NetUpdate"/>）；
        /// 客户端上自己改的值会被下一次同步覆盖
        /// </summary>
        public bool Enabled {
            get => _enabled;
            set {
                if (_enabled == value) {
                    return;
                }
                _enabled = value;
                Scenario?.NotifyNodeToggled();
            }
        }

        /// <summary>这个节点与它的所有祖先节点是否都启用</summary>
        public bool EnabledInTree {
            get {
                for (ScenarioNode node = this; node != null; node = node.Parent) {
                    if (!node._enabled) {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// 这个节点的作用范围：最近的一个祖先 <see cref="ScenarioArea"/>；没有时是场景的主区域（<see cref="VaultScenario.Area"/>，
        /// 它自己除外），再没有时为空，表示作用于整块舞台。跟随玩家的节点只对身在这片区域里的玩家生效，按位置的节点只管这片区域里的格子
        /// </summary>
        public ScenarioArea ScopeArea {
            get {
                if (_scopeResolved) {
                    return _scope;
                }
                ScenarioArea scope = FindScope();
                //组合结束后树的形状不再变化，结果可以记住
                if (Scenario != null && Scenario.Composed) {
                    _scope = scope;
                    _scopeResolved = true;
                }
                return scope;
            }
        }

        /// <summary>从场景到这个节点的路径，各级节点名用 <c>/</c> 连接</summary>
        public string Path => Parent == null ? Name : Parent.Path + "/" + Name;

        //带舞台坐标的节点（区域、锚点、还原区）只在声明它们的舞台里有意义，挂了它们的场景不能放在会话层
        internal virtual bool BindsToStage => false;

        /// <summary>
        /// 把一个节点挂到这个节点下面，返回这个节点自己以便连续书写。只能在 Compose 期间调用；
        /// 子节点可以先挂好再把父节点交给 <see cref="ScenarioComposer.Add(ScenarioNode)"/>，也可以反过来
        /// </summary>
        /// <param name="child">要挂载的子节点，不能已经挂在别处</param>
        /// <exception cref="ArgumentNullException">子节点为空</exception>
        /// <exception cref="InvalidOperationException">Compose 已经结束，子节点已挂在别处，或者会形成环</exception>
        public ScenarioNode AddChild(ScenarioNode child) {
            ArgumentNullException.ThrowIfNull(child);
            if (child.Parent != null || child.Scenario != null) {
                throw new InvalidOperationException($"{child.GetType().Name} is already attached");
            }
            if (Scenario != null && Scenario.Composed) {
                throw new InvalidOperationException($"{Scenario.Key} has finished composing; nodes can only be declared inside Compose");
            }
            for (ScenarioNode node = this; node != null; node = node.Parent) {
                if (ReferenceEquals(node, child)) {
                    throw new InvalidOperationException($"{child.GetType().Name} cannot be attached under itself");
                }
            }
            (_children ??= []).Add(child);
            child.Parent = this;
            //父节点已经在场景上：子树现在就登记；否则等父节点挂到场景上时一并登记
            Scenario?.AttachSubtree(child);
            return this;
        }

        /// <summary>按类型（与名字）取第一个直接子节点，没有时返回空</summary>
        /// <param name="name">节点名，为空时只按类型</param>
        public T GetChild<T>(string name = null) where T : ScenarioNode {
            if (_children == null) {
                return null;
            }
            foreach (ScenarioNode child in _children) {
                if (child is T match && (name == null || child.Name == name)) {
                    return match;
                }
            }
            return null;
        }

        /// <summary>按类型（与名字）在这个节点下面的整棵子树里找第一个节点（先序），没有时返回空</summary>
        /// <param name="name">节点名，为空时只按类型</param>
        public T FindNode<T>(string name = null) where T : ScenarioNode {
            if (_children == null) {
                return null;
            }
            foreach (ScenarioNode child in _children) {
                if (child is T match && (name == null || child.Name == name)) {
                    return match;
                }
                if (child.FindNode<T>(name) is T nested) {
                    return nested;
                }
            }
            return null;
        }

        /// <summary>按相对路径取节点：各级节点名用 <c>/</c> 连接，从这个节点的子节点开始；找不到或类型不符时返回空</summary>
        /// <param name="path">相对路径，例如 <c>BossRoom/Rules</c></param>
        public T GetNode<T>(string path) where T : ScenarioNode => Resolve(Children, path) as T;

        /// <summary>取最近的一个指定类型的祖先节点，没有时返回空</summary>
        public T GetAncestor<T>() where T : ScenarioNode {
            for (ScenarioNode node = Parent; node != null; node = node.Parent) {
                if (node is T match) {
                    return match;
                }
            }
            return null;
        }

        /// <summary>这个节点是否在指定分组里</summary>
        /// <param name="group">分组名</param>
        public bool IsInGroup(string group) => _groups != null && group != null && Array.IndexOf(_groups, group) >= 0;

        /// <summary>挂到场景实例上时调用，此时场景仍在 Compose 中，尚未激活</summary>
        protected virtual void OnAttach() { }

        /// <summary>
        /// Compose 结束后调用，检查这个节点的声明是否完整；返回空表示合法，否则请求以
        /// <see cref="ScenarioRejectReason.InvalidComposition"/> 被拒绝，返回值作为说明
        /// </summary>
        protected virtual string Validate() => null;

        /// <summary>所属场景激活时调用，先于场景自身的 OnActivate，时机与线程见 <see cref="VaultScenario"/> 的 OnActivate</summary>
        /// <param name="transition">引起这次激活的转场</param>
        protected virtual void OnActivate(ScenarioTransition transition) { }

        /// <summary>所属场景进入激活状态后、第一次更新之前调用，主线程</summary>
        protected virtual void OnStart() { }

        /// <summary>所属场景激活期间每个游戏帧调用，先于场景自身的 Update，主线程。停用的节点（<see cref="EnabledInTree"/> 为假）不调用</summary>
        protected virtual void Update() { }

        /// <summary>所属场景停用时调用，在场景自身的 OnDeactivate 之后，时机与线程见 <see cref="VaultScenario"/> 的 OnDeactivate</summary>
        /// <param name="transition">引起这次停用的转场</param>
        protected virtual void OnDeactivate(ScenarioTransition transition) { }

        /// <summary>
        /// 所属场景激活期间，这个节点在树里的启停（<see cref="EnabledInTree"/>）发生变化时调用：自己被开关，或者某个祖先被开关。
        /// 每一端各调用一次（多人客户端在收到服务器的同步时），主线程。停用后要收掉、启用后要重新铺开的东西放在这里；
        /// 场景的激活与停用本身不触发它，那两处各有自己的钩子
        /// </summary>
        protected virtual void OnEnabledChanged() { }

        /// <summary>
        /// 这个节点在树里的启停发生变化时触发，在 <see cref="OnEnabledChanged"/> 之后，调用范围与它相同；不想为此派生一个子类时在 Compose 里订阅它，
        /// 例如客户端在某个阶段的分支被服务器打开时播一段过场
        /// </summary>
        public event Action<ScenarioNode> EnabledChanged;

        /// <summary>当前端是否对场景树有裁决权（单人或服务器），见 <see cref="VaultScenario.IsAuthority"/></summary>
        protected static bool IsAuthority => ScenarioPlatform.IsAuthority;

        /// <summary>
        /// 写入这个节点需要让客户端知道、又不能由 Compose 算出来的状态（例如生成期间写下的锚点），与 <see cref="NetReceive"/> 成对重写。
        /// 调用时机与 <see cref="VaultScenario.SendExtraData"/> 相同，各节点按树的先序依次写入。<see cref="Enabled"/> 由框架同步，不必自己写
        /// </summary>
        /// <param name="writer">读写必须对称：写了多少，<see cref="NetReceive"/> 就读多少</param>
        protected virtual void NetSend(BinaryWriter writer) { }

        /// <summary>读取 <see cref="NetSend"/> 写入的状态，在客户端调用</summary>
        /// <param name="reader">按 <see cref="NetSend"/> 的顺序读取</param>
        protected virtual void NetReceive(BinaryReader reader) { }

        internal void Attach(VaultScenario scenario) {
            Scenario = scenario;
            OnAttach();
        }

        //按网络状态写入启用标志：不触发刷新与重发，由调用方统一处理
        internal bool SetEnabledSilently(bool value) {
            if (_enabled == value) {
                return false;
            }
            _enabled = value;
            return true;
        }

        //把“上次通知过的启停状态”对齐到现状，返回两者是否不同。场景进入激活状态时对齐一次作为基线，之后每次有开关变动都比对
        internal bool SyncAnnouncedState() {
            bool now = EnabledInTree;
            if (now == _announcedInTree) {
                return false;
            }
            _announcedInTree = now;
            return true;
        }

        internal void InvokeEnabledChanged() {
            OnEnabledChanged();
            EnabledChanged?.Invoke(this);
        }

        internal static ScenarioNode Resolve(IReadOnlyList<ScenarioNode> level, string path) {
            if (string.IsNullOrEmpty(path)) {
                return null;
            }
            ScenarioNode current = null;
            foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
                ScenarioNode next = null;
                for (int i = 0; i < level.Count; i++) {
                    if (level[i].Name == segment) {
                        next = level[i];
                        break;
                    }
                }
                if (next == null) {
                    return null;
                }
                current = next;
                level = next.Children;
            }
            return current;
        }

        private ScenarioArea FindScope() {
            for (ScenarioNode node = Parent; node != null; node = node.Parent) {
                if (node is ScenarioArea area) {
                    return area;
                }
            }
            ScenarioArea primary = Scenario?.Area;
            return ReferenceEquals(primary, this) ? null : primary;
        }

        internal void InvokeNetSend(BinaryWriter writer) => NetSend(writer);
        internal void InvokeNetReceive(BinaryReader reader) => NetReceive(reader);
        internal string InvokeValidate() => Validate();
        internal void InvokeActivate(ScenarioTransition transition) => OnActivate(transition);
        internal void InvokeStart() => OnStart();
        internal void InvokeUpdate() => Update();
        internal void InvokeDeactivate(ScenarioTransition transition) => OnDeactivate(transition);
    }

    /// <summary>
    /// <see cref="ScenarioNode"/> 的书写辅助
    /// </summary>
    public static class ScenarioNodeExtensions
    {
        /// <summary>
        /// 把若干节点挂到这个节点下面，返回这个节点自己（保留它的具体类型），用于在 Compose 里一口气写出一棵子树：
        /// <c>composer.Add(new ScenarioArea(room).With(new ScenarioRules { NoNaturalSpawns = true }, new ScenarioCamera()))</c>
        /// </summary>
        /// <typeparam name="T">父节点的类型</typeparam>
        /// <param name="node">父节点</param>
        /// <param name="children">要挂到它下面的子节点，按顺序挂载</param>
        public static T With<T>(this T node, params ScenarioNode[] children) where T : ScenarioNode {
            ArgumentNullException.ThrowIfNull(node);
            if (children != null) {
                foreach (ScenarioNode child in children) {
                    node.AddChild(child);
                }
            }
            return node;
        }
    }
}
