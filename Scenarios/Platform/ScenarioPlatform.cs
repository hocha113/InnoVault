using System;

namespace InnoVault.Scenarios
{
    internal enum ScenarioNetRole
    {
        SinglePlayer,
        Client,
        Server,
    }

    //场景核心与宿主之间的全部接缝：网络身份、日志、换舞台后端与区域还原后端
    //核心只经这里接触宿主，游戏里由 Scenarios/Tml 的分部实现在静态构造时装好，未装配的项都有无害的缺省行为
    internal static partial class ScenarioPlatform
    {
        //缺省为空，换舞台请求返回 StageBackendUnavailable
        internal static IScenarioStageBackend StageBackend { get; set; }

        //缺省为空，区域还原组件激活时只记录一条错误
        internal static IScenarioRestoreBackend RestoreBackend { get; set; }

        //场景实例停用完毕（所有停用钩子执行之后）时调用，宿主在这里清理归属实体；缺省什么都不做
        internal static Action<VaultScenario> ScenarioEnded { get; set; }

        //揭开地图组件范围内的地图，主线程；缺省什么都不做
        internal static Action<ScenarioMap> RevealMap { get; set; }

        static ScenarioPlatform() {
            InstallHost();
        }

        static partial void InstallHost();

        //缺省视为单人
        internal static Func<ScenarioNetRole> NetRoleSource { get; set; }

        //(去重键, 消息)，缺省丢弃
        internal static Action<string, string> ErrorSink { get; set; }

        //缺省丢弃
        internal static Action<string> InfoSink { get; set; }

        internal static ScenarioNetRole NetRole => NetRoleSource?.Invoke() ?? ScenarioNetRole.SinglePlayer;

        internal static void Error(string key, string message) => ErrorSink?.Invoke(key, message);

        internal static void Info(string message) => InfoSink?.Invoke(message);
    }

    //区域还原的平台后端：激活时记录、停用时写回、读入舞台时补还原
    internal interface IScenarioRestoreBackend
    {
        void Capture(ScenarioRestore restore, ScenarioTransition transition);

        void Restore(ScenarioRestore restore, ScenarioTransition transition);
    }
}
