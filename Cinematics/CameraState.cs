using Microsoft.Xna.Framework;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 一个镜头状态：屏幕中心对准的世界点 + 缩放（与原版 <c>Main.GameZoomTarget</c> 同一单位：1 = 原版缺省，实际矩阵缩放再乘 <c>Main.ForcedMinimumZoom</c>）
    /// </summary>
    public struct CameraState
    {
        /// <summary>屏幕中心对准的世界坐标</summary>
        public Vector2 Center;
        /// <summary>缩放倍率</summary>
        public float Zoom;

        /// <summary>
        /// 建一个镜头状态
        /// </summary>
        public CameraState(Vector2 center, float zoom) {
            Center = center;
            Zoom = zoom;
        }

        /// <summary>
        /// 两个镜头状态之间插值
        /// </summary>
        public static CameraState Lerp(in CameraState a, in CameraState b, float t)
            => new(Vector2.Lerp(a.Center, b.Center, t), MathHelper.Lerp(a.Zoom, b.Zoom, t));

        /// <inheritdoc/>
        public override readonly string ToString() => $"({Center.X:F0}, {Center.Y:F0}) x{Zoom:F2}";
    }

    /// <summary>
    /// 构图主体：一个世界外接框 + 权重；<see cref="Required"/> 的主体在缩放下限装不下全部主体时也必须留在画面里
    /// </summary>
    public struct CameraSubject
    {
        /// <summary>外接框左上（世界）</summary>
        public Vector2 Min;
        /// <summary>外接框右下（世界）</summary>
        public Vector2 Max;
        /// <summary>装不下时决定画面偏向谁的权重</summary>
        public float Weight;
        /// <summary>必须留在画面里（主主体）</summary>
        public bool Required;

        /// <summary>
        /// 按外接框建主体
        /// </summary>
        public CameraSubject(Vector2 min, Vector2 max, float weight = 1f, bool required = false) {
            Min = Vector2.Min(min, max);
            Max = Vector2.Max(min, max);
            Weight = weight;
            Required = required;
        }

        /// <summary>
        /// 按矩形建主体（实体 hitbox）
        /// </summary>
        public static CameraSubject FromRect(Rectangle rect, float weight = 1f, bool required = false)
            => new(new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Bottom), weight, required);

        /// <summary>
        /// 按中心 + 半尺寸建主体
        /// </summary>
        public static CameraSubject FromCenter(Vector2 center, Vector2 halfSize, float weight = 1f, bool required = false)
            => new(center - halfSize, center + halfSize, weight, required);

        /// <summary>外接框中心</summary>
        public readonly Vector2 Center => (Min + Max) * 0.5f;
    }
}
