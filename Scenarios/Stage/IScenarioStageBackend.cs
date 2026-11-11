using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Scenarios
{
    //执行换舞台的平台后端。核心只经这个接缝驱动换舞台，游戏里由 Scenarios/Tml 实现
    internal interface IScenarioStageBackend
    {
        //此刻能否开始（或继续准备）换舞台；返回空表示可以，否则返回给日志看的原因。幕布合上期间每帧都会再问一次。
        //只在裁决端（单人与服务器）调用
        string CheckReady(ScenarioTransition transition);

        //主线程调用，旧舞台场景的子树已经停用、数据包已经写好。
        //单人：后端在后台线程完成存盘、换文件、生成、读档，读档成功后宿主经 NotifyStageLoaded 激活新舞台场景；
        //服务器：同样的步骤在这次调用里同步做完；
        //客户端：在后台线程卸载本地的旧舞台，然后在原连接上重新等待服务器的世界信息，卸载完成时调用 ScenarioDirector.NotifyLeftStage
        void Begin(ScenarioTransition transition);

        //（单人）换舞台途中玩家出生进入新舞台后在主线程调用，用于放置玩家
        void OnPlayerEntered(ScenarioTransition transition);

        //会话开始时调用（单人与客户端：从菜单进入世界；服务器：开服读完世界），用于记下根世界的身份
        void OnSessionStarted();

        //保存退出、会话即将结束时调用；stage 是当时的舞台场景
        void OnSessionEnding(VaultScenario stage);

        //取出并清除上次保存退出时所在的舞台场景 Key，没有时返回空
        string ConsumeQuitLocation();

        //会话之外的世界卸载（保存退出已存完盘）时调用
        void OnWorldUnloadedOutsideTravel();

        //模组卸载时调用
        void Reset();

        //（服务器）换舞台已经提交、提交消息已经广播：把这些客户端置为在途，停止向它们广播世界同步、让它们的角色离场
        void OnTravelCommitted(IReadOnlyCollection<int> clients);

        //（服务器）一个在途的客户端已经卸载了旧舞台：重置它的区块表并下发当前舞台的世界信息，原版的进服流程由此在原连接上重走一遍
        void AdmitClient(int client);

        //（服务器）一个客户端已经出生在当前舞台里。traveler 为真表示它是随换舞台过来的：恢复向它广播、重新同步玩家；
        //为假表示这是一次普通的进服，原版已经做完这些
        void OnClientEntered(int client, bool traveler);

        //（服务器）这次换舞台的落点（物块坐标）；没有可用锚点时返回假，客户端留在舞台出生点，回主世界时回到各自离开前的位置
        bool TryResolveLanding(ScenarioTransition transition, out Point tile);

        //（客户端）换舞台途中本地玩家已经出生在新舞台里：让原版结束“等待世界数据”的状态
        void OnLocalPlayerEntered();

        //（客户端）场景树快照已经套用：按服务器给的落点放置本地玩家，没有落点且目标是主世界时回到离开前的位置
        void PlaceLocalPlayer(ScenarioTransition transition, Point? landing);

        //（客户端）本地玩家所在的区块是否已经收到
        bool IsLocalPlayerLoaded();
    }
}
