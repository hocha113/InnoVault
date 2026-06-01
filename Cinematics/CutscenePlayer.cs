using Terraria.ModLoader;

namespace InnoVault.Cinematics
{
    internal sealed class CutscenePlayer : ModPlayer
    {
        public override void ModifyScreenPosition() {
            if (VaultUtils.isServer || Player.whoAmI != Terraria.Main.myPlayer) {
                return;
            }

            CutsceneDirector.Camera.ApplyScreenPosition();
        }

        public override void SetControls() {
            if (VaultUtils.isServer || Player.whoAmI != Terraria.Main.myPlayer) {
                return;
            }

            CutsceneDirector.Camera.ApplyInputLock(Player);
        }
    }
}
