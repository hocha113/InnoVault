using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 画布合成落位：锚点落在哪（屏幕或世界，由消费方定）、每个骨架像素对应多少目标像素、是否水平翻转
    /// </summary>
    public readonly struct Rig2DPlacement
    {
        /// <summary>
        /// 锚点在目标空间的位置
        /// </summary>
        public readonly Vector2 Anchor;
        /// <summary>
        /// 骨架像素 → 目标像素的倍率
        /// </summary>
        public readonly float UnitScale;
        /// <summary>
        /// 水平翻转（面向左）
        /// </summary>
        public readonly bool Flip;

        /// <summary>
        /// 建一个落位
        /// </summary>
        public Rig2DPlacement(Vector2 anchor, float unitScale, bool flip) {
            Anchor = anchor;
            UnitScale = unitScale;
            Flip = flip;
        }
    }

    /// <summary>
    /// 画布的一个拍摄层带：只画层序落在 [<see cref="Min"/>, <see cref="Max"/>] 的件与带；
    /// <see cref="Before"/> 在这一带开画之前调用（批次未开），分隔线、墨底这类要换着色器的底层由消费方在这里画
    /// </summary>
    public sealed class Rig2DCanvasBand
    {
        /// <summary>
        /// 带名（调试用）
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 层序下界
        /// </summary>
        public float Min { get; set; } = float.NegativeInfinity;
        /// <summary>
        /// 层序上界
        /// </summary>
        public float Max { get; set; } = float.PositiveInfinity;
        /// <summary>
        /// 是否拍这一带
        /// </summary>
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// 画这一带之前的回调：(画布, 批次, 画布矩阵, 本带)
        /// </summary>
        public Action<Rig2DCanvas, SpriteBatch, Matrix, Rig2DCanvasBand> Before { get; set; }
    }

    /// <summary>
    /// 骨架画布：把一副骨架按"锚点钉位"拍进一张固定尺寸的 RT（锚点落在 <see cref="RootPixel"/>），再由消费方按 <see cref="Rig2DPlacement"/> 合成。
    /// 骨架在自己的空间里求解（锚点即脚下地面点），巨人再大、翻转与否，拍出来的都是同一张图；合成着色器（描边、血光、剪影）留在消费方。
    /// <br/>用法：姿态推进后 <see cref="Submit"/>，宿主在当帧任何绘制之前统一 <see cref="CaptureAll"/>（游戏里挂 <c>Main.OnPreDraw</c>，InnoVault 已挂好）；
    /// 世界侧用 <see cref="Draw"/> 或自己按 <see cref="SourceRect"/> / <see cref="Origin"/> 画。<see cref="SourceRect"/> 按件的实际外接框算（含 <see cref="Margin"/>），
    /// 只合成有内容的那一块。卸世界时宿主调 <see cref="ReleaseAll"/> 释放全部 RT
    /// <br/>调试叠层：<c>rig.DebugTransform = p =&gt; canvas.Map(p, 世界落位)</c>，<c>/vaultdebug</c> 就能把骨线画在合成后的位置
    /// </summary>
    public sealed class Rig2DCanvas : IDisposable
    {
        private static readonly List<Rig2DCanvas> live = [];
        private bool pending;

        /// <summary>
        /// 建一张画布
        /// </summary>
        public Rig2DCanvas(Rig2DInstance rig, int width, int height, Vector2 rootPixel) {
            Rig = rig;
            Width = Math.Max(width, 1);
            Height = Math.Max(height, 1);
            RootPixel = rootPixel;
        }

        /// <summary>
        /// 被拍的骨架实例
        /// </summary>
        public Rig2DInstance Rig { get; set; }
        /// <summary>
        /// 画布宽
        /// </summary>
        public int Width { get; }
        /// <summary>
        /// 画布高
        /// </summary>
        public int Height { get; }
        /// <summary>
        /// 锚点在画布里的固定像素位
        /// </summary>
        public Vector2 RootPixel { get; }
        /// <summary>
        /// 内容框外扩（骨架单位，乘 Scale）：给描边、血光这类向外长的合成留地方
        /// </summary>
        public float Margin { get; set; } = 4f;
        /// <summary>
        /// 拍摄环境色（舞台光；缺省白 = 贴图本色）
        /// </summary>
        public Color Environment { get; set; } = Color.White;
        /// <summary>
        /// 拍摄采样器
        /// </summary>
        public SamplerState Sampler { get; set; } = SamplerState.LinearClamp;
        /// <summary>
        /// 拍摄层带（空 = 一次画完全部层）
        /// </summary>
        public List<Rig2DCanvasBand> Bands { get; } = [];
        /// <summary>
        /// 拍摄前回调（RT 已清空、批次未开）：口腔暗底、全件墨底一类最底层
        /// </summary>
        public Action<Rig2DCanvas, SpriteBatch, Matrix> BeforeCapture { get; set; }
        /// <summary>
        /// 拍摄后回调（批次未开）
        /// </summary>
        public Action<Rig2DCanvas, SpriteBatch, Matrix> AfterCapture { get; set; }
        /// <summary>
        /// 画布 RT（首次拍摄时建）
        /// </summary>
        public RenderTarget2D Target { get; private set; }
        /// <summary>
        /// 最近一次拍摄是否成功
        /// </summary>
        public bool HasImage { get; private set; }
        /// <summary>
        /// 拍摄那一刻的锚点（骨架空间）：合成映射的基准
        /// </summary>
        public Vector2 AnchorAtCapture { get; private set; }
        /// <summary>
        /// 本次拍摄有内容的画布区域
        /// </summary>
        public Rectangle SourceRect { get; private set; }
        /// <summary>
        /// 本次拍摄件与带的外接框（骨架空间，未外扩）
        /// </summary>
        public Vector2 BoundsMin { get; private set; }
        /// <summary>
        /// 见 <see cref="BoundsMin"/>
        /// </summary>
        public Vector2 BoundsMax { get; private set; }
        /// <summary>
        /// 本次拍摄用的画布矩阵（骨架空间 → 画布像素）
        /// </summary>
        public Matrix CaptureMatrix { get; private set; } = Matrix.Identity;
        /// <summary>
        /// 当前登记（提交过、未释放）的画布数
        /// </summary>
        public static int LiveCount => live.Count;

        //==================== 生命周期 ====================

        /// <summary>
        /// 本帧要拍：姿态推进后调用。未登记的画布自动登记
        /// </summary>
        public void Submit() {
            if (Rig2DPlatform.IsServer) {
                return;
            }
            pending = true;
            if (!live.Contains(this)) {
                live.Add(this);
            }
        }

        /// <summary>
        /// 宿主在当帧任何绘制之前调用：把提交过的画布逐张拍好（拍失败的记日志、本帧无图）
        /// </summary>
        public static void CaptureAll(GraphicsDevice gd, SpriteBatch sb) {
            if (gd == null || sb == null) {
                return;
            }
            for (int i = live.Count - 1; i >= 0; i--) {
                Rig2DCanvas c = live[i];
                if (!c.pending) {
                    continue;
                }
                c.pending = false;
                try {
                    c.Capture(gd, sb);
                } catch (Exception e) {
                    c.HasImage = false;
                    Rig2DPlatform.LogError($"[Rig2DCanvas:{c.Rig?.Name}]", $"capture failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 释放 RT 并退出登记
        /// </summary>
        public void Release() {
            live.Remove(this);
            RenderTarget2D t = Target;
            Target = null;
            HasImage = false;
            pending = false;
            if (t != null && !t.IsDisposed) {
                Rig2DPlatform.QueueMainThread(t.Dispose);
            }
        }

        /// <summary>
        /// 释放全部登记的画布（卸世界 / 卸载）
        /// </summary>
        public static void ReleaseAll() {
            for (int i = live.Count - 1; i >= 0; i--) {
                live[i].Release();
            }
            live.Clear();
        }

        /// <inheritdoc/>
        public void Dispose() => Release();

        //==================== 拍摄 ====================

        /// <summary>
        /// 立刻拍进自己的 RT（首次调用时按画布尺寸建）
        /// </summary>
        public bool Capture(GraphicsDevice gd, SpriteBatch sb) {
            if (gd == null) {
                return false;
            }
            if (Target == null || Target.IsDisposed) {
                Target = new RenderTarget2D(gd, Width, Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
            return Capture(gd, sb, Target);
        }

        /// <summary>
        /// 立刻拍进给定 RT（离线宿主每格一张图时用）；调用前后 SpriteBatch 都须未开批，渲染目标会还原
        /// </summary>
        public bool Capture(GraphicsDevice gd, SpriteBatch sb, RenderTarget2D into) {
            Rig2DInstance r = Rig;
            if (r?.Definition == null || gd == null || sb == null || into == null) {
                HasImage = false;
                return false;
            }
            RenderTargetBinding[] prev = gd.GetRenderTargets();
            gd.SetRenderTarget(into);
            gd.Clear(Color.Transparent);

            Vector2 anchor = r.Anchor;
            Vector2 shift = RootPixel - anchor;
            Matrix m = Matrix.CreateTranslation(shift.X, shift.Y, 0f);
            CaptureMatrix = m;
            Rig2DDrawContext ctx = Rig2DDrawContext.Stage(m, Environment);
            BeforeCapture?.Invoke(this, sb, m);
            if (Bands.Count == 0) {
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Sampler, DepthStencilState.None, RasterizerState.CullNone, null, m);
                Rig2DRenderer.DrawAll(sb, r, in ctx);
                sb.End();
            }
            else {
                for (int i = 0; i < Bands.Count; i++) {
                    Rig2DCanvasBand band = Bands[i];
                    if (!band.Enabled) {
                        continue;
                    }
                    band.Before?.Invoke(this, sb, m, band);
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Sampler, DepthStencilState.None, RasterizerState.CullNone, null, m);
                    Rig2DRenderer.DrawAll(sb, r, ctx.Layers(band.Min, band.Max));
                    sb.End();
                }
            }
            AfterCapture?.Invoke(this, sb, m);

            if (prev == null || prev.Length == 0) {
                gd.SetRenderTarget(null);
            }
            else {
                gd.SetRenderTargets(prev);
            }

            AnchorAtCapture = anchor;
            if (!Rig2DRenderer.MeasureBounds(r, out Vector2 min, out Vector2 max)) {
                min = max = anchor;
            }
            BoundsMin = min;
            BoundsMax = max;
            float margin = Margin * Math.Max(r.Scale, 0.001f);
            int x0 = Math.Clamp((int)MathF.Floor(min.X + shift.X - margin), 0, Width - 1);
            int y0 = Math.Clamp((int)MathF.Floor(min.Y + shift.Y - margin), 0, Height - 1);
            int x1 = Math.Clamp((int)MathF.Ceiling(max.X + shift.X + margin), x0 + 1, Width);
            int y1 = Math.Clamp((int)MathF.Ceiling(max.Y + shift.Y + margin), y0 + 1, Height);
            SourceRect = new Rectangle(x0, y0, x1 - x0, y1 - y0);
            HasImage = true;
            return true;
        }

        /// <summary>
        /// 内容是否被画布边缘裁掉（外接框越出画布）：画布开小了或锚点钉歪了
        /// </summary>
        public bool Clipped {
            get {
                Vector2 shift = RootPixel - AnchorAtCapture;
                return BoundsMin.X + shift.X < 0f || BoundsMin.Y + shift.Y < 0f
                    || BoundsMax.X + shift.X > Width || BoundsMax.Y + shift.Y > Height;
            }
        }

        //==================== 合成映射 ====================

        /// <summary>
        /// 骨架空间点 → 目标空间（按拍摄时的锚点）
        /// </summary>
        public Vector2 Map(Vector2 rigPoint, in Rig2DPlacement place) {
            Vector2 rel = (rigPoint - AnchorAtCapture) * place.UnitScale;
            if (place.Flip) {
                rel.X = -rel.X;
            }
            return place.Anchor + rel;
        }

        /// <summary>
        /// 骨架空间方向（弧度）→ 目标空间方向
        /// </summary>
        public static float MapAngle(float rigAngle, in Rig2DPlacement place) => place.Flip ? MathHelper.Pi - rigAngle : rigAngle;

        /// <summary>
        /// 合成时 <see cref="SourceRect"/> 的原点（锚点在源矩形里的位置，翻转时取镜像）
        /// </summary>
        public Vector2 Origin(bool flip) {
            Rectangle src = SourceRect;
            Vector2 local = RootPixel - new Vector2(src.X, src.Y);
            if (flip) {
                local.X = src.Width - local.X;
            }
            return local;
        }

        /// <summary>
        /// 按落位把画布画出来（调用方须处于已开的批次内；着色器由批次决定）
        /// </summary>
        public void Draw(SpriteBatch sb, in Rig2DPlacement place, Color color) {
            if (!HasImage || Target == null || Target.IsDisposed) {
                return;
            }
            sb.Draw(Target, place.Anchor, SourceRect, color, 0f, Origin(place.Flip), place.UnitScale,
                place.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }
    }
}
