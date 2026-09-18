using InnoVault.UIHandles;
using InnoVault.Vectors;
using InnoVault.Vectors.Svg;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Debugs
{
    /// <summary>
    /// 矢量绘图模块（<c>InnoVault.Vectors</c>）的样例画廊，受 <see cref="DebugSettings.VectorsShowGallery"/> 控制，
    /// 通过 <c>/vaultdebug</c> 面板的 Vectors 页打开<br/>
    /// 世界层（围绕玩家，网格后端）：旧 Trail 迁移条带、平铺纹理双遍闪电、四种接头对比（经 <see cref="VectorBatch"/> 一次提交）、渐变填充与描边、
    /// 「O / A」孔洞填充、动画虚线环、SDF 抗锯齿与硬边并排、<see cref="TrailHistory"/> 跟随玩家的拖尾、SVG 字形弧长揭示、整份 <see cref="VectorDocument"/><br/>
    /// 界面层（左下角面板，像素笔 + UI 空间网格）：环 / 弧 / 扇区 / 圆角矩形 / 虚线 / 辉光笔，同一字形两种后端并排，SVG 文档缩略图；
    /// 界面层网格提交都带 <see cref="VectorDrawOptions.SpriteBatch"/>，演示 Immediate 批次的自动恢复<br/>
    /// 网格后端自带投影矩阵，所以两种空间的样例都能在界面层批次里出画；世界样例因此叠在一切之上，画廊用途下可以接受
    /// </summary>
    internal class VectorsDebugGallery : UIHandle
    {
        //带 A 弧线指令的盾形钥匙孔字形，归一 [-1,1] 空间：外轮廓（弧 + 直线 + 二次曲线 + 闭合）、内圆（两段半圆弧）、钥匙柄
        private const string GlyphSvg = "M -0.6 -0.2 A 0.6 0.6 0 1 1 0.6 -0.2 L 0.6 0.45 Q 0 0.95 -0.6 0.45 Z "
            + "M -0.22 -0.12 a 0.22 0.22 0 1 0 0.44 0 a 0.22 0.22 0 1 0 -0.44 0 M 0 0.1 v 0.42";

        /// <summary>整份 SVG 文档样例（<c>Assets/Vectors/Gallery.svg</c>）</summary>
        [VaultLoaden("Assets/Vectors/Gallery")]
        internal static VectorDocument GalleryDoc { get; set; }

        private static readonly Vector2[] trailPoints = new Vector2[28];
        private static readonly Vector2[] boltPoints = new Vector2[14];
        private static readonly Vector2[] zigzag = new Vector2[5];
        private static readonly VectorMesh mesh = new(1024, 3072);
        private static readonly VectorBatch batch = new();
        private static readonly TrailHistory history = new(48);

        //旧 Trail 迁移样板：按点序参数化、宽度随 t 增长、颜色沿身渐变、箭头端帽
        private static readonly StrokeStyle trailStyle = new() {
            Parameterization = StrokeParameterization.PointIndex,
            WidthFunction = t => 4f + 26f * t,
            ColorFunction = (t, side) => Color.Lerp(new Color(30, 90, 220), new Color(140, 235, 255), t) * (0.1f + 0.9f * t),
            EndCap = LineCap.Arrow,
            CapLength = 36f,
        };
        //旧 ThunderTrail 光栅化：本体按贴图宽度平铺 + 1/4 宽加法流光
        private static readonly StrokeStyle boltBody = new() {
            Width = 22f,
            Color = new Color(170, 210, 255),
            Join = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            UvMode = StrokeUvMode.Tile,
        };
        private static readonly StrokeStyle boltFlow = new() {
            Width = 6f,
            Color = Color.White,
            Join = LineJoin.Round,
            UvMode = StrokeUvMode.Tile,
        };
        private static readonly StrokeStyle[] joinStyles = [
            new() { Width = 22f, Color = new Color(255, 120, 90) * 0.85f, Join = LineJoin.Averaged },
            new() { Width = 22f, Color = new Color(255, 200, 80) * 0.85f, Join = LineJoin.Miter, StartCap = LineCap.Square, EndCap = LineCap.Square },
            new() { Width = 22f, Color = new Color(120, 230, 120) * 0.85f, Join = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round },
            new() { Width = 22f, Color = new Color(140, 160, 255) * 0.85f, Join = LineJoin.Bevel, StartCap = LineCap.Arrow, EndCap = LineCap.Arrow, CapLength = 26f },
        ];
        private static readonly string[] joinNames = ["Averaged", "Miter+Square", "Round+Round", "Bevel+Arrow"];
        //渐变：径向填充（路径空间，随星形旋转）+ 线性描边
        private static readonly FillStyle starFill = new() {
            Paint = new RadialGradientPaint(new Vector2(0f, -10f), 52f, new Color(255, 240, 170), new Color(255, 110, 40) * 0.9f),
            MaxTriangleEdge = 14f,
        };
        private static readonly StrokeStyle gradientStroke = new(9f, Color.White) {
            Join = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            Paint = new LinearGradientPaint(new Vector2(0f, 0f), new Vector2(110f, 0f), new Color(255, 80, 160), new Color(80, 220, 255)),
            MaxSegmentLength = 6f,
        };
        private static readonly FillStyle panelFill = new(new Color(20, 40, 70) * 0.75f);
        private static readonly StrokeStyle panelEdge = new(3f, new Color(110, 200, 255)) { Join = LineJoin.Round };
        //孔洞填充：evenodd 的 O，nonzero 的带孔 A
        private static readonly FillStyle holeFillEvenOdd = new(new Color(200, 230, 255) * 0.9f) { Rule = FillRule.EvenOdd };
        private static readonly FillStyle holeFillNonZero = new(new Color(255, 200, 120) * 0.9f) { Rule = FillRule.NonZero };
        //虚线环
        private static readonly StrokeStyle dashRing = new(6f, new Color(255, 230, 120)) {
            Dash = [18f, 10f, 4f, 10f],
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        //SDF 对比：同一条曲线，左硬边右软边
        private static readonly StrokeStyle sdfStroke = new(5f, new Color(190, 255, 220)) {
            Join = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            MaxSegmentLength = 8f,
        };
        //玩家拖尾
        private static readonly StrokeStyle historyStyle = new() {
            Parameterization = StrokeParameterization.PointIndex,
            WidthFunction = t => 2f + 14f * t,
            ColorFunction = (t, side) => new Color(255, 160, 60) * (0.05f + 0.75f * t),
            EndCap = LineCap.Round,
        };
        private static readonly StrokeStyle glyphWorld = new(7f, new Color(255, 240, 200)) {
            Join = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        private static readonly StrokeStyle glyphPen = new(2f, new Color(200, 235, 255));
        private static readonly StrokeStyle glyphRunner = new(3f, Color.White);
        private static readonly StrokeStyle glyphMeshUi = new(2f, new Color(255, 200, 120)) {
            Join = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        private static readonly StrokeStyle wavePen = new(2f, new Color(255, 170, 90));
        private static readonly StrokeStyle dashPen = new(2f, new Color(160, 255, 200)) { Dash = [6f, 4f] };

        private static VectorPath starPath;
        private static VectorPath panelPath;
        private static VectorPath wavePath;
        private static VectorPath ringPath;
        private static VectorPath holeO;
        private static VectorPath holeA;
        private static VectorPath sdfCurve;

        public override bool Active => DebugSettings.VectorsShowGallery;

        public override void Update() {
            if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active) {
                return;
            }
            history.PushIfMoved(Main.LocalPlayer.Center, 2f);
        }

        public override void Draw(SpriteBatch spriteBatch) {
            if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active) {
                return;
            }
            EnsurePaths();
            float time = Main.GlobalTimeWrappedHourly;
            DrawWorldSamples(spriteBatch, Main.LocalPlayer.Center, time);
            DrawUISamples(spriteBatch, time);
        }

        private static void EnsurePaths() {
            starPath ??= VectorPath.Star(Vector2.Zero, 48f, 20f, 5);
            panelPath ??= VectorPath.RoundedRect(new Vector2(-80f, -34f), new Vector2(160f, 68f), 18f);
            wavePath ??= VectorPath.Cubic(new Vector2(0f, 0f), new Vector2(35f, -45f), new Vector2(75f, 45f), new Vector2(110f, 0f));
            ringPath ??= VectorPath.Circle(Vector2.Zero, 46f);
            holeO ??= VectorPath.Combine(VectorPath.Circle(Vector2.Zero, 34f), VectorPath.Circle(Vector2.Zero, 18f));
            //A：外轮廓顺时针 + 内三角形反向 = 非零规则下的孔
            holeA ??= VectorPath.Combine(
                VectorPath.Polygon([new Vector2(-32f, 36f), new Vector2(-10f, -36f), new Vector2(10f, -36f), new Vector2(32f, 36f), new Vector2(18f, 36f), new Vector2(11f, 14f), new Vector2(-11f, 14f), new Vector2(-18f, 36f)]),
                VectorPath.Polygon([new Vector2(-6f, 0f), new Vector2(6f, 0f), new Vector2(0f, -20f)]));
            sdfCurve ??= VectorPath.Cubic(new Vector2(0f, 0f), new Vector2(30f, -60f), new Vector2(70f, 60f), new Vector2(110f, 0f));
        }

        //==================== 世界层（网格后端） ====================

        private static void DrawWorldSamples(SpriteBatch sb, Vector2 anchor, float time) {
            VectorDrawOptions world = new(VectorSpace.World) { Blend = BlendState.AlphaBlend };
            VectorDrawOptions worldAdd = new(VectorSpace.World) { Blend = BlendState.Additive };

            //1. 拖尾迁移样板：一条正弦摆动的点列，points[0] 为尾、points[^1] 为头，加法混合
            Vector2 head = anchor + new Vector2(-60f, -150f);
            for (int i = 0; i < trailPoints.Length; i++) {
                float f = i / (float)(trailPoints.Length - 1);
                float back = (1f - f) * 300f;
                trailPoints[i] = head + new Vector2(-back, MathF.Sin(time * 3f - back * 0.02f) * 28f * (1f - f));
            }
            VectorRenderer.DrawStroke(trailPoints, trailStyle, in worldAdd);
            Label(sb, head + new Vector2(20f, -10f), "Trail migration: PointIndex + width/color func + Arrow cap");

            //2. 闪电光栅化：VectorPathGenerators.Lightning 每 6 tick 换形；本体平铺贴图（Wrap 采样）+ 加法流光
            Texture2D light = VaultAsset.Light?.Value;
            if (light != null) {
                Vector2 a = anchor + new Vector2(-320f, 40f);
                Vector2 b = anchor + new Vector2(-90f, 170f);
                VectorPathGenerators.Lightning(a, b, boltPoints.Length - 1, 34f, (int)(Main.GameUpdateCount / 6), boltPoints);
                boltBody.TileLength = light.Width;
                boltBody.UvOffset = -time * 1.5f;
                boltFlow.TileLength = light.Width * 0.5f;
                boltFlow.UvOffset = -time * 4f;
                VectorRenderer.DrawStroke(boltPoints, boltBody, new VectorDrawOptions(VectorSpace.World, null, light) {
                    Blend = BlendState.NonPremultiplied,
                    Sampler = SamplerState.LinearWrap,
                });
                VectorRenderer.DrawStroke(boltPoints, boltFlow, new VectorDrawOptions(VectorSpace.World, null, light) {
                    Blend = BlendState.Additive,
                    Sampler = SamplerState.LinearWrap,
                });
                Label(sb, b + new Vector2(16f, 0f), "Lightning generator + Tile UV + Wrap, two passes");
            }

            //3. 四种接头 / 端帽对比：同一条锐角折线，经 VectorBatch 合成一次提交
            batch.Begin();
            for (int k = 0; k < joinStyles.Length; k++) {
                Vector2 origin = anchor + new Vector2(90f + k * 120f, -200f);
                for (int i = 0; i < zigzag.Length; i++) {
                    zigzag[i] = origin + new Vector2((i & 1) == 0 ? 0f : 62f, i * 34f);
                }
                batch.Stroke(zigzag, joinStyles[k], in world);
                Label(sb, origin + new Vector2(-6f, -24f), joinNames[k]);
            }
            batch.End();
            Label(sb, anchor + new Vector2(90f, -60f), $"VectorBatch: 4 strokes -> {batch.LastDrawCalls} draw call");

            //4. 渐变：径向渐变星形（旋转）+ 线性渐变描边 + 圆角矩形填充与描边，合在一个网格里一次提交
            mesh.Clear();
            mesh.AppendFill(starPath, starFill, VectorTransform.At(anchor + new Vector2(150f, 60f), 1f, time * 0.8f));
            VectorTransform panelTf = VectorTransform.At(anchor + new Vector2(330f, 60f));
            mesh.AppendFill(panelPath, panelFill, in panelTf);
            mesh.AppendStroke(panelPath, panelEdge, in panelTf);
            mesh.AppendStroke(wavePath, gradientStroke, VectorTransform.At(anchor + new Vector2(275f, 60f)));
            VectorRenderer.Draw(mesh, in world);
            Label(sb, anchor + new Vector2(100f, 118f), "Paint: radial fill / linear stroke (MaxSegmentLength 6)");

            //5. 孔洞填充：evenodd 的 O 与 nonzero 的带孔 A，同网格
            mesh.Clear();
            mesh.AppendFill(holeO, holeFillEvenOdd, VectorTransform.At(anchor + new Vector2(-330f, -180f)));
            mesh.AppendFill(holeA, holeFillNonZero, VectorTransform.At(anchor + new Vector2(-240f, -180f)));
            VectorRenderer.Draw(mesh, in world);
            Label(sb, anchor + new Vector2(-370f, -240f), "FillRule: EvenOdd ring / NonZero A with hole");

            //6. 动画虚线环
            dashRing.DashOffset = -time * 40f;
            VectorRenderer.DrawStroke(ringPath, dashRing, VectorTransform.At(anchor + new Vector2(-330f, -60f)), in world);
            Label(sb, anchor + new Vector2(-380f, -120f), "Dash [18 10 4 10] + Round caps, offset animated");

            //7. SDF 抗锯齿：同一曲线，左硬边（BasicEffect）右软边 + 中心提亮
            VectorTransform hardTf = VectorTransform.At(anchor + new Vector2(60f, 320f));
            VectorTransform softTf = VectorTransform.At(anchor + new Vector2(200f, 320f));
            VectorRenderer.DrawStroke(sdfCurve, sdfStroke, in hardTf, in world);
            VectorRenderer.DrawStroke(sdfCurve, sdfStroke, in softTf, new VectorDrawOptions(VectorSpace.World) {
                Blend = BlendState.AlphaBlend,
                Antialias = 1.5f,
                Glow = 0.8f,
                GlowPower = 2f,
            });
            Label(sb, anchor + new Vector2(60f, 340f), VectorEffects.SdfStrokeAvailable ? "BasicEffect hard edge  |  SDF Antialias 1.5 + Glow 0.8" : "SDF shader missing: both hard edge");

            //8. TrailHistory：跟随玩家的历史拖尾
            if (history.Count >= 2) {
                VectorRenderer.DrawStroke(history.AsSpan(), historyStyle, in worldAdd);
            }
            Label(sb, anchor + new Vector2(30f, -20f), $"TrailHistory {history.Count}/{history.Capacity}");

            //9. SVG 字形（含 A 弧线）按弧长窗口揭示，圆接圆帽
            VectorPath glyph = VectorPath.FromSvg(GlyphSvg);
            glyphWorld.To = 0.5f + 0.5f * MathF.Sin(time * 1.2f);
            VectorRenderer.DrawStroke(glyph, glyphWorld, VectorTransform.At(anchor + new Vector2(-120f, 220f), 70f), in world);
            Label(sb, anchor + new Vector2(-200f, 296f), $"SVG d with A arcs, From/To reveal = {glyphWorld.To:0.00}");

            //10. 整份 SVG 文档：VaultLoaden 加载，Fit 到 150px 高
            VectorDocument doc = GalleryDoc;
            if (doc != null && !doc.IsEmpty) {
                doc.Draw(doc.Fit(anchor + new Vector2(-330f, 220f), 150f), in world);
                Label(sb, anchor + new Vector2(-440f, 300f), $"VectorDocument Gallery.svg: {doc.Shapes.Count} shapes, warnings {doc.Warnings.Count}");
            }
            else {
                Label(sb, anchor + new Vector2(-440f, 300f), "VectorDocument Gallery.svg: not loaded");
            }
        }

        //==================== 界面层（像素笔 + 网格 UI 空间） ====================

        private static void DrawUISamples(SpriteBatch sb, float time) {
            float uiHeight = Main.screenHeight / Main.UIScale;
            Rectangle panel = new(20, (int)(uiHeight - 270f), 640, 250);
            VectorPen.FillRect(sb, panel, new Color(8, 16, 30) * 0.88f);
            VectorPen.RectOutline(sb, panel, 2f, new Color(80, 160, 240));
            Utils.DrawBorderString(sb, "Vectors Gallery: pixel pen (left)  vs  mesh in UI space (right, SpriteBatch restore on)", new Vector2(panel.X + 12, panel.Y + 8), new Color(150, 220, 255), 0.7f);

            //界面层网格提交统一带上当前批次：Immediate 批次下会自动恢复精灵着色器
            VectorDrawOptions ui = new(sb, VectorSpace.UI) { Blend = BlendState.AlphaBlend };

            float y = panel.Y + 70f;
            //环 + 弧形量表 + 实心扇区
            Vector2 gaugeCenter = new(panel.X + 70f, y + 30f);
            float gauge = 0.5f + 0.5f * MathF.Sin(time * 1.7f);
            VectorPen.Ring(sb, gaugeCenter, 28f, 2f, new Color(70, 120, 190));
            VectorPen.Arc(sb, gaugeCenter, 28f, -MathHelper.PiOver2, -MathHelper.PiOver2 + MathHelper.TwoPi * gauge, 4f, new Color(120, 230, 255));
            VectorPen.FillSector(sb, gaugeCenter, 10f, 19f, -MathHelper.PiOver2, -MathHelper.PiOver2 + MathHelper.TwoPi * gauge, new Color(120, 230, 255) * 0.5f);
            Utils.DrawBorderString(sb, "Ring / Arc / FillSector", new Vector2(panel.X + 20, y + 72f), new Color(160, 200, 240), 0.6f);

            //圆角矩形描边 + 三次曲线辉光笔 + 虚线（像素笔）
            VectorPen.RoundedRectOutline(sb, new Vector2(panel.X + 140f, y), new Vector2(120f, 62f), 16f, 2f, new Color(200, 220, 255));
            VectorPen.GlowStroke(sb, wavePath, wavePen, VectorTransform.At(new Vector2(panel.X + 145f, y + 31f)));
            dashPen.DashOffset = -time * 20f;
            VectorPen.Stroke(sb, wavePath, dashPen, VectorTransform.At(new Vector2(panel.X + 145f, y + 46f)));
            VectorPen.DashedLine(sb, new Vector2(panel.X + 140f, y + 70f), new Vector2(panel.X + 260f, y + 70f), 2f, new Color(255, 220, 120), 8f, 5f, time * 30f);
            Utils.DrawBorderString(sb, "RoundedRect / Glow / Dash pen", new Vector2(panel.X + 140, y + 78f), new Color(160, 200, 240), 0.6f);

            //同一字形：像素笔（含巡行亮笔）vs 网格 UI 空间
            VectorPath glyph = VectorPath.FromSvg(GlyphSvg);
            VectorTransform penTf = VectorTransform.At(new Vector2(panel.X + 340f, y + 30f), 34f);
            VectorTransform meshTf = VectorTransform.At(new Vector2(panel.X + 430f, y + 30f), 34f);
            VectorPen.GlowStroke(sb, glyph, glyphPen, in penTf);
            VectorPen.StrokeRunner(sb, glyph, glyphRunner, in penTf, time * 0.35f, 0.18f);
            VectorRenderer.DrawStroke(glyph, glyphMeshUi, in meshTf, in ui);
            Utils.DrawBorderString(sb, "VectorPen + Runner", new Vector2(panel.X + 295, y + 72f), new Color(160, 200, 240), 0.6f);
            Utils.DrawBorderString(sb, "VectorRenderer (UI)", new Vector2(panel.X + 385, y + 72f), new Color(255, 200, 120), 0.6f);

            //SVG 文档缩略图（UI 空间网格）
            VectorDocument doc = GalleryDoc;
            if (doc != null && !doc.IsEmpty) {
                doc.Draw(doc.Fit(new Vector2(panel.X + 560f, y + 30f), 70f), in ui);
                Utils.DrawBorderString(sb, "Gallery.svg", new Vector2(panel.X + 525, y + 72f), new Color(160, 200, 240), 0.6f);
            }

            Utils.DrawBorderString(sb, $"glyph: {glyph.SubPathCount} subpaths, {glyph.PointCount} pts, length {glyph.TotalLength:0.00}   SpriteBatchState.Available = {SpriteBatchState.Available}",
                new Vector2(panel.X + 12, panel.Bottom - 26f), new Color(120, 160, 200), 0.6f);
        }

        //==================== 工具 ====================

        //世界坐标 → 当前界面批次坐标（界面批次挂 UIScaleMatrix，所以把屏幕像素再除一次 UI 缩放）
        private static void Label(SpriteBatch sb, Vector2 world, string text) {
            Vector2 ui = VaultUtils.WorldToScreen(world) / Main.UIScale;
            Utils.DrawBorderString(sb, text, ui, new Color(200, 225, 255), 0.6f);
        }
    }
}
