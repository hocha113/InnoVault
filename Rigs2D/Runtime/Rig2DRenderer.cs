using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 骨架的 SpriteBatch 渲染器：按层序把每件贴图钉在骨骼近端、转到骨骼轴向
    /// <br/>核心公式：<c>rotation = 骨骼世界轴向 − 贴图轴角 + 附加旋转</c>；镜像时轴角取 π − a、锚点 x 取 宽 − x、贴图水平翻转
    /// <br/>不做 End / Begin，不设 shader：加色层、洗色层、shader 层由消费方在本体前后自行处理，坐标系从 <see cref="Rig2DDrawContext"/> 取
    /// <br/>实例开了 <see cref="Rig2DInstance.Mirrored"/> 时每件沿骨轴镜像（与件自身 <see cref="Piece2DState.Mirror"/> 异或），附加旋转跟着变号
    /// <br/>带状件（<see cref="Rig2DRibbonRenderer"/>）需要换批次，不在 <see cref="Draw(SpriteBatch, Rig2DInstance, in Rig2DDrawContext)"/> 里画；
    /// 要件与带按层序交错，用 <see cref="DrawAll(SpriteBatch, Rig2DInstance, in Rig2DDrawContext)"/>
    /// </summary>
    public static class Rig2DRenderer
    {
        /// <summary>
        /// 件与带按 <c>SortKey</c> 合并排序一起画：连续的带成组交给 <see cref="Rig2DRibbonRenderer.DrawIndices"/>（每组切一轮批次），
        /// 件在两组之间照常走当前批次。同键时件先于带。调用方必须处于一个已 <c>Begin</c> 的 Deferred 批次内
        /// </summary>
        public static void DrawAll(SpriteBatch sb, Rig2DInstance rig, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null) {
                return;
            }
            DrawAll(sb, rig, rig.Bones, in ctx);
        }

        /// <summary>
        /// 用另一套骨骼位姿绘制件与带（残影快照）
        /// </summary>
        public static void DrawAll(SpriteBatch sb, Rig2DInstance rig, Bone2D[] bones, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null || bones == null || ctx.Alpha <= 0.001f) {
                return;
            }
            if (rig.Ribbons.Length == 0) {
                Draw(sb, rig, bones, in ctx);
                return;
            }
            ReadOnlySpan<int> pieces = rig.SortedPieces();
            ReadOnlySpan<int> ribbons = rig.SortedRibbons();
            int p = 0;
            int r = 0;
            while (p < pieces.Length || r < ribbons.Length) {
                bool ribbonFirst = r < ribbons.Length
                    && (p >= pieces.Length || rig.Ribbons[ribbons[r]].SortKey < rig.Pieces[pieces[p]].SortKey);
                if (ribbonFirst) {
                    int start = r;
                    while (r < ribbons.Length
                        && (p >= pieces.Length || rig.Ribbons[ribbons[r]].SortKey < rig.Pieces[pieces[p]].SortKey)) {
                        r++;
                    }
                    Rig2DRibbonRenderer.DrawIndices(sb, rig, bones, in ctx, ribbons.Slice(start, r - start));
                    continue;
                }
                int i = pieces[p++];
                ref Piece2DState st = ref rig.Pieces[i];
                if (!st.Visible || st.SortKey < ctx.LayerMin || st.SortKey > ctx.LayerMax) {
                    continue;
                }
                int b = rig.Definition.Pieces[i].BoneIndex;
                if (b < 0 || b >= bones.Length) {
                    continue;
                }
                DrawPiece(sb, rig, i, in bones[b], in ctx);
            }
        }

        /// <summary>
        /// 绘制实例的全部可见件（按 <see cref="Piece2DState.SortKey"/> 升序，受 <see cref="Rig2DDrawContext.LayerMin"/> / <see cref="Rig2DDrawContext.LayerMax"/> 过滤）
        /// </summary>
        public static void Draw(SpriteBatch sb, Rig2DInstance rig, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null) {
                return;
            }
            Draw(sb, rig, rig.Bones, in ctx);
        }

        /// <summary>
        /// 用另一套骨骼位姿（例如 <see cref="Rig2DPoseTrail"/> 的快照）绘制实例的件；件状态仍取实例当前值
        /// </summary>
        public static void Draw(SpriteBatch sb, Rig2DInstance rig, Bone2D[] bones, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null || bones == null || ctx.Alpha <= 0.001f) {
                return;
            }
            ReadOnlySpan<int> order = rig.SortedPieces();
            for (int k = 0; k < order.Length; k++) {
                int i = order[k];
                ref Piece2DState st = ref rig.Pieces[i];
                if (!st.Visible || st.SortKey < ctx.LayerMin || st.SortKey > ctx.LayerMax) {
                    continue;
                }
                Piece2DDef def = rig.Definition.Pieces[i];
                int b = def.BoneIndex;
                if (b < 0 || b >= bones.Length) {
                    continue;
                }
                DrawPiece(sb, rig, i, in bones[b], in ctx);
            }
        }

        /// <summary>
        /// 绘制单件（骨骼位姿由调用方给，便于残影 / 局部重绘）
        /// </summary>
        public static void DrawPiece(SpriteBatch sb, Rig2DInstance rig, int pieceIndex, in Bone2D bone, in Rig2DDrawContext ctx) {
            Piece2DDef def = rig.Definition.Pieces[pieceIndex];
            ref Piece2DState st = ref rig.Pieces[pieceIndex];
            Texture2D tex = ResolveTexture(rig, pieceIndex, in st);
            if (tex == null) {
                return;
            }
            Rectangle frame = FrameRect(tex, def.Frames, st.Frame, def.FramePad);
            Vector2 scale = PieceScale(def, in st, in bone, rig.Scale, frame);
            Color color = PieceColor(def, in st, in bone, in ctx);
            if (color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0) {
                return;
            }
            ctx.BeforePiece?.Invoke(rig, pieceIndex, tex, frame);
            Vector2 proximal = def.ProximalNormalized
                ? new Vector2(def.Proximal.X * frame.Width, def.Proximal.Y * frame.Height)
                : def.Proximal;
            //骨架级镜像：件沿骨轴翻面（与件自身镜像异或），绕近端的附加旋转是局部量，跟着变号
            bool mirror = st.Mirror ^ rig.Mirrored;
            float extraRotation = st.ExtraRotation * rig.MirrorSign;
            DrawPiece(sb, tex, frame, proximal, def.Axis, bone.Pos + st.PositionOffset, bone.Dir, scale,
                mirror, color, extraRotation, ctx.ViewOffset);
        }

        /// <summary>
        /// 锚点绘制核心：贴图 <paramref name="proximal"/> 像素钉在 <paramref name="worldPos"/>，贴图轴角 <paramref name="axis"/> 转到 <paramref name="worldDir"/>
        /// </summary>
        /// <param name="sb">批次</param>
        /// <param name="tex">贴图</param>
        /// <param name="frame">源矩形（整图传 <see langword="null"/>）</param>
        /// <param name="proximal">近端像素（帧内坐标）</param>
        /// <param name="axis">贴图内在骨轴角</param>
        /// <param name="worldPos">近端世界位置</param>
        /// <param name="worldDir">骨骼世界轴向</param>
        /// <param name="scale">贴图坐标系缩放</param>
        /// <param name="mirror">水平镜像</param>
        /// <param name="color">着色</param>
        /// <param name="extraRotation">附加旋转</param>
        /// <param name="viewOffset">视口偏移</param>
        public static void DrawPiece(SpriteBatch sb, Texture2D tex, Rectangle? frame, Vector2 proximal, float axis,
            Vector2 worldPos, float worldDir, Vector2 scale, bool mirror, Color color, float extraRotation, Vector2 viewOffset) {
            if (tex == null) {
                return;
            }
            int width = frame?.Width ?? tex.Width;
            float texAxis = mirror ? MathHelper.Pi - axis : axis;
            Vector2 origin = mirror ? new Vector2(width - proximal.X, proximal.Y) : proximal;
            SpriteEffects fx = mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            float rotation = worldDir - texAxis + extraRotation;
            sb.Draw(tex, worldPos - viewOffset, frame, color, rotation, origin, scale, fx, 0f);
        }

        /// <summary>
        /// 拉伸骨节绘制：贴图约定尖端朝上、底端锚在 <paramref name="from"/>，沿骨拉到 <paramref name="to"/>
        /// </summary>
        /// <param name="sb">批次</param>
        /// <param name="tex">贴图（尖端朝上）</param>
        /// <param name="from">近端世界位置</param>
        /// <param name="to">远端世界位置</param>
        /// <param name="thickness">横向缩放</param>
        /// <param name="color">着色</param>
        /// <param name="viewOffset">视口偏移</param>
        /// <param name="tipPad">贴图两端透明留白（像素），轴长按 高 − 2×留白 换算</param>
        /// <param name="minLength">短于此长度不画</param>
        public static void DrawStretchBone(SpriteBatch sb, Texture2D tex, Vector2 from, Vector2 to, float thickness,
            Color color, Vector2 viewOffset, float tipPad = 2f, float minLength = 3f) {
            if (tex == null) {
                return;
            }
            Vector2 dir = to - from;
            float len = dir.Length();
            if (len < minLength) {
                return;
            }
            float rot = (float)Math.Atan2(dir.Y, dir.X) + MathHelper.PiOver2;
            Vector2 origin = new(tex.Width * 0.5f, tex.Height - tipPad);
            float axisLen = Math.Max(tex.Height - tipPad * 2f, 1f);
            Vector2 scale = new(thickness, len / axisLen);
            sb.Draw(tex, from - viewOffset, null, color, rot, origin, scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 多帧竖排图集的帧矩形
        /// </summary>
        public static Rectangle FrameRect(Texture2D tex, int frames, int frame, int pad) {
            if (tex == null) {
                return Rectangle.Empty;
            }
            if (frames <= 1) {
                return new Rectangle(0, 0, tex.Width, tex.Height);
            }
            int frameH = tex.Height / frames;
            int f = Math.Clamp(frame, 0, frames - 1);
            return new Rectangle(0, f * frameH, tex.Width, Math.Max(1, frameH - pad));
        }

        /// <summary>
        /// 件的最终贴图缩放：设计缩放 × 运行时缩放 × 骨架倍率，再按拉伸模式用骨长改写轴向分量
        /// </summary>
        public static Vector2 PieceScale(Piece2DDef def, in Piece2DState st, in Bone2D bone, float rigScale, Rectangle frame) {
            Vector2 baseScale = def.Scale * st.ScaleMul;
            switch (def.Stretch) {
                case Piece2DStretch.Axis: {
                    bool vertical = IsVertical(def.Axis);
                    float axisLen = def.AxisLength > 0f ? def.AxisLength : (vertical ? frame.Height : frame.Width);
                    float along = ClampStretch(bone.Length / Math.Max(axisLen, 0.001f), def);
                    return vertical
                        ? new Vector2(baseScale.X * rigScale, baseScale.Y * along)
                        : new Vector2(baseScale.X * along, baseScale.Y * rigScale);
                }
                case Piece2DStretch.Uniform: {
                    float axisLen = def.AxisLength > 0f ? def.AxisLength : Math.Max(frame.Width, frame.Height);
                    float s = ClampStretch(bone.Length / Math.Max(axisLen, 0.001f), def);
                    return baseScale * s;
                }
                default:
                    return baseScale * rigScale;
            }
        }

        /// <summary>
        /// 件的最终着色：环境光 × 设计着色 × 运行时着色 × 压暗 × 不透明度
        /// </summary>
        public static Color PieceColor(Piece2DDef def, in Piece2DState st, in Bone2D bone, in Rig2DDrawContext ctx) {
            Color c = def.Unlit ? ctx.UnlitAt() : ctx.LightAt(bone.Pos + st.PositionOffset);
            if (def.Tint != Color.White) {
                c = c.MultiplyRGBA(def.Tint);
            }
            if (st.TintMul != Color.White) {
                c = c.MultiplyRGBA(st.TintMul);
            }
            float dark = def.Dark * st.DarkMul;
            if (dark != 1f) {
                c = new Color((byte)MathHelper.Clamp(c.R * dark, 0f, 255f), (byte)MathHelper.Clamp(c.G * dark, 0f, 255f),
                    (byte)MathHelper.Clamp(c.B * dark, 0f, 255f), c.A);
            }
            float alpha = def.Alpha * st.AlphaMul;
            return alpha != 1f ? c * alpha : c;
        }

        private static Texture2D ResolveTexture(Rig2DInstance rig, int pieceIndex, in Piece2DState st) {
            if (st.TextureOverride != null) {
                return st.TextureOverride.Value;
            }
            Asset<Texture2D>[] textures = rig.Asset?.PieceTextures;
            if (textures == null || pieceIndex >= textures.Length) {
                return null;
            }
            return textures[pieceIndex]?.Value;
        }

        private static bool IsVertical(float axis) => Math.Abs(Math.Sin(axis)) > Math.Abs(Math.Cos(axis));

        private static float ClampStretch(float s, Piece2DDef def) {
            if (def.StretchMin > 0f && s < def.StretchMin) {
                s = def.StretchMin;
            }
            if (def.StretchMax > 0f && s > def.StretchMax) {
                s = def.StretchMax;
            }
            return s;
        }
    }
}
