namespace InnoVault.Scenarios
{
    //执行换舞台的平台后端。核心只经这个接缝驱动换舞台，游戏里由 Scenarios/Tml 实现
    internal interface IScenarioStageBackend
    {
        //此刻能否开始（或继续准备）换舞台；返回空表示可以，否则返回给日志看的原因。幕布合上期间每帧都会再问一次
        string CheckReady(ScenarioTransition transition);

        //主线程调用，旧舞台场景的子树已经停用、数据包已经写好；后端完成存盘、换文件、生成、读档，
        //读档成功后宿主经 NotifyStageLoaded 激活新舞台场景
        void Begin(ScenarioTransition transition);

        //换舞台途中玩家出生进入新舞台后在主线程调用，用于放置玩家
        void OnPlayerEntered(ScenarioTransition transition);

        //从菜单进入世界、会话开始时调用，用于记下根世界的身份
        void OnSessionStarted();

        //保存退出、会话即将结束时调用；stage 是当时的舞台场景
        void OnSessionEnding(VaultScenario stage);

        //取出并清除上次保存退出时所在的舞台场景 Key，没有时返回空
        string ConsumeQuitLocation();

        //会话之外的世界卸载（保存退出已存完盘）时调用
        void OnWorldUnloadedOutsideTravel();

        //模组卸载时调用
        void Reset();
    }
}
