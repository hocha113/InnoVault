using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 多主体构图（纯函数）：在缩放上下限之内把全部主体装进画面；装不下时以缩放下限为准，画面偏向权重大的主体，
    /// 并保证 <see cref="CameraSubject.Required"/> 的主体完整留在画面里（再装不下就对准它的中心）
    /// <br/>缩放单位同 <see cref="CameraState.Zoom"/>；原版缩放低于 1 会露出未绘制的黑边，缩放下限缺省就该是 1
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>
        /// 某缩放下画面覆盖的世界尺寸
        /// </summary>
        /// <param name="screenSize">屏幕像素尺寸</param>
        /// <param name="zoom">缩放（<see cref="CameraState.Zoom"/> 单位）</param>
        /// <param name="forcedMinimumZoom">原版强制最小缩放（超大分辨率下大于 1）</param>
        public static Vector2 VisibleSize(Vector2 screenSize, float zoom, float forcedMinimumZoom = 1f)
            => screenSize / MathF.Max(zoom * MathF.Max(forcedMinimumZoom, 0.01f), 0.01f);

        /// <summary>
        /// 构图
        /// </summary>
        /// <param name="subjects">主体</param>
        /// <param name="screenSize">屏幕像素尺寸</param>
        /// <param name="margin">主体外留白（世界像素）</param>
        /// <param name="minZoom">缩放下限（拉远的极限）</param>
        /// <param name="maxZoom">缩放上限（推近的极限）</param>
        /// <param name="forcedMinimumZoom">原版强制最小缩放</param>
        /// <param name="bias">画面偏移（占可见尺寸的比例；(0, −0.1) = 画面整体往上挪十分之一屏，把地面压低）</param>
        public static CameraState Fit(ReadOnlySpan<CameraSubject> subjects, Vector2 screenSize, float margin = 64f,
            float minZoom = 1f, float maxZoom = 2f, float forcedMinimumZoom = 1f, Vector2 bias = default) {
            if (subjects.Length == 0) {
                return new CameraState(Vector2.Zero, MathHelper.Clamp(1f, minZoom, maxZoom));
            }
            minZoom = MathF.Max(minZoom, 0.01f);
            maxZoom = MathF.Max(maxZoom, minZoom);
            Vector2 min = new(float.MaxValue), max = new(float.MinValue);
            Vector2 weighted = Vector2.Zero;
            float weightSum = 0f;
            bool anyRequired = false;
            Vector2 rMin = new(float.MaxValue), rMax = new(float.MinValue);
            for (int i = 0; i < subjects.Length; i++) {
                CameraSubject s = subjects[i];
                min = Vector2.Min(min, s.Min);
                max = Vector2.Max(max, s.Max);
                float w = MathF.Max(s.Weight, 0f);
                weighted += s.Center * w;
                weightSum += w;
                if (s.Required) {
                    anyRequired = true;
                    rMin = Vector2.Min(rMin, s.Min);
                    rMax = Vector2.Max(rMax, s.Max);
                }
            }
            Vector2 pad = new(MathF.Max(margin, 0f));
            min -= pad;
            max += pad;
            Vector2 size = Vector2.Max(max - min, Vector2.One);
            float forced = MathF.Max(forcedMinimumZoom, 0.01f);
            float fit = MathF.Min(screenSize.X / (forced * size.X), screenSize.Y / (forced * size.Y));
            float zoom = MathHelper.Clamp(fit, minZoom, maxZoom);
            Vector2 visible = VisibleSize(screenSize, zoom, forced);
            bool fits = size.X <= visible.X + 0.5f && size.Y <= visible.Y + 0.5f;
            Vector2 center = fits || weightSum <= 0f ? (min + max) * 0.5f : weighted / weightSum;
            if (!fits) {
                //装不下：逐轴把中心钳在"全部主体外接框"里能覆盖的范围内，免得画面偏向空处
                center = ClampAxis(center, min, max, visible);
            }
            center += bias * visible;
            if (anyRequired) {
                rMin -= pad;
                rMax += pad;
                center = KeepInside(center, rMin, rMax, visible);
            }
            return new CameraState(center, zoom);
        }

        private static Vector2 ClampAxis(Vector2 center, Vector2 min, Vector2 max, Vector2 visible) {
            Vector2 half = visible * 0.5f;
            float x = max.X - min.X <= visible.X ? (min.X + max.X) * 0.5f : MathHelper.Clamp(center.X, min.X + half.X, max.X - half.X);
            float y = max.Y - min.Y <= visible.Y ? (min.Y + max.Y) * 0.5f : MathHelper.Clamp(center.Y, min.Y + half.Y, max.Y - half.Y);
            return new Vector2(x, y);
        }

        /// <summary>
        /// 把画面中心钳到"外接框 [<paramref name="min"/>, <paramref name="max"/>] 完整留在可见范围里"的区间；框比画面还大时对准框中心
        /// </summary>
        public static Vector2 KeepInside(Vector2 center, Vector2 min, Vector2 max, Vector2 visible) {
            Vector2 half = visible * 0.5f;
            float x = max.X - min.X >= visible.X ? (min.X + max.X) * 0.5f : MathHelper.Clamp(center.X, max.X - half.X, min.X + half.X);
            float y = max.Y - min.Y >= visible.Y ? (min.Y + max.Y) * 0.5f : MathHelper.Clamp(center.Y, max.Y - half.Y, min.Y + half.Y);
            return new Vector2(x, y);
        }
    }
}
