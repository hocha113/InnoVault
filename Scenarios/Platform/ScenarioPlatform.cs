using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace InnoVault.Scenarios
{
    internal enum ScenarioNetRole
    {
        SinglePlayer,
        Client,
        Server,
    }

    //场景核心与宿主之间的全部接缝：网络身份与网络收发、日志、换舞台后端与区域还原后端
    //核心只经这里接触宿主，游戏里由 Scenarios/Tml 的分部实现在静态构造时装好，未装配的项都有无害的缺省行为
    internal static partial class ScenarioPlatform
    {
        //缺省为空，换舞台请求返回 StageBackendUnavailable
        internal static IScenarioStageBackend StageBackend { get; set; }

        //缺省为空，区域还原组件激活时只记录一条错误
        internal static IScenarioRestoreBackend RestoreBackend { get; set; }

        //缺省为空：多人下导演层保持离线，所有请求返回 MultiplayerUnsupported
        internal static IScenarioNetHost Net { get; set; }

        //场景实例停用完毕（所有停用钩子执行之后）时调用，宿主在这里清理归属实体；缺省什么都不做
        internal static Action<VaultScenario> ScenarioEnded { get; set; }

        //揭开地图组件范围内的地图，主线程；缺省什么都不做
        internal static Action<ScenarioMap> RevealMap { get; set; }

        //权威端整块改写了一片物块（物块坐标）之后调用：服务器把这片范围重新发给客户端；缺省什么都不做
        internal static Action<Rectangle> RegionChanged { get; set; }

        //一片区域在运行期间被停用、还在里面的某个玩家（索引）被标成离开之后调用，宿主补上区域的离开回调；缺省什么都不做
        internal static Action<ScenarioArea, int> AreaReleased { get; set; }

        //把一个动作交给主线程（换舞台后端可能在后台线程上回调导演层）；缺省当场执行
        internal static Action<Action> MainThreadSink { get; set; }

        internal static void OnMainThread(Action action) {
            if (MainThreadSink != null) {
                MainThreadSink(action);
            }
            else {
                action();
            }
        }

        static ScenarioPlatform() {
            InstallHost();
        }

        static partial void InstallHost();

        //缺省视为单人
        internal static Func<ScenarioNetRole> NetRoleSource { get; set; }

        //本地玩家的索引，专用服务器上为 -1；缺省 0
        internal static Func<int> LocalPlayerSource { get; set; }

        //当前端能否重塑物块空间（装下超出原版固定分配的舞台）；缺省不能
        internal static Func<bool> TileSpaceResizable { get; set; }

        //(去重键, 消息)，缺省丢弃
        internal static Action<string, string> ErrorSink { get; set; }

        //缺省丢弃
        internal static Action<string> InfoSink { get; set; }

        //会话期间锁定的网络身份，由导演层在会话开始时写入、结束时清空。客户端掉线后宿主立刻变回单人身份，
        //而镜像的停用钩子那之后才执行：它们必须仍然认得自己是客户端，不能把只该在裁决端做的事做一遍
        internal static ScenarioNetRole? SessionRole { get; set; }

        //宿主此刻的网络身份，不看会话锁定的值
        internal static ScenarioNetRole HostNetRole => NetRoleSource?.Invoke() ?? ScenarioNetRole.SinglePlayer;

        internal static ScenarioNetRole NetRole => SessionRole ?? HostNetRole;

        //单人与服务器：场景树在这一端裁决并执行
        internal static bool IsAuthority => NetRole != ScenarioNetRole.Client;

        internal static bool IsServer => NetRole == ScenarioNetRole.Server;

        internal static bool IsClient => NetRole == ScenarioNetRole.Client;

        internal static int LocalPlayerIndex => LocalPlayerSource?.Invoke() ?? 0;

        internal static bool CanResizeTileSpace => TileSpaceResizable?.Invoke() ?? false;

        internal static void Error(string key, string message) => ErrorSink?.Invoke(key, message);

        internal static void Info(string message) => InfoSink?.Invoke(message);
    }

    //区域还原的平台后端：激活时记录、停用时写回、读入舞台时补还原
    internal interface IScenarioRestoreBackend
    {
        void Capture(ScenarioRestore restore, ScenarioTransition transition);

        void Restore(ScenarioRestore restore, ScenarioTransition transition);

        //（客户端）服务器已经写回这片范围并重发了区块，本端善后：刷新地图，把被埋住的本地玩家挪出来
        void Settle(ScenarioRestore restore);
    }

    //场景网络消息的收发。核心只决定发什么、发给谁，封包与连接状态由宿主负责
    internal interface IScenarioNetHost
    {
        //（客户端）发给服务器
        void SendToServer(ScenarioNetOp op, Action<BinaryWriter> body);

        //（服务器）发给一个客户端；client 为 -1 时发给所有已经身处当前舞台的客户端
        void SendToClient(int client, ScenarioNetOp op, Action<BinaryWriter> body);

        //（服务器）已经身处当前舞台的客户端
        void CollectClients(ICollection<int> clients);

        //（服务器）这个客户端的连接是否还在
        bool IsConnected(int client);
    }
}
