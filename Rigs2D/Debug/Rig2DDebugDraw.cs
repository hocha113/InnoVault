using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 调试叠层的画线原件：线段、圆、点、文字，全部以像素贴图拼出，只在开发面板打开时调用
    /// </summary>
    public static class Rig2DDebugDraw
    {
        private static readonly Rectangle unit = new(0, 0, 1, 1);

        /// <summary>
        /// 屏幕坐标线段
        /// </summary>
        public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness = 1f) {
            Texture2D px = VaultAsset.placeholder2?.Value;
            if (px == null) {
                return;
            }
            Vector2 d = b - a;
            float len = d.Length();
            if (len < 0.5f) {
                return;
            }
            float rot = MathF.Atan2(d.Y, d.X);
            sb.Draw(px, a, unit, color, rot, new Vector2(0f, 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 世界坐标圆（经 <paramref name="toScreen"/> 换算），分段折线
        /// </summary>
        public static void Circle(SpriteBatch sb, Func<Vector2, Vector2> toScreen, Vector2 center, float radius, Color color, int segments = 32) {
            if (radius <= 0.5f || toScreen == null) {
                return;
            }
            segments = Math.Max(segments, 8);
            Vector2 prev = toScreen(center + new Vector2(radius, 0f));
            for (int i = 1; i <= segments; i++) {
                float a = MathHelper.TwoPi * i / segments;
                Vector2 cur = toScreen(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
                Line(sb, prev, cur, color, 1f);
                prev = cur;
            }
        }

        /// <summary>
        /// 屏幕坐标实心点
        /// </summary>
        public static void Dot(SpriteBatch sb, Vector2 pos, float size, Color color) {
            Texture2D px = VaultAsset.placeholder2?.Value;
            if (px == null) {
                return;
            }
            sb.Draw(px, pos, unit, color, 0f, new Vector2(0.5f), size, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 带描边的小字
        /// </summary>
        public static void Text(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 0.6f) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }
            Utils.DrawBorderString(sb, text, pos, color, scale);
        }
    }
}
