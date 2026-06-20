using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Narrative.Styling
{
    /// <summary>
    /// 皮肤绘制用的共享图元助手，集中维护填充矩形 / 边框等基础绘制，<br/>
    /// 避免各皮肤重复复制相同的像素绘制代码
    /// </summary>
    public static class NarrativeSkinDraw
    {
        private static Texture2D Pixel => VaultAsset.placeholder2.Value;
        private static readonly Rectangle PixelSrc = new(0, 0, 1, 1);

        /// <summary>填充一个实心矩形</summary>
        public static void FillRect(SpriteBatch sb, Rectangle rect, Color color)
            => sb.Draw(Pixel, rect, PixelSrc, color);

        /// <summary>绘制一个矩形描边</summary>
        public static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 2) {
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), PixelSrc, color);
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), PixelSrc, color);
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), PixelSrc, color);
            sb.Draw(Pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), PixelSrc, color);
        }

        /// <summary>绘制带阴影、填充与描边的标准面板</summary>
        public static void DrawPanel(SpriteBatch sb, Rectangle rect, Color fill, Color edge, float alpha) {
            Rectangle shadow = rect;
            shadow.Offset(4, 5);
            FillRect(sb, shadow, Color.Black * (alpha * 0.4f));
            FillRect(sb, rect, fill * alpha);
            DrawBorder(sb, rect, edge * alpha);
        }
    }
}
