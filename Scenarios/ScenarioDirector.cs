using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景框架的对外入口：发起激活、停用与换舞台，查询当前处于激活中的场景<br/>
    /// 会话（从菜单进入世界到保存退出）之外没有任何场景实例<br/>
    /// 任意时刻舞台层恰好一个拥有舞台的实例（<see cref="Stage"/>），不拥有舞台的实例挂在它下面，
    /// 或者挂在会话层（<see cref="VaultScenario.SurvivesTravel"/>）<br/>
    /// 多人模式下场景树由服务器裁决：服务器上的调用直接执行并同步给所有客户端；客户端上的调用变成发给服务器的请求，
    /// 结果是 <see cref="ScenarioResult.Pending"/>，被拒绝时触发 <see cref="RequestRejected"/>。换舞台会带走服务器上的所有玩家：
    /// 服务器换掉自己的世界，客户端在原连接上重新接收新舞台
    /// </summary>
    public static partial class ScenarioDirector
    {
        private static readonly List<VaultScenario> _active = [];
        private static readonly List<VaultScenario> _sessionLayer = [];
        private static VaultScenario[] _tickBuffer = [];
        private static bool _multiplayerNoticeLogged;
        //会话结束期间拒绝新请求，否则停用钩子里发起的激活会留下收不到 OnDeactivate 的实例
        private static bool _endingSession;
        private static bool _sessionActive;
        //进行中的换舞台；切换期间 To 可能被改写为回退的根场景
        private static ScenarioTransition _travel;
        //会话开始后按恢复策略要回到的舞台场景，在第一次更新时发起切换
        private static VaultScenario _pendingResume;
        //离场与入场阶段已经过的帧数
        private static int _phaseFrames;
        private static Type _departureClip;
        private static int _departureTimeout;
        private static bool _waitingForClip;
        private static ScenarioCurtain _curtain;
        private static float _curtainCoverage;

        /// <summary>当前处于激活中的根场景实例：身处主世界时有值，离开主世界期间与会话之外为空</summary>
        public static VaultScenario Root { get; private set; }

        /// <summary>
        /// 当前舞台层的场景实例（身处主世界时就是 <see cref="Root"/>）；会话之外与存盘、读档期间为空。
        /// 多人客户端刚进入舞台、还没收到服务器的场景树时也是空
        /// </summary>
        public static VaultScenario Stage { get; private set; }

        /// <summary>所有处于激活中的场景实例，按激活顺序排列</summary>
        public static IReadOnlyList<VaultScenario> Active => _active;

        /// <summary>是否处于会话中（从菜单进入世界到保存退出，换舞台途中也算在会话中）</summary>
        public static bool InSession => _sessionActive;

        /// <summary>是否正在换舞台（从幕布开始合上，到抵达后幕布完全拉开）</summary>
        public static bool IsTraveling { get; private set; }

        /// <summary>正在进行的换舞台，没有在切换时为空。切换中途目标场景读不进来时，它会被改为一次回到主世界的切换</summary>
        public static ScenarioTransition CurrentTravel => _travel;

        /// <summary>
        /// 换舞台进行到哪一步。多人模式下换好世界之后、所有玩家到齐之前停在 <see cref="ScenarioTravelPhase.Loading"/>，幕布保持合上
        /// </summary>
        public static ScenarioTravelPhase TravelPhase { get; internal set; }

        /// <summary>当前是否身处主世界</summary>
        public static bool IsHome => Stage is RootScenario;

        /// <summary>
        /// 此刻能否发起场景请求，<see cref="ScenarioRejectReason.None"/> 表示可以。物品、UI 等入口可以先用它判断是否可用
        /// </summary>
        public static ScenarioRejectReason Availability => CheckAvailability();

        //幕布遮盖程度（0 完全拉开，1 完全合上），由宿主的幕布绘制读取
        internal static float CurtainCoverage => _curtainCoverage;

        //这次换舞台用的幕布组件，为空时用缺省值
        internal static ScenarioCurtain ActiveCurtain => _curtain;

        //后端正在存盘、生成或读档（单人下游戏处于菜单状态；多人下还包括换好世界之后等所有玩家到齐的那一段）
        internal static bool InBackendTravel => IsTraveling && TravelPhase is ScenarioTravelPhase.Saving
            or ScenarioTravelPhase.Generating or ScenarioTravelPhase.Loading;

        //换舞台期间锁住本地玩家的输入：离场，以及多人下盖着幕布等其他玩家的那一段
        internal static bool InputLocked => IsTraveling && TravelPhase != ScenarioTravelPhase.Arriving;

        //幕布完全合着、本地玩家看不见也动不了的时段。单人下合上的同一帧就交给了后端；多人下还要等其他玩家合上幕布、等所有人到齐
        internal static bool Blindfolded => IsTraveling && TravelPhase != ScenarioTravelPhase.Arriving && _curtainCoverage >= 1f;

        private static int CloseFrames => Math.Max(_curtain?.CloseFrames ?? ScenarioCurtain.DefaultFrames, 0);

        private static int OpenFrames => Math.Max(_curtain?.OpenFrames ?? ScenarioCurtain.DefaultFrames, 0);

        /// <summary>当前舞台层的场景是否为 <typeparamref name="T"/></summary>
        public static bool IsIn<T>() where T : VaultScenario => Stage is T;

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

        /// <summary>
        /// 在当前舞台上所有处于激活中的场景里按名字找锚点，后激活的场景优先（叠加场景的锚点盖过舞台场景的同名锚点）
        /// </summary>
        /// <param name="name">锚点名</param>
        /// <param name="tile">锚点的物块坐标</param>
        public static bool TryFindAnchor(string name, out Point tile) {
            for (int i = _active.Count - 1; i >= 0; i--) {
                VaultScenario scenario = _active[i];
                if (scenario.Status != ScenarioStatus.Active) {
                    continue;
                }
                foreach (ScenarioComponent component in scenario.Components) {
                    if (component is ScenarioAnchors anchors && anchors.TryGet(name, out tile)) {
                        return true;
                    }
                }
            }
            tile = default;
            return false;
        }

        /// <summary>激活一个不拥有舞台的场景</summary>
        /// <typeparam name="T">场景类型</typeparam>
        /// <param name="setup">实例创建后、Compose 之前调用，用来给这一次激活传参数（例如区域位置）</param>
        public static ScenarioResult Activate<T>(Action<T> setup) where T : VaultScenario
            => Activate(VaultScenario.FindTemplate(typeof(T)), null, Wrap(setup));

        /// <summary>激活一个不拥有舞台的场景</summary>
        /// <typeparam name="T">场景类型</typeparam>
        /// <param name="parent">父场景实例；为空时挂在当前舞台场景下，<see cref="VaultScenario.SurvivesTravel"/> 为真时挂在会话层</param>
        /// <param name="setup">实例创建后、Compose 之前调用，用来给这一次激活传参数</param>
        public static ScenarioResult Activate<T>(VaultScenario parent = null, Action<T> setup = null) where T : VaultScenario
            => Activate(VaultScenario.FindTemplate(typeof(T)), parent, Wrap(setup));

        /// <summary>
        /// 激活一个不拥有舞台的场景<br/>
        /// 多人模式下 <paramref name="setup"/> 只在发起的这一端执行，其他端拿到的是实例上标了 <see cref="SyncVarAttribute"/> 的成员：
        /// 需要传给其他端的参数必须写进这样的成员里
        /// </summary>
        /// <param name="template">场景模板（传入实例时取它的模板）</param>
        /// <param name="parent">父场景实例；为空时挂在当前舞台场景下，<see cref="VaultScenario.SurvivesTravel"/> 为真时挂在会话层</param>
        /// <param name="setup">实例创建后、Compose 之前调用，用来给这一次激活传参数</param>
        public static ScenarioResult Activate(VaultScenario template, VaultScenario parent = null, Action<VaultScenario> setup = null) {
            template = template?.Template;
            if (template == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "template is null or not registered");
            }
            ScenarioRejectReason reason = Review();
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (parent != null && (parent.IsTemplate || parent.Status != ScenarioStatus.Active)) {
                return Reject(ScenarioRejectReason.InvalidParent, $"{parent.Key} is not an active instance");
            }
            if (ScenarioPlatform.IsClient) {
                return RequestActivate(template, parent, setup);
            }
            return ActivateCore(template, parent, setup, null);
        }

        /// <summary>停用一个不拥有舞台的场景实例，它的子场景先行停用</summary>
        /// <param name="instance">要停用的实例</param>
        public static ScenarioResult Deactivate(VaultScenario instance) {
            if (instance == null || instance.IsTemplate) {
                return Reject(ScenarioRejectReason.NotActive, "not an instance");
            }
            ScenarioRejectReason reason = Review();
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (instance.Status != ScenarioStatus.Active) {
                return Reject(ScenarioRejectReason.NotActive, $"{instance.Key} is {instance.Status}");
            }
            if (instance.OwnsStage) {
                return Reject(ScenarioRejectReason.CannotDeactivateStage, instance.Key);
            }
            if (ScenarioPlatform.IsClient) {
                return RequestDeactivate(instance);
            }
            DeactivateTree(instance, new ScenarioTransition(ScenarioTransitionKind.Deactivation, instance, null));
            AnnounceDeactivated(instance);
            return ScenarioResult.Accept(instance);
        }

        /// <summary>换到一个拥有舞台的场景</summary>
        /// <typeparam name="T">场景类型</typeparam>
        /// <param name="anchor">落点锚点名，为空时落在 <see cref="ScenarioAnchors.Entry"/>，没有时落在舞台出生点</param>
        /// <param name="setup">实例创建后、Compose 之前调用，用来给这一次进入传参数</param>
        public static ScenarioResult Travel<T>(string anchor = null, Action<T> setup = null) where T : VaultScenario
            => Travel(VaultScenario.FindTemplate(typeof(T)), anchor, Wrap(setup));

        /// <summary>
        /// 换到一个拥有舞台的场景。请求被接受后先合上幕布（并播放离开舞台的离场演出），然后存盘、生成或读取目标舞台，
        /// 抵达后幕布拉开；整个过程 <see cref="IsTraveling"/> 为真<br/>
        /// 多人模式下换的是服务器的舞台，所有玩家一起过去；服务器等各客户端合上幕布再换世界，等它们到齐再一起拉开（都有超时）。
        /// <paramref name="setup"/> 的限制与 <see cref="Activate(VaultScenario, VaultScenario, Action{VaultScenario})"/> 相同
        /// </summary>
        /// <param name="template">场景模板（传入实例时取它的模板）</param>
        /// <param name="anchor">落点锚点名，为空时落在 <see cref="ScenarioAnchors.Entry"/>，没有时落在舞台出生点</param>
        /// <param name="setup">实例创建后、Compose 之前调用，用来给这一次进入传参数</param>
        public static ScenarioResult Travel(VaultScenario template, string anchor = null, Action<VaultScenario> setup = null) {
            template = template?.Template;
            if (template == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "template is null or not registered");
            }
            ScenarioRejectReason reason = Review();
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (Stage != null && ReferenceEquals(Stage.Template, template)) {
                return Reject(ScenarioRejectReason.AlreadyThere, template.Key);
            }
            if (ScenarioPlatform.IsClient) {
                return RequestTravel(template, anchor, setup);
            }
            return TravelCore(template, anchor, setup, null);
        }

        /// <summary>回到主世界，即换舞台到根场景</summary>
        /// <param name="anchor">主世界里的落点锚点名（经 <see cref="GlobalScenario.Compose"/> 挂到根场景上的锚点）；为空时落回离开前的位置</param>
        public static ScenarioResult ReturnHome(string anchor = null) => Travel(VaultScenario.FindTemplate(typeof(RootScenario)), anchor);

        //gate 在实例组合完成、结构校验通过之后调用，返回非空表示拒绝；服务器处理客户端请求时用它接入裁决钩子
        private static ScenarioResult ActivateCore(VaultScenario template, VaultScenario parent, Action<VaultScenario> setup, Func<VaultScenario, ScenarioResult?> gate) {
            if (!TryCreate(template, setup, out VaultScenario instance, out ScenarioResult failure)) {
                return failure;
            }
            if (instance.OwnsStage) {
                return Reject(ScenarioRejectReason.RequiresTravel, template.Key);
            }
            bool sessionLayer = parent == null && instance.SurvivesTravel;
            if ((sessionLayer || IsOnSessionLayer(parent)) && instance.HasStageBoundComponents) {
                return Reject(ScenarioRejectReason.InvalidComposition,
                    $"{template.Key} carries stage-bound components (area, anchors, restore) and cannot live on the session layer");
            }
            if (gate?.Invoke(instance) is ScenarioResult denied) {
                return denied;
            }
            parent ??= sessionLayer ? null : Stage;
            ScenarioTransition transition = new(ScenarioTransitionKind.Activation, null, instance);
            if (!PassesVeto(instance, transition)) {
                return Reject(ScenarioRejectReason.Vetoed, template.Key);
            }
            ActivateTree(instance, parent, sessionLayer, transition);
            return ScenarioResult.Accept(instance);
        }

        private static ScenarioResult TravelCore(VaultScenario template, string anchor, Action<VaultScenario> setup, Func<VaultScenario, ScenarioResult?> gate) {
            if (!TryCreate(template, setup, out VaultScenario instance, out ScenarioResult failure)) {
                return failure;
            }
            if (!instance.OwnsStage) {
                return Reject(ScenarioRejectReason.RequiresStage, template.Key);
            }
            string invalid = instance.Stage.ValidateStage();
            if (invalid != null) {
                return Reject(ScenarioRejectReason.InvalidStage, $"{template.Key}: {invalid}");
            }
            IScenarioStageBackend backend = ScenarioPlatform.StageBackend;
            if (backend == null) {
                return Reject(ScenarioRejectReason.StageBackendUnavailable, "no stage backend installed on this endpoint");
            }
            if (gate?.Invoke(instance) is ScenarioResult denied) {
                return denied;
            }
            ScenarioTransition transition = new(ScenarioTransitionKind.Travel, Stage, instance, anchor);
            string notReady = backend.CheckReady(transition);
            if (notReady != null) {
                return Reject(ScenarioRejectReason.NotReady, notReady);
            }
            if (!PassesVeto(instance, transition)) {
                return Reject(ScenarioRejectReason.Vetoed, template.Key);
            }

            IsTraveling = true;
            _travel = transition;
            _curtain = ResolveCurtain(transition);
            _phaseFrames = 0;
            TravelPhase = ScenarioTravelPhase.Departing;
            ScenarioCinematics cinematics = transition.From?.GetComponent<ScenarioCinematics>();
            _departureClip = cinematics?.DepartureClip;
            _departureTimeout = cinematics?.DepartureTimeout ?? ScenarioCinematics.DefaultDepartureTimeout;
            if (ScenarioPlatform.IsServer) {
                //服务器不播演出也没有幕布：通知所有客户端离场，等它们合上幕布
                BeginServerDeparture(transition);
                return ScenarioResult.Accept(instance);
            }
            _waitingForClip = _departureClip != null && CutsceneDirector.Play(_departureClip);
            if (!_waitingForClip && CloseFrames == 0) {
                _curtainCoverage = 1f;
                if (!BeginBackendTravel()) {
                    return Reject(ScenarioRejectReason.NotReady, "the stage backend failed to start; see the log");
                }
            }
            return ScenarioResult.Accept(instance);
        }

        //本地玩家出生进入舞台时由宿主在主线程调用（服务器上则是读完一个世界时）：换舞台途中交给后端放置玩家并开始拉开幕布，否则是从菜单进入，开始会话
        internal static void NotifyEnteredStage() {
            //上一个会话没有正常结束、而宿主的网络身份已经变了（客户端掉线后直接进了别的世界）：先结束它，再按现在的身份开始
            if (InSession && ScenarioPlatform.SessionRole is ScenarioNetRole latched && latched != ScenarioPlatform.HostNetRole) {
                ScenarioPlatform.Error("Scenario.StaleSession", "[Scenarios] Entered a world while a session from another network role was still open; ending it now");
                IsTraveling = false;
                _travel = null;
                TravelPhase = ScenarioTravelPhase.None;
                EndSession(stageAlreadySaved: true);
            }
            if (ScenarioPlatform.IsClient) {
                ClientEnteredStage();
                return;
            }
            if (IsTraveling) {
                if (ScenarioPlatform.IsServer) {
                    //服务器换好了世界：幕布继续合着，等在途的客户端到场
                    ServerStageReady();
                    return;
                }
                if (_travel != null) {
                    ScenarioPlatform.StageBackend?.OnPlayerEntered(_travel);
                }
                TravelPhase = ScenarioTravelPhase.Arriving;
                _phaseFrames = 0;
                _curtainCoverage = 1f;
                if (OpenFrames == 0) {
                    FinishTravel();
                }
                return;
            }
            StartSession();
        }

        //舞台读档完成（各模组 LoadWorldData 之后、玩家出生之前）时由宿主调用：单人运行在读档线程上，服务器运行在主线程上，客户端不会走到这里
        internal static void NotifyStageLoaded() {
            if (!IsTraveling || _travel?.To == null) {
                return;
            }
            VaultScenario arriving = _travel.To;
            Stage = arriving;
            Root = arriving as RootScenario;
            ActivateTree(arriving, null, false, _travel);
        }

        //目标舞台无法生成或读取时由后端调用：把这次切换的目标改为一个新的根场景实例，数据包原样保留
        internal static ScenarioTransition RedirectTravelHome(ScenarioTransition failed) {
            if (Stage != null) {
                DeactivateTree(Stage, failed);
                Stage = null;
                Root = null;
            }
            VaultScenario template = VaultScenario.FindTemplate(typeof(RootScenario));
            if (template == null || !TryCreate(template, null, out VaultScenario root, out _)) {
                return null;
            }
            ScenarioTransition redirected = new(ScenarioTransitionKind.Travel, failed.From, root);
            foreach (var pair in failed.Payload) {
                redirected.Payload[pair.Key] = pair.Value;
            }
            _travel = redirected;
            RedirectServerTravel(redirected);
            return redirected;
        }

        //连根世界都进不去时由后端调用：结束这次切换与整个会话
        internal static void AbortTravel() {
            IsTraveling = false;
            _travel = null;
            TravelPhase = ScenarioTravelPhase.None;
            EndSession();
        }

        //区域进出改变了参与合成的组件时由宿主调用
        internal static void RefreshRuntime() => ScenarioRuntime.Rebuild(Stage, _active);

        //单人从菜单进入世界、服务器开服读完世界时开始会话：根场景在这一端激活。客户端的会话由 ClientEnteredStage 开始，场景树来自服务器的快照
        private static void StartSession() {
            if (NetOffline) {
                NoteMultiplayer();
                return;
            }
            if (InSession) {
                ScenarioPlatform.Error("Scenario.StaleSession", "[Scenarios] Entered a world while the previous session was still open; ending it now");
                EndSession();
            }
            VaultScenario template = VaultScenario.FindTemplate(typeof(RootScenario));
            if (template == null || !TryCreate(template, null, out VaultScenario root, out _)) {
                ScenarioPlatform.Error("Scenario.StartSession", "[Scenarios] Root scenario could not be created; session not started");
                return;
            }
            _sessionActive = true;
            ScenarioPlatform.SessionRole = ScenarioPlatform.HostNetRole;
            _curtainCoverage = 0f;
            IScenarioStageBackend backend = ScenarioPlatform.StageBackend;
            backend?.OnSessionStarted();
            Root = root;
            Stage = root;
            ActivateTree(root, null, false, new ScenarioTransition(ScenarioTransitionKind.SessionStart, null, root));
            _pendingResume = ResolveResume(backend?.ConsumeQuitLocation());
        }

        //上次在某个舞台场景里保存退出时，询问它的恢复策略：全局钩子优先，其次看场景是否挂了 ResumeOnRelog
        private static VaultScenario ResolveResume(string key) {
            VaultScenario template = VaultScenario.FindTemplate(key);
            if (template == null || template is RootScenario || !TryCreate(template, null, out VaultScenario probe, out _)) {
                return null;
            }
            bool? decision = null;
            foreach (GlobalScenario global in GlobalScenario.Instances) {
                bool? vote = Guard<bool?>(probe, $"{global.GetType().Name}.ResumeAfterRelog", () => global.ResumeAfterRelog(probe), null);
                if (vote.HasValue) {
                    decision = vote;
                }
            }
            bool resume = decision ?? probe.GetComponent<Presets.ResumeOnRelog>() != null;
            return resume ? template : null;
        }

        //保存退出时由宿主调用：先停舞台子树，再停会话层；重复调用无害，后端存盘、读档途中忽略。
        //stageAlreadySaved 为真表示舞台已经存过盘（绕过暂停菜单的保存退出），停用钩子里对世界的改动不会进存档
        internal static void EndSession(bool stageAlreadySaved = false) {
            if (!InSession || _endingSession) {
                return;
            }
            if (IsTraveling) {
                if (TravelPhase == ScenarioTravelPhase.Departing) {
                    CancelTravel("the session ended");
                }
                else if (TravelPhase == ScenarioTravelPhase.Arriving) {
                    FinishTravel();
                }
                else {
                    return;
                }
            }
            _endingSession = true;
            try {
                ScenarioPlatform.StageBackend?.OnSessionEnding(Stage);
                ScenarioTransition transition = new(ScenarioTransitionKind.SessionEnd, Stage, null) {
                    StageAlreadySaved = stageAlreadySaved,
                };
                if (Stage != null) {
                    DeactivateTree(Stage, transition);
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
            if (IsTraveling) {
                AdvanceTravel();
                if (InBackendTravel) {
                    return;
                }
            }
            else if (_curtainCoverage > 0f) {
                _curtainCoverage = Math.Max(0f, _curtainCoverage - 1f / ScenarioCurtain.DefaultFrames);
            }
            if (!IsTraveling && _pendingResume != null) {
                VaultScenario resume = _pendingResume;
                _pendingResume = null;
                ScenarioResult result = Travel(resume);
                if (!result.Accepted) {
                    ScenarioPlatform.Info($"[Scenarios] Resuming {resume.Key} after relog was rejected: {result}");
                }
                if (InBackendTravel) {
                    return;
                }
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
            FlushNetUpdates();
        }

        //模组卸载时调用：不触发任何钩子，直接丢弃全部状态
        internal static void Reset() {
            ClearState();
            _tickBuffer = [];
            _multiplayerNoticeLogged = false;
            _endingSession = false;
            RequestRejected = null;
        }

        private static void ClearState() {
            _active.Clear();
            _sessionLayer.Clear();
            Root = null;
            Stage = null;
            IsTraveling = false;
            _travel = null;
            _pendingResume = null;
            _sessionActive = false;
            _phaseFrames = 0;
            _departureClip = null;
            _waitingForClip = false;
            _curtain = null;
            _curtainCoverage = 0f;
            TravelPhase = ScenarioTravelPhase.None;
            ClearNetState();
            ScenarioRuntime.Clear();
            ScenarioPlatform.SessionRole = null;
        }

        private static void AdvanceTravel() {
            switch (ScenarioPlatform.NetRole) {
                case ScenarioNetRole.Server:
                    AdvanceServerTravel();
                    break;
                case ScenarioNetRole.Client:
                    AdvanceClientTravel();
                    break;
                default:
                    if (TravelPhase == ScenarioTravelPhase.Departing) {
                        AdvanceDeparture();
                    }
                    else if (TravelPhase == ScenarioTravelPhase.Arriving) {
                        AdvanceArrival();
                    }
                    break;
            }
        }

        //（单人）离场：先等离场演出播完（或超时），再合上幕布，合上后交给后端存盘。每帧都向后端确认还能继续，不能时取消
        private static void AdvanceDeparture() {
            IScenarioStageBackend backend = ScenarioPlatform.StageBackend;
            string notReady = backend == null ? "no stage backend installed on this endpoint" : backend.CheckReady(_travel);
            if (notReady != null) {
                CancelTravel(notReady);
                return;
            }
            if (_waitingForClip) {
                if (CutsceneDirector.CurrentClip?.GetType() == _departureClip && _phaseFrames < _departureTimeout) {
                    _phaseFrames++;
                    return;
                }
                _waitingForClip = false;
                _phaseFrames = 0;
            }
            int close = CloseFrames;
            _phaseFrames++;
            _curtainCoverage = close == 0 ? 1f : Math.Min(1f, _phaseFrames / (float)close);
            if (_phaseFrames >= close) {
                BeginBackendTravel();
            }
        }

        private static void AdvanceArrival() {
            int open = OpenFrames;
            _phaseFrames++;
            _curtainCoverage = open == 0 ? 0f : Math.Max(0f, 1f - _phaseFrames / (float)open);
            if (_phaseFrames >= open) {
                FinishTravel();
            }
        }

        //停用旧舞台子树并交给后端；后端启动失败时结束会话（旧舞台已经停用，没有可以回去的状态）
        private static bool BeginBackendTravel() {
            ScenarioTransition transition = _travel;
            VaultScenario departing = Stage;
            Stage = null;
            Root = null;
            _waitingForClip = false;
            if (departing != null) {
                DeactivateTree(departing, transition);
            }
            //先切到存盘阶段再交给后端：后端开头派发的 PreSaveAndQuit 会走到 EndSession，那时必须认得出这是后端在换舞台，而不是离场中的保存退出
            TravelPhase = ScenarioTravelPhase.Saving;
            //服务器：先让客户端也停用各自的旧舞台子树、卸载本地世界，再换世界
            CommitServerTravel(transition);
            try {
                ScenarioPlatform.StageBackend.Begin(transition);
                return true;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Travel.Begin", $"[Scenarios] Stage backend failed to start travelling to {transition?.To?.Key}: {ex}");
                AbortTravel();
                return false;
            }
        }

        //离场期间取消：目标实例从未激活，直接丢弃；幕布在之后的帧里拉开
        private static void CancelTravel(string reason) {
            ScenarioPlatform.Info($"[Scenarios] Travel to {_travel?.To?.Key} was cancelled before leaving: {reason}");
            IsTraveling = false;
            _travel = null;
            TravelPhase = ScenarioTravelPhase.None;
            _waitingForClip = false;
            _phaseFrames = 0;
        }

        private static void FinishTravel() {
            IsTraveling = false;
            _travel = null;
            TravelPhase = ScenarioTravelPhase.None;
            _phaseFrames = 0;
            _curtainCoverage = 0f;
        }

        //进入场景用目标场景的幕布，返回主世界用离开的那个场景的
        private static ScenarioCurtain ResolveCurtain(ScenarioTransition transition) {
            VaultScenario owner = transition.To is RootScenario ? transition.From : transition.To;
            return owner?.GetComponent<ScenarioCurtain>();
        }

        private static bool IsOnSessionLayer(VaultScenario scenario) {
            if (scenario == null) {
                return false;
            }
            while (scenario.Parent != null) {
                scenario = scenario.Parent;
            }
            return _sessionLayer.Contains(scenario);
        }

        private static Action<VaultScenario> Wrap<T>(Action<T> setup) where T : VaultScenario
            => setup == null ? null : scenario => setup((T)scenario);

        private static ScenarioRejectReason Review() {
            ScenarioRejectReason reason = CheckAvailability();
            if (reason == ScenarioRejectReason.MultiplayerUnsupported) {
                NoteMultiplayer();
            }
            return reason;
        }

        private static ScenarioRejectReason CheckAvailability() {
            if (NetOffline) {
                return ScenarioRejectReason.MultiplayerUnsupported;
            }
            if (!InSession) {
                return ScenarioRejectReason.NoSession;
            }
            if (IsTraveling || _endingSession) {
                return ScenarioRejectReason.Busy;
            }
            return NetBusy() ? ScenarioRejectReason.Busy : ScenarioRejectReason.None;
        }

        private static void NoteMultiplayer() {
            if (_multiplayerNoticeLogged) {
                return;
            }
            _multiplayerNoticeLogged = true;
            ScenarioPlatform.Info("[Scenarios] No scenario network host is installed on this endpoint; the scenario director stays offline in multiplayer");
        }

        private static ScenarioResult Reject(ScenarioRejectReason reason, string detail = null) => ScenarioResult.Reject(reason, detail);

        //composed 在 Compose 结束、组件已经挂好之后调用：按网络状态新建实例时在这里套用附加数据与组件状态
        private static bool TryCreate(VaultScenario template, Action<VaultScenario> setup, out VaultScenario instance, out ScenarioResult failure,
            Action<VaultScenario> composed = null) {
            instance = null;
            try {
                VaultScenario created = template.CreateInstance();
                setup?.Invoke(created);
                ScenarioComposer composer = new(created);
                created.InvokeCompose(composer);
                foreach (GlobalScenario global in GlobalScenario.Instances) {
                    global.Compose(created, composer);
                }
                composer.Seal();
                composed?.Invoke(created);
                string invalid = created.ValidateComposition();
                if (invalid != null) {
                    failure = Reject(ScenarioRejectReason.InvalidComposition, $"{template.Key}: {invalid}");
                    return false;
                }
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
            RegisterNetId(instance);

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
            ScenarioRuntime.Rebuild(Stage, _active);
            AnnounceActivated(instance);

            //客户端的镜像不自行激活声明的子场景：子场景要不要激活由服务器上的否决钩子决定，激活了的由服务器逐个下发
            if (!ScenarioPlatform.IsAuthority) {
                return;
            }
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
            if (!TryCreate(childTemplate, null, out VaultScenario child, out ScenarioResult failure)) {
                ScenarioPlatform.Error($"Scenario.ChildCompose.{childTemplate.Key}", $"[Scenarios] {parent.Key} declares {childTemplate.Key} as a child, but it is invalid: {failure}");
                return;
            }
            if (child.OwnsStage) {
                ScenarioPlatform.Error($"Scenario.ChildStage.{childTemplate.Key}", $"[Scenarios] {parent.Key} declares {childTemplate.Key} as a child, but it owns a stage; skipped");
                return;
            }
            if (IsOnSessionLayer(parent) && child.HasStageBoundComponents) {
                ScenarioPlatform.Error($"Scenario.ChildSession.{childTemplate.Key}", $"[Scenarios] {parent.Key} lives on the session layer, so its child {childTemplate.Key} cannot carry stage-bound components; skipped");
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
            if (ScenarioPlatform.ScenarioEnded != null) {
                Guard(instance, "ScenarioEnded", () => ScenarioPlatform.ScenarioEnded(instance));
            }

            instance.UnlinkFromParent();
            _sessionLayer.Remove(instance);
            _active.Remove(instance);
            UnregisterNetId(instance);
            instance.StartPending = false;
            instance.NetUpdate = false;
            instance.Area?.ClearEngaged();
            instance.Status = ScenarioStatus.Ended;
            ScenarioRuntime.Rebuild(Stage, _active);
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

        private static T Guard<T>(VaultScenario scenario, string hook, Func<T> func, T fallback) {
            try {
                return func();
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.{scenario.Key}.{hook}", $"[Scenarios] {scenario.Key} {hook} threw: {ex}");
                return fallback;
            }
        }
    }
}
