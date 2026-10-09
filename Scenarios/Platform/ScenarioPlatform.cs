using System;

namespace InnoVault.Scenarios
{
    internal enum ScenarioNetRole
    {
        SinglePlayer,
        Client,
        Server,
    }

    //场景核心与宿主之间的全部接缝：网络身份与日志；阶段 1 起加入世界切换后端
    //核心只经这里接触宿主，游戏里由 Scenarios/Tml 的分部实现在静态构造时装好，未装配的项都有无害的缺省行为
    internal static partial class ScenarioPlatform
    {
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
}
