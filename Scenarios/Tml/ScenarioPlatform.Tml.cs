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
        }
    }
}
