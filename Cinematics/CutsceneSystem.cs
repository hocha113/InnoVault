using Terraria;
using Terraria.Graphics;
using Terraria.ModLoader;

namespace InnoVault.Cinematics
{
    internal sealed class CutsceneSystem : ModSystem
    {
        public override void Load() {
            if (Main.dedServ) {
                return;
            }
            //镜头栈在原版镜头与所有模组的 ModifyScreenPosition 之后应用：直接写 screenPosition 的旧写法成为最底层
            On_Main.DoDraw_UpdateCameraPosition += UpdateCameraPositionHook;
        }

        public override void Unload() {
            if (!Main.dedServ) {
                On_Main.DoDraw_UpdateCameraPosition -= UpdateCameraPositionHook;
            }
            VaultCamera.Clear();
        }

        private static void UpdateCameraPositionHook(On_Main.orig_DoDraw_UpdateCameraPosition orig) {
            orig();
            VaultCamera.Apply();
        }

        public override void PostUpdateEverything() {
            if (VaultUtils.isServer) {
                return;
            }

            //先让演出写本帧的焦点 / 缩放 / 输入锁，再推进镜头栈的权重与平滑
            CutsceneDirector.Update();
            VaultCamera.Update();
        }

        public override void ModifyTransformMatrix(ref SpriteViewMatrix Transform) => VaultCamera.ModifyTransform(ref Transform);

        public override void OnWorldUnload() {
            CutsceneDirector.Reset();
            VaultCamera.ResetState();
        }

        public override void ClearWorld() {
            if (!Main.dedServ) {
                VaultCamera.ResetState();
            }
        }
    }
}
