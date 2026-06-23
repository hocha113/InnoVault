using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;

namespace InnoVault.Narrative.Styling
{
    /// <summary>
    /// 中性阴影面板的着色器参数集合。集中描述一块圆角面板的填充渐变 / 柔和外阴影 / 内缘高光，<br/>
    /// 供 <see cref="NarrativeSkinDraw.DrawSoftPanel"/> 使用，皮肤可据主题构建不同实例
    /// </summary>
    public struct SoftPanelStyle
    {
        /// <summary>阴影外扩边距（像素），面板实体相对绘制 quad 内缩这么多</summary>
        public float Margin;
        /// <summary>圆角半径（像素）</summary>
        public float Radius;
        /// <summary>阴影羽化宽度（像素）</summary>
        public float ShadowSoft;
        /// <summary>阴影最大不透明度</summary>
        public float ShadowAlpha;
        /// <summary>阴影偏移（像素，正 y 向下）</summary>
        public Vector2 ShadowOffset;
        /// <summary>顶部填充色（含基础透明度）</summary>
        public Color FillTop;
        /// <summary>底部填充色（含基础透明度）</summary>
        public Color FillBottom;
        /// <summary>内缘高光色（含强度）</summary>
        public Color Rim;
        /// <summary>内缘高光宽度（像素）</summary>
        public float RimWidth;

        /// <summary>中性默认面板：偏冷的深色半透明、轻外阴影、细内缘高光</summary>
        public static SoftPanelStyle Default => new() {
            Margin = 34f,
            Radius = 16f,
            ShadowSoft = 22f,
            ShadowAlpha = 0.42f,
            ShadowOffset = new Vector2(0f, 8f),
            FillTop = new Color(26, 28, 36, 214),
            FillBottom = new Color(15, 17, 23, 226),
            Rim = new Color(150, 162, 184, 60),
            RimWidth = 1.4f,
        };

        /// <summary>细长药丸：用于滚动条滑块等小元素，几乎无阴影、强圆角</summary>
        public static SoftPanelStyle Pill(Color fill, float radius) => new() {
            Margin = 3f,
            Radius = radius,
            ShadowSoft = 4f,
            ShadowAlpha = 0.22f,
            ShadowOffset = new Vector2(0f, 1f),
            FillTop = fill,
            FillBottom = fill,
            Rim = new Color(255, 255, 255, 28),
            RimWidth = 1f,
        };
    }

    /// <summary>
    /// 皮肤绘制用的共享图元助手，集中维护填充矩形 / 边框 / 着色器面板 / 滚动裁剪等基础绘制，<br/>
    /// 避免各皮肤重复复制相同的绘制代码
    /// </summary>
    public static class NarrativeSkinDraw
    {
        [VaultLoaden("InnoVault/Effects/")]
        private static Asset<Effect> SoftPanel { get; set; }

        private static Texture2D Pixel => VaultAsset.placeholder2.Value;
        private static readonly Rectangle PixelSrc = new(0, 0, 1, 1);
        private static readonly RasterizerState ScissorRaster = new() { CullMode = CullMode.None, ScissorTestEnable = true };
        private static Rectangle _prevScissor;

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

        /// <summary>绘制带阴影、填充与描边的标准面板（无着色器时的回退实现）</summary>
        public static void DrawPanel(SpriteBatch sb, Rectangle rect, Color fill, Color edge, float alpha) {
            Rectangle shadow = rect;
            shadow.Offset(4, 5);
            FillRect(sb, shadow, Color.Black * (alpha * 0.4f));
            FillRect(sb, rect, fill * alpha);
            DrawBorder(sb, rect, edge * alpha);
        }

        /// <summary>
        /// 用 <c>SoftPanel</c> 着色器程序化绘制一块中性阴影圆角面板。<br/>
        /// 内部会临时 End/Begin 当前 SpriteBatch 以挂载着色器，绘制后恢复为标准 UI 状态。<br/>
        /// 着色器缺失时回退到 <see cref="DrawPanel"/>
        /// </summary>
        public static void DrawSoftPanel(SpriteBatch sb, Rectangle panel, in SoftPanelStyle style, float alpha) {
            if (alpha <= 0.001f) {
                return;
            }
            Effect fx = SoftPanel?.Value;
            if (fx == null) {
                DrawPanel(sb, panel, style.FillBottom, style.Rim, alpha);
                return;
            }

            int margin = (int)MathF.Ceiling(style.Margin);
            Rectangle quad = panel;
            quad.Inflate(margin, margin);

            EffectParameterCollection p = fx.Parameters;
            p["PanelSize"].SetValue(new Vector2(quad.Width, quad.Height));
            p["Margin"].SetValue(style.Margin);
            p["Radius"].SetValue(style.Radius);
            p["ShadowSoft"].SetValue(style.ShadowSoft);
            p["ShadowAlpha"].SetValue(style.ShadowAlpha);
            p["ShadowOffset"].SetValue(style.ShadowOffset);
            p["FillTop"].SetValue(style.FillTop.ToVector4());
            p["FillBottom"].SetValue(style.FillBottom.ToVector4());
            p["RimColor"].SetValue(style.Rim.ToVector4());
            p["RimWidth"].SetValue(style.RimWidth);
            p["Alpha"].SetValue(alpha);

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, fx, Main.UIScaleMatrix);
            sb.Draw(Pixel, quad, PixelSrc, Color.White);
            sb.End();
            BeginUI(sb, RasterizerState.CullCounterClockwise);
        }

        /// <summary>临时切到带裁剪的 SpriteBatch，把后续绘制裁剪在 <paramref name="uiRect"/> 内（UI 坐标）</summary>
        public static void BeginClip(SpriteBatch sb, Rectangle uiRect) {
            sb.End();
            GraphicsDevice gd = sb.GraphicsDevice;
            _prevScissor = gd.ScissorRectangle;
            Rectangle device = Rectangle.Intersect(UiToDevice(uiRect), gd.Viewport.Bounds);
            gd.ScissorRectangle = device;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, ScissorRaster, null, Main.UIScaleMatrix);
        }

        /// <summary>结束裁剪并恢复标准 UI SpriteBatch 状态，须与 <see cref="BeginClip"/> 成对</summary>
        public static void EndClip(SpriteBatch sb) {
            sb.End();
            sb.GraphicsDevice.ScissorRectangle = _prevScissor;
            BeginUI(sb, RasterizerState.CullCounterClockwise);
        }

        private static void BeginUI(SpriteBatch sb, RasterizerState raster)
            => sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, raster, null, Main.UIScaleMatrix);

        private static Rectangle UiToDevice(Rectangle r) {
            Matrix m = Main.UIScaleMatrix;
            Vector2 tl = Vector2.Transform(new Vector2(r.Left, r.Top), m);
            Vector2 br = Vector2.Transform(new Vector2(r.Right, r.Bottom), m);
            return new Rectangle(
                (int)MathF.Round(tl.X),
                (int)MathF.Round(tl.Y),
                (int)MathF.Round(br.X - tl.X),
                (int)MathF.Round(br.Y - tl.Y));
        }
    }
}
