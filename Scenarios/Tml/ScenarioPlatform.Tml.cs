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
            ErrorSink = VaultMod.LoggerError;
            InfoSink = message => VaultMod.Instance?.Logger.Info(message);
        }
    }
}
