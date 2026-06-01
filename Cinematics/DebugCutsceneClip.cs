using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Cinematics
{
#if DEBUG
    internal sealed class DebugCutsceneClip : CutsceneClip
    {
        public override int Priority => 100;

        protected override void BuildTimeline(CutsceneTimeline timeline) {
            timeline.Duration = 180;

            timeline
                .Add(new InputLockTrack(0, 150))
                .Add(CameraFocusTrack.Lerp(
                    0,
                    45,
                    context => context.PlayerCenter,
                    context => context.PlayerCenter + new Vector2(context.Player.direction * 220f, -140f),
                    lerpSpeed: 0.08f,
                    ease: CutsceneEase.CubicOut))
                .Add(CameraFocusTrack.Follow(
                    45,
                    105,
                    context => context.PlayerCenter + new Vector2(context.Player.direction * 220f, -140f),
                    lerpSpeed: 0.05f))
                .Add(new CameraZoomTrack(0, 60, 1f, 1.45f, 0.04f, CutsceneEase.CubicOut))
                .Add(new CameraZoomTrack(100, 50, 1.45f, 1.15f, 0.035f, CutsceneEase.QuadInOut))
                .Add(new CameraShakeTrack(70, Vector2.Zero, 16f, 0.88f, 22))
                .AddEvent(0, _ => VaultUtils.Text("Cutscene demo start", Color.Cyan))
                .AddEvent(150, _ => VaultUtils.Text("Cutscene demo restore", Color.LightGreen));
        }
    }
#endif
}
