using System;
using System.Collections.Generic;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景框架的对外入口：发起激活、停用与世界切换，查询当前处于激活中的场景<br/>
    /// 会话（从菜单进入世界到保存退出）之外没有任何场景实例；多人模式下 v1 不开会话，所有请求返回
    /// <see cref="ScenarioRejectReason.MultiplayerUnsupported"/><br/>
    /// 任意时刻世界层恰好一个拥有世界的实例（<see cref="World"/>），不拥有世界的实例挂在它下面，
    /// 或者挂在会话层（<see cref="VaultScenario.SurvivesTravel"/>）
    /// </summary>
    public static class ScenarioDirector
    {
        private static readonly List<VaultScenario> _active = [];
        private static readonly List<VaultScenario> _sessionLayer = [];
        private static VaultScenario[] _tickBuffer = [];
        private static readonly IScenarioAuthority _localAuthority = new LocalScenarioAuthority();
        private static readonly IScenarioAuthority _multiplayerGate = new MultiplayerScenarioGate();
        private static bool _multiplayerNoticeLogged;
        //会话结束期间拒绝新请求，否则停用钩子里发起的激活会留下收不到 OnDeactivate 的实例
        private static bool _endingSession;

        /// <summary>本次会话的根场景实例，会话之外为空</summary>
        public static VaultScenario Root { get; private set; }

        /// <summary>当前世界层的场景实例（身处主世界时就是 <see cref="Root"/>），会话之外为空</summary>
        public static VaultScenario World { get; private set; }

        /// <summary>所有处于激活中的场景实例，按激活顺序排列</summary>
        public static IReadOnlyList<VaultScenario> Active => _active;

        /// <summary>是否处于会话中</summary>
        public static bool InSession => Root != null;

        /// <summary>是否正在进行世界切换</summary>
        public static bool IsTraveling { get; private set; }

        /// <summary>当前是否身处主世界</summary>
        public static bool IsHome => World != null && ReferenceEquals(World, Root);

        /// <summary>
        /// 此刻能否发起场景请求，<see cref="ScenarioRejectReason.None"/> 表示可以。物品、UI 等入口可以先用它判断是否可用
        /// </summary>
        public static ScenarioRejectReason Availability {
            get {
                if (ScenarioPlatform.NetRole != ScenarioNetRole.SinglePlayer) {
                    return ScenarioRejectReason.MultiplayerUnsupported;
                }
                if (!InSession) {
                    return ScenarioRejectReason.NoSession;
                }
                return IsTraveling || _endingSession ? ScenarioRejectReason.Busy : ScenarioRejectReason.None;
            }
        }

        /// <summary>当前世界层的场景是否为 <typeparamref name="T"/></summary>
        public static bool IsIn<T>() where T : VaultScenario => World is T;

        /// <summary>取第一个处于激活中的 <typeparamref name="T"/> 实例，没有时返回空</summary>
        public static T Get<T>() where T : VaultScenario {
            foreach (VaultScenario scenario in _active) {
                if (scenario is T match && scenario.Status == ScenarioStatus.Active) {
                    return match;
                }
            }
            return null;
        }

        /// <summary>取第一个处于激活中的 <typeparamref name="T"/> 实例</summary>
        public static bool TryGet<T>(out T instance) where T : VaultScenario {
            instance = Get<T>();
            return instance != null;
        }

        /// <summary>取所有处于激活中的 <typeparamref name="T"/> 实例，返回新建的列表</summary>
        public static List<T> GetAll<T>() where T : VaultScenario {
            List<T> results = [];
            foreach (VaultScenario scenario in _active) {
                if (scenario is T match && scenario.Status == ScenarioStatus.Active) {
                    results.Add(match);
                }
            }
            return results;
        }

        /// <summary>激活一个不拥有世界的场景</summary>
        /// <typeparam name="T">场景类型</typeparam>
        /// <param name="parent">父场景实例；为空时挂在当前世界场景下，<see cref="VaultScenario.SurvivesTravel"/> 为真时挂在会话层</param>
        public static ScenarioResult Activate<T>(VaultScenario parent = null) where T : VaultScenario
            => Activate(VaultScenario.FindTemplate(typeof(T)), parent);

        /// <summary>激活一个不拥有世界的场景</summary>
        /// <param name="template">场景模板（传入实例时取它的模板）</param>
        /// <param name="parent">父场景实例；为空时挂在当前世界场景下，<see cref="VaultScenario.SurvivesTravel"/> 为真时挂在会话层</param>
        public static ScenarioResult Activate(VaultScenario template, VaultScenario parent = null) {
            template = template?.Template;
            if (template == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "template is null or not registered");
            }
            ScenarioRejectReason reason = Review(new ScenarioRequest(ScenarioRequestKind.Activate, template, parent));
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (parent != null && (parent.IsTemplate || parent.Status != ScenarioStatus.Active)) {
                return Reject(ScenarioRejectReason.InvalidParent, $"{parent.Key} is not an active instance");
            }
            if (!TryCreate(template, out VaultScenario instance, out ScenarioResult failure)) {
                return failure;
            }
            if (instance.OwnsWorld) {
                return Reject(ScenarioRejectReason.RequiresTravel, template.Key);
            }
            bool sessionLayer = parent == null && instance.SurvivesTravel;
            parent ??= sessionLayer ? null : World;
            ScenarioTransition transition = new(ScenarioTransitionKind.Activation, null, instance);
            if (!PassesVeto(instance, transition)) {
                return Reject(ScenarioRejectReason.Vetoed, template.Key);
            }
            ActivateTree(instance, parent, sessionLayer, transition);
            return ScenarioResult.Accept(instance);
        }

        /// <summary>停用一个不拥有世界的场景实例，它的子场景先行停用</summary>
        /// <param name="instance">要停用的实例</param>
        public static ScenarioResult Deactivate(VaultScenario instance) {
            if (instance == null || instance.IsTemplate) {
                return Reject(ScenarioRejectReason.NotActive, "not an instance");
            }
            ScenarioRejectReason reason = Review(new ScenarioRequest(ScenarioRequestKind.Deactivate, instance, null));
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (instance.Status != ScenarioStatus.Active) {
                return Reject(ScenarioRejectReason.NotActive, $"{instance.Key} is {instance.Status}");
            }
            if (instance.OwnsWorld) {
                return Reject(ScenarioRejectReason.CannotDeactivateWorld, instance.Key);
            }
            DeactivateTree(instance, new ScenarioTransition(ScenarioTransitionKind.Deactivation, instance, null));
            return ScenarioResult.Accept(instance);
        }

        /// <summary>切换到一个拥有世界的场景</summary>
        /// <typeparam name="T">场景类型</typeparam>
        public static ScenarioResult Travel<T>() where T : VaultScenario => Travel(VaultScenario.FindTemplate(typeof(T)));

        /// <summary>切换到一个拥有世界的场景</summary>
        /// <param name="template">场景模板（传入实例时取它的模板）</param>
        public static ScenarioResult Travel(VaultScenario template) {
            template = template?.Template;
            if (template == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "template is null or not registered");
            }
            ScenarioRejectReason reason = Review(new ScenarioRequest(ScenarioRequestKind.Travel, template, null));
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (World != null && ReferenceEquals(World.Template, template)) {
                return Reject(ScenarioRejectReason.AlreadyThere, template.Key);
            }
            if (!TryCreate(template, out VaultScenario instance, out ScenarioResult failure)) {
                return failure;
            }
            if (!instance.OwnsWorld) {
                return Reject(ScenarioRejectReason.RequiresWorld, template.Key);
            }
            string invalid = instance.World.Validate();
            if (invalid != null) {
                return Reject(ScenarioRejectReason.InvalidWorld, $"{template.Key}: {invalid}");
            }
            return Reject(ScenarioRejectReason.WorldBackendUnavailable, "world travel arrives in phase 1");
        }

        /// <summary>回到主世界，即切换到根场景</summary>
        public static ScenarioResult ReturnHome() => Travel(VaultScenario.FindTemplate(typeof(RootScenario)));

        //本地玩家进入世界时由宿主调用：建立根场景，会话开始
        internal static void StartSession() {
            if (ScenarioPlatform.NetRole != ScenarioNetRole.SinglePlayer) {
                NoteMultiplayer();
                return;
            }
            if (IsTraveling) {
                return;
            }
            if (InSession) {
                ScenarioPlatform.Error("Scenario.StaleSession", "[Scenarios] Entered a world while the previous session was still open; ending it now");
                EndSession();
            }
            VaultScenario template = VaultScenario.FindTemplate(typeof(RootScenario));
            if (template == null || !TryCreate(template, out VaultScenario root, out ScenarioResult failure)) {
                ScenarioPlatform.Error("Scenario.StartSession", "[Scenarios] Root scenario could not be created; session not started");
                return;
            }
            Root = root;
            World = root;
            ActivateTree(root, null, false, new ScenarioTransition(ScenarioTransitionKind.SessionStart, null, root));
        }

        //保存退出时由宿主调用：先停世界子树，再停会话层；重复调用无害
        internal static void EndSession() {
            if (!InSession || _endingSession) {
                return;
            }
            _endingSession = true;
            try {
                ScenarioTransition transition = new(ScenarioTransitionKind.SessionEnd, World, null);
                if (World != null) {
                    DeactivateTree(World, transition);
                }
                for (int i = _sessionLayer.Count - 1; i >= 0; i--) {
                    if (i < _sessionLayer.Count) {
                        DeactivateTree(_sessionLayer[i], transition);
                    }
                }
            } finally {
                _endingSession = false;
                ClearState();
            }
        }

        //每个游戏帧由宿主在主线程调用。按快照遍历，钩子里发起的激活与停用不会打乱本帧的迭代
        internal static void Tick() {
            if (!InSession) {
                return;
            }
            int count = _active.Count;
            if (_tickBuffer.Length < count) {
                _tickBuffer = new VaultScenario[Math.Max(count, _tickBuffer.Length * 2)];
            }
            _active.CopyTo(_tickBuffer);
            for (int i = 0; i < count; i++) {
                VaultScenario scenario = _tickBuffer[i];
                _tickBuffer[i] = null;
                if (scenario.Status != ScenarioStatus.Active) {
                    continue;
                }
                if (scenario.StartPending) {
                    scenario.StartPending = false;
                    RunStart(scenario);
                    if (scenario.Status != ScenarioStatus.Active) {
                        continue;
                    }
                }
                RunUpdate(scenario);
                scenario.ActiveTicks++;
            }
        }

        //模组卸载时调用：不触发任何钩子，直接丢弃全部状态
        internal static void Reset() {
            ClearState();
            _tickBuffer = [];
            _multiplayerNoticeLogged = false;
            _endingSession = false;
        }

        private static void ClearState() {
            _active.Clear();
            _sessionLayer.Clear();
            Root = null;
            World = null;
            IsTraveling = false;
        }

        private static ScenarioRejectReason Review(in ScenarioRequest request) {
            IScenarioAuthority authority = ScenarioPlatform.NetRole == ScenarioNetRole.SinglePlayer
                ? _localAuthority
                : _multiplayerGate;
            ScenarioRejectReason reason = authority.Review(request);
            if (reason == ScenarioRejectReason.MultiplayerUnsupported) {
                NoteMultiplayer();
                return reason;
            }
            if (reason != ScenarioRejectReason.None) {
                return reason;
            }
            if (!InSession) {
                return ScenarioRejectReason.NoSession;
            }
            return IsTraveling || _endingSession ? ScenarioRejectReason.Busy : ScenarioRejectReason.None;
        }

        private static void NoteMultiplayer() {
            if (_multiplayerNoticeLogged) {
                return;
            }
            _multiplayerNoticeLogged = true;
            ScenarioPlatform.Info("[Scenarios] Multiplayer is not supported yet; the scenario director stays offline in multiplayer");
        }

        private static ScenarioResult Reject(ScenarioRejectReason reason, string detail = null) => ScenarioResult.Reject(reason, detail);

        private static bool TryCreate(VaultScenario template, out VaultScenario instance, out ScenarioResult failure) {
            instance = null;
            try {
                VaultScenario created = template.CreateInstance();
                ScenarioComposer composer = new(created);
                created.InvokeCompose(composer);
                foreach (GlobalScenario global in GlobalScenario.Instances) {
                    global.Compose(created, composer);
                }
                composer.Seal();
                instance = created;
                failure = default;
                return true;
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Compose.{template.Key}", $"[Scenarios] {template.Key} failed to compose: {ex}");
                failure = Reject(ScenarioRejectReason.InvalidComposition, $"{template.Key}: {ex.Message}");
                return false;
            }
        }

        private static bool PassesVeto(VaultScenario instance, ScenarioTransition transition) {
            if (!Guard(instance, "CanActivate", () => instance.InvokeCanActivate(transition), false)) {
                return false;
            }
            foreach (GlobalScenario global in GlobalScenario.Instances) {
                if (!Guard(instance, $"{global.GetType().Name}.CanActivate", () => global.CanActivate(instance, transition), false)) {
                    return false;
                }
            }
            return true;
        }

        private static void ActivateTree(VaultScenario instance, VaultScenario parent, bool sessionLayer, ScenarioTransition transition) {
            instance.Status = ScenarioStatus.Activating;
            if (parent != null) {
                parent.LinkChild(instance);
            }
            else if (sessionLayer) {
                _sessionLayer.Add(instance);
            }
            _active.Add(instance);

            foreach (ScenarioComponent component in instance.Components) {
                Guard(instance, $"{component.GetType().Name}.OnActivate", () => component.InvokeActivate(transition));
            }
            Guard(instance, "OnActivate", () => instance.InvokeActivate(transition));
            foreach (GlobalScenario global in GlobalScenario.Instances) {
                Guard(instance, $"{global.GetType().Name}.OnActivate", () => global.OnActivate(instance, transition));
            }

            if (instance.Status != ScenarioStatus.Activating) {
                return;
            }
            instance.Status = ScenarioStatus.Active;
            instance.StartPending = true;

            foreach (VaultScenario childTemplate in instance.ChildTemplates) {
                if (instance.Status != ScenarioStatus.Active) {
                    return;
                }
                ActivateDeclaredChild(instance, childTemplate, transition);
            }
        }

        private static void ActivateDeclaredChild(VaultScenario parent, VaultScenario childTemplate, ScenarioTransition transition) {
            for (VaultScenario ancestor = parent; ancestor != null; ancestor = ancestor.Parent) {
                if (ReferenceEquals(ancestor.Template, childTemplate)) {
                    ScenarioPlatform.Error($"Scenario.ChildCycle.{childTemplate.Key}", $"[Scenarios] {parent.Key} declares {childTemplate.Key} as a child, which is already one of its ancestors; skipped");
                    return;
                }
            }
            if (!TryCreate(childTemplate, out VaultScenario child, out _)) {
                return;
            }
            if (child.OwnsWorld) {
                ScenarioPlatform.Error($"Scenario.ChildWorld.{childTemplate.Key}", $"[Scenarios] {parent.Key} declares {childTemplate.Key} as a child, but it owns a world; skipped");
                return;
            }
            if (!PassesVeto(child, transition)) {
                return;
            }
            ActivateTree(child, parent, false, transition);
        }

        private static void DeactivateTree(VaultScenario instance, ScenarioTransition transition) {
            if (instance.Status is not (ScenarioStatus.Active or ScenarioStatus.Activating)) {
                return;
            }
            instance.Status = ScenarioStatus.Deactivating;

            IReadOnlyList<VaultScenario> children = instance.Children;
            for (int i = children.Count - 1; i >= 0; i--) {
                if (i < children.Count) {
                    DeactivateTree(children[i], transition);
                }
            }

            IReadOnlyList<GlobalScenario> globals = GlobalScenario.Instances;
            for (int i = globals.Count - 1; i >= 0; i--) {
                GlobalScenario global = globals[i];
                Guard(instance, $"{global.GetType().Name}.OnDeactivate", () => global.OnDeactivate(instance, transition));
            }
            Guard(instance, "OnDeactivate", () => instance.InvokeDeactivate(transition));
            IReadOnlyList<ScenarioComponent> components = instance.Components;
            for (int i = components.Count - 1; i >= 0; i--) {
                ScenarioComponent component = components[i];
                Guard(instance, $"{component.GetType().Name}.OnDeactivate", () => component.InvokeDeactivate(transition));
            }

            instance.UnlinkFromParent();
            _sessionLayer.Remove(instance);
            _active.Remove(instance);
            instance.StartPending = false;
            instance.Status = ScenarioStatus.Ended;
        }

        private static void RunStart(VaultScenario scenario) {
            foreach (ScenarioComponent component in scenario.Components) {
                Guard(scenario, $"{component.GetType().Name}.OnStart", component.InvokeStart);
            }
            Guard(scenario, "OnStart", scenario.InvokeStart);
            foreach (GlobalScenario global in GlobalScenario.Instances) {
                Guard(scenario, $"{global.GetType().Name}.OnStart", () => global.OnStart(scenario));
            }
        }

        //每帧路径，不经 Guard 以免逐组件分配委托与字符串
        private static void RunUpdate(VaultScenario scenario) {
            IReadOnlyList<ScenarioComponent> components = scenario.Components;
            for (int i = 0; i < components.Count; i++) {
                try {
                    components[i].InvokeUpdate();
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.{scenario.Key}.{components[i].GetType().Name}.Update", $"[Scenarios] {scenario.Key} {components[i].GetType().Name}.Update threw: {ex}");
                }
            }
            try {
                scenario.InvokeUpdate();
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.{scenario.Key}.Update", $"[Scenarios] {scenario.Key} Update threw: {ex}");
            }
        }

        //单个钩子出错只记录（同类错误按冷却去重），不打断同一次转场里的其他参与者
        private static void Guard(VaultScenario scenario, string hook, Action action) {
            try {
                action();
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.{scenario.Key}.{hook}", $"[Scenarios] {scenario.Key} {hook} threw: {ex}");
            }
        }

        private static bool Guard(VaultScenario scenario, string hook, Func<bool> predicate, bool fallback) {
            try {
                return predicate();
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.{scenario.Key}.{hook}", $"[Scenarios] {scenario.Key} {hook} threw: {ex}");
                return fallback;
            }
        }
    }
}
