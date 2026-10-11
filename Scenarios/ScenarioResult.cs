namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景请求被拒绝的原因
    /// </summary>
    public enum ScenarioRejectReason
    {
        /// <summary>没有拒绝</summary>
        None,
        /// <summary>多人模式下场景框架尚未开放，可以向玩家展示 <see cref="ScenarioText.MultiplayerUnsupported"/></summary>
        MultiplayerUnsupported,
        /// <summary>当前不在会话中（没有进入世界）</summary>
        NoSession,
        /// <summary>目标场景没有注册（类型未被加载，或传入了空值）</summary>
        NotRegistered,
        /// <summary>正在换舞台（含幕布合上与拉开的过程），暂时不能处理新请求</summary>
        Busy,
        /// <summary>目标场景的 Compose 声明不合法，或带舞台坐标的场景要挂到会话层，详见 <see cref="ScenarioResult.Detail"/></summary>
        InvalidComposition,
        /// <summary>目标场景拥有舞台，只能经 <see cref="ScenarioDirector.Travel(VaultScenario, string, System.Action{VaultScenario})"/> 进入</summary>
        RequiresTravel,
        /// <summary>换舞台的目标场景不拥有舞台</summary>
        RequiresStage,
        /// <summary>目标场景的舞台声明不合法（例如尺寸越界、锚点在舞台外），详见 <see cref="ScenarioResult.Detail"/></summary>
        InvalidStage,
        /// <summary>已经身处目标舞台场景中</summary>
        AlreadyThere,
        /// <summary>指定的父场景不是一个处于激活状态的实例</summary>
        InvalidParent,
        /// <summary>要停用的实例没有处于激活状态</summary>
        NotActive,
        /// <summary>拥有舞台的场景不能直接停用，应当经换舞台离开</summary>
        CannotDeactivateStage,
        /// <summary>被场景自身或某个 <see cref="GlobalScenario"/> 否决</summary>
        Vetoed,
        /// <summary>当前端没有安装换舞台的平台后端</summary>
        StageBackendUnavailable,
        /// <summary>平台后端此刻不能开始换舞台（例如玩家处于死亡状态），详见 <see cref="ScenarioResult.Detail"/></summary>
        NotReady,
    }

    /// <summary>
    /// 一次场景请求的结果
    /// </summary>
    public readonly struct ScenarioResult
    {
        private ScenarioResult(ScenarioRejectReason reason, VaultScenario instance, string detail) {
            Reason = reason;
            Instance = instance;
            Detail = detail;
        }

        /// <summary>拒绝原因；请求被接受时为 <see cref="ScenarioRejectReason.None"/></summary>
        public ScenarioRejectReason Reason { get; }

        /// <summary>请求涉及的场景实例：激活时是新实例，停用时是被停用的实例，被拒绝时为空</summary>
        public VaultScenario Instance { get; }

        /// <summary>给开发者看的补充说明，用于日志，不面向玩家</summary>
        public string Detail { get; }

        /// <summary>请求是否被接受</summary>
        public bool Accepted => Reason == ScenarioRejectReason.None;

        internal static ScenarioResult Accept(VaultScenario instance) => new(ScenarioRejectReason.None, instance, null);

        internal static ScenarioResult Reject(ScenarioRejectReason reason, string detail = null) => new(reason, null, detail);

        /// <inheritdoc/>
        public override string ToString() {
            if (Accepted) {
                return $"Accepted: {Instance?.Key}";
            }
            return string.IsNullOrEmpty(Detail) ? $"Rejected: {Reason}" : $"Rejected: {Reason} ({Detail})";
        }
    }
}
