using Terraria;
using Terraria.ID;

namespace InnoVault.Scenarios
{
    //游戏宿主：把平台接缝接到 tModLoader
    internal static partial class ScenarioPlatform
    {
        static partial void InstallHost() {
            NetRoleSource = static () => Main.netMode switch {
                NetmodeID.MultiplayerClient => ScenarioNetRole.Client,
                NetmodeID.Server => ScenarioNetRole.Server,
                _ => ScenarioNetRole.SinglePlayer,
            };
            LocalPlayerSource = static () => Main.dedServ ? -1 : Main.myPlayer;
            TileSpaceResizable = static () => ScenarioTileSpace.Available;
            ErrorSink = VaultMod.LoggerError;
            InfoSink = message => VaultMod.Instance?.Logger.Info(message);
            StageBackend = new TmlStageBackend();
            RestoreBackend = new TmlRestoreBackend();
            Net = new TmlScenarioNetHost();
            ScenarioEnded = ScenarioEntities.CleanUp;
            RevealMap = ScenarioMapPolicy.Reveal;
            RegionChanged = ScenarioRegionSync.Resend;
            AreaReleased = ScenarioAreaTracker.NotifyReleased;
            //专用服务器上导演层只在主线程上运行，当场执行；客户端与单人排进原版的主线程动作队列（菜单期间也会清空）
            MainThreadSink = static action => {
                if (Main.dedServ) {
                    action();
                }
                else {
                    Main.QueueMainThreadAction(action);
                }
            };
        }
    }
}
