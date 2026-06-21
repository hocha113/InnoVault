using System;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 叙事面板进出场动画的共享缓动与位移计算，对齐 CWR ADV 原型（线性帧进度 + EaseOutBack/EaseInCubic）
    /// </summary>
    public static class NarrativePanelMotion
    {
        /// <summary>各 UI 类型的位移与透明度配置，数值来自 CWR 原型</summary>
        public readonly struct Profile(float openSlide, float closeSlide, float alphaBoost = 1f)
        {
            /// <summary>打开动画的 Y 轴滑移距离（像素）</summary>
            public float OpenSlide { get; } = openSlide;
            /// <summary>关闭动画的 Y 轴滑移距离（像素）</summary>
            public float CloseSlide { get; } = closeSlide;
            /// <summary>透明度增益系数，大于 1 时可让面板更快达到完全不透明</summary>
            public float AlphaBoost { get; } = alphaBoost;

            /// <summary>对话框：18/14 帧，90px 滑移，线性 alpha</summary>
            public static Profile Dialogue => new(90f, 90f, 1f);

            /// <summary>选择框：12/10 帧，60px 滑移，alpha ×1.5 封顶</summary>
            public static Profile Choice => new(60f, 60f, 1.5f);

            /// <summary>奖励弹窗近似：24/16 帧，30px 滑移</summary>
            public static Profile Popup => new(30f, 30f, 1f);
        }

        /// <summary>根据打开进度计算位移缓动值</summary>
        public static float ResolveEased(float progress, bool isClosing)
            => isClosing ? VaultUtils.EaseInCubic(progress) : VaultUtils.EaseOutBack(progress);

        /// <summary>根据打开进度计算透明度</summary>
        public static float ResolveAlpha(float progress, Profile profile)
            => profile.AlphaBoost <= 1f
                ? progress
                : Math.Min(progress * profile.AlphaBoost, 1f);

        /// <summary>根据打开进度计算 Y 轴附加位移</summary>
        public static float ResolveSlide(float progress, bool isClosing, Profile profile) {
            float eased = ResolveEased(progress, isClosing);
            float distance = isClosing ? profile.CloseSlide : profile.OpenSlide;
            return (1f - eased) * distance;
        }
    }
}
