using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Debugs
{
    /// <summary>
    /// 打开InnoVault开发者调试面板的指令
    /// 使用方法: /vaultdebug
    /// </summary>
    public class VaultDebugCommand : ModCommand
    {
        /// <inheritdoc/>
        public override string Command => "vaultdebug";
        /// <inheritdoc/>
        public override string Description => "vaultdebug";
        /// <inheritdoc/>
        public override string Usage => "/vaultdebug";
        /// <inheritdoc/>
        public override CommandType Type => CommandType.Chat;
        /// <inheritdoc/>
        public override void Action(CommandCaller caller, string input, string[] args) {
            if (Main.dedServ) {
                return;
            }
            DeveloperPanelUI.Instance?.Toggle();
        }
    }
}
