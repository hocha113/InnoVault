using Terraria.ModLoader;

namespace InnoVault.Cinematics
{
    internal sealed class CutscenePlayer : ModPlayer
    {
        //镜头不再在这里写：镜头栈在所有 ModifyScreenPosition 之后统一应用（见 CutsceneSystem）
        public override void SetControls() {
            if (VaultUtils.isServer || Player.whoAmI != Terraria.Main.myPlayer) {
                return;
            }

            CutsceneDirector.ApplyCameraInputLock(Player);
        }
    }
}
