using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 调试叠层的画线原件：线段、圆、点、文字，全部以像素贴图拼出，只在开发面板打开时调用
    /// <br/>像素贴图与文字经 <see cref="Rig2DPlatform"/> 取（游戏里是 InnoVault 占位白像素与原版描边字），离线宿主也能画骨架线
    /// </summary>
    public static class Rig2DDebugDraw
    {
        private static readonly Rectangle unit = new(0, 0, 1, 1);

        /// <summary>
        /// 调试用纯白像素；宿主没接时为 <see langword="null"/>，各画法据此跳过
        /// </summary>
        public static Texture2D Pixel => Rig2DPlatform.DebugPixel?.Invoke();

        /// <summary>
        /// 屏幕坐标线段
        /// </summary>
        public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness = 1f) {
            Texture2D px = Pixel;
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
        /// 世界坐标胶囊（两条侧边 + 两端半圆，经 <paramref name="toScreen"/> 换算）
        /// </summary>
        public static void Capsule(SpriteBatch sb, Func<Vector2, Vector2> toScreen, Vector2 a, Vector2 b, float radius, Color color, int segments = 10) {
            if (radius <= 0.5f || toScreen == null) {
                return;
            }
            Vector2 d = b - a;
            float axis = d.LengthSquared() > 0.0001f ? MathF.Atan2(d.Y, d.X) : 0f;
            Vector2 n = new(-MathF.Sin(axis), MathF.Cos(axis));
            Line(sb, toScreen(a + n * radius), toScreen(b + n * radius), color, 1f);
            Line(sb, toScreen(a - n * radius), toScreen(b - n * radius), color, 1f);
            segments = Math.Max(segments, 4);
            for (int end = 0; end < 2; end++) {
                Vector2 c = end == 0 ? a : b;
                float start = end == 0 ? axis + MathHelper.PiOver2 : axis - MathHelper.PiOver2;
                Vector2 prev = toScreen(c + new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius);
                for (int i = 1; i <= segments; i++) {
                    float ang = start + MathHelper.Pi * i / segments;
                    Vector2 cur = toScreen(c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius);
                    Line(sb, prev, cur, color, 1f);
                    prev = cur;
                }
            }
        }

        /// <summary>
        /// 画出实例全部受击胶囊组（骨架空间点经 <paramref name="toScreen"/> 换算）
        /// </summary>
        public static void Hitboxes(SpriteBatch sb, Runtime.Rig2DInstance rig, Func<Vector2, Vector2> toScreen, Color color) {
            if (rig?.Definition == null || toScreen == null) {
                return;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.List<Data.Hitbox2DDef>> kv in rig.Definition.Hitboxes) {
                foreach (Data.Hitbox2DDef h in kv.Value) {
                    if (Runtime.Rig2DHit.Capsule(rig, h, out Vector2 a, out Vector2 b, out float r)) {
                        Capsule(sb, toScreen, a, b, r, color);
                    }
                }
            }
        }

        /// <summary>
        /// 屏幕坐标实心点
        /// </summary>
        public static void Dot(SpriteBatch sb, Vector2 pos, float size, Color color) {
            Texture2D px = Pixel;
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
            Rig2DPlatform.DebugText?.Invoke(sb, text, pos, color, scale);
        }
    }
}
