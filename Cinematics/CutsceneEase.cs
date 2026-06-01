using Microsoft.Xna.Framework;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 运镜时间轴使用的基础缓动曲线
    /// </summary>
    public enum CutsceneEase
    {
        /// <summary>线性过渡</summary>
        Linear,
        /// <summary>二次方加速</summary>
        QuadIn,
        /// <summary>二次方减速</summary>
        QuadOut,
        /// <summary>二次方先加速后减速</summary>
        QuadInOut,
        /// <summary>三次方加速</summary>
        CubicIn,
        /// <summary>三次方减速</summary>
        CubicOut,
        /// <summary>三次方先加速后减速</summary>
        CubicInOut,
        /// <summary>正弦减速</summary>
        SineOut
    }

    internal static class CutsceneEaseHelper
    {
        public static float Evaluate(CutsceneEase ease, float progress) {
            progress = MathHelper.Clamp(progress, 0f, 1f);
            return ease switch {
                CutsceneEase.QuadIn => VaultUtils.EaseInQuad(progress),
                CutsceneEase.QuadOut => VaultUtils.EaseOutQuad(progress),
                CutsceneEase.QuadInOut => VaultUtils.EaseInOutQuad(progress),
                CutsceneEase.CubicIn => VaultUtils.EaseInCubic(progress),
                CutsceneEase.CubicOut => VaultUtils.EaseOutCubic(progress),
                CutsceneEase.CubicInOut => VaultUtils.EaseInOutCubic(progress),
                CutsceneEase.SineOut => VaultUtils.EaseOutSine(progress),
                _ => progress,
            };
        }
    }
}
