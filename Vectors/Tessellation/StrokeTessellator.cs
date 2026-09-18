using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace InnoVault.Vectors.Tessellation
{
    /// <summary>
    /// 折线 → 描边三角网格：接头（平均 / 尖接 / 圆接 / 斜接）、端帽（平 / 方 / 圆 / 箭头，贴图端帽只收集印章交给调用方）、弧长窗口、长段细分、虚线切片、拉伸或平铺 UV、逐点宽度与颜色（含 <see cref="VectorPaint"/>）
    /// <br/>预处理流水线：变换 → 弧长窗口裁剪 → 去重 → <see cref="StrokeStyle.MaxSegmentLength"/> 细分 → 虚线切片成若干「片」；每片各自生成条带与端帽
    /// <br/>所有暂存缓冲为静态复用，只允许在渲染线程调用，且宽度 / 颜色函数内不得再发起描边（DEBUG 下有重入断言）
    /// <br/>约定：<c>normal = (-tangent.Y, tangent.X)</c>，法线正侧 v = 0、负侧 v = 1（<see cref="StrokeStyle.FlipV"/> 交换），与旧 <c>Trail</c> 一致
    /// </summary>
    internal static class StrokeTessellator
    {
        //相邻两点在输出空间近于此距离时合并
        private const float MinSegmentLength = 1e-3f;
        //接近直线的转角不补接头
        private const float StraightCross = 0.0175f;
        //圆接 / 圆帽的角步长：弦弓高 ≤ r/100
        private static readonly float roundStep = 2f * MathF.Acos(1f - 0.01f);

        //输出空间的源折线：点 / 全局 t / 累计弧长
        private static readonly List<Vector2> srcPts = new(256);
        private static readonly List<float> srcTs = new(256);
        private static readonly List<float> srcArcs = new(256);
        //窗口裁剪 + 去重 + 细分之后的折线（切片前）
        private static readonly List<Vector2> midPts = new(256);
        private static readonly List<float> midTs = new(256);
        private static readonly List<float> midArcs = new(256);
        //切片后的全部片段点，按片段连续存放
        private static readonly List<Vector2> allPts = new(256);
        private static readonly List<float> allTs = new(256);
        private static readonly List<float> allArcs = new(256);
        private static readonly List<Piece> pieces = new(16);
        //当前片段的工作表
        private static readonly List<Vector2> pts = new(256);
        private static readonly List<float> ts = new(256);
        private static readonly List<float> arcs = new(256);
        private static readonly List<Vector2> tangents = new(256);
        //连续条带两侧的顶点索引
        private static readonly List<int> sideA = new(256);
        private static readonly List<int> sideB = new(256);
        //闭合条带回到起点那一对顶点的 t 与弧长
        private static float closeT;
        private static float closeArc;
        //当前片段是否按闭合条带生成
        private static bool workClosed;
        //本次描边的路径 → 输出变换，Paint 按路径空间求色时要反变换
        private static VectorTransform curTransform = VectorTransform.Identity;
        //DEBUG 重入哨兵
        private static bool busy;

        //本次描边收集到的贴图端帽印章（只在 LineCap.Texture 下产生）
        private static readonly List<CapStamp> capStamps = new(4);

        private struct Piece
        {
            public int Start;
            public int Count;
            public bool Closed;
            public bool PathStart;
            public bool PathEnd;
        }

        /// <summary>一枚贴图端帽印章：端点、朝外的单位切向、输出像素尺寸、颜色</summary>
        internal struct CapStamp
        {
            public Vector2 Position;
            public Vector2 Outward;
            public Vector2 Size;
            public Color Color;
        }

        /// <summary>预处理结果</summary>
        internal enum PrepareResult
        {
            /// <summary>无可画内容</summary>
            None,
            /// <summary>退化为一点（<see cref="WorkPoints"/> 只有一项）</summary>
            Dot,
            /// <summary>至少一片折线，逐片用 <see cref="LoadPiece"/> 取</summary>
            Polyline,
        }

        /// <summary>当前片段的输出空间折线（渲染线程只读）</summary>
        internal static List<Vector2> WorkPoints => pts;
        /// <summary>与 <see cref="WorkPoints"/> 对应的全局 t</summary>
        internal static List<float> WorkTs => ts;
        /// <summary>当前片段是否闭合</summary>
        internal static bool WorkClosed => workClosed;
        /// <summary>预处理后的片段数</summary>
        internal static int PieceCount => pieces.Count;
        /// <summary>上一次描边收集到的贴图端帽印章数（含叠印，每枚一个四边形）</summary>
        internal static int CapStampCount => capStamps.Count;

        /// <summary>丢弃上一次收集的贴图端帽；网格后端在 <c>AppendStroke</c> 之前调一次</summary>
        internal static void ResetCapStamps() => capStamps.Clear();

        /// <summary>把收集到的贴图端帽拼成四边形（uv 0..1、+X 指向路径之外）追加到 <paramref name="mesh"/></summary>
        internal static void AppendCapQuads(VectorMesh mesh) {
            for (int i = 0; i < capStamps.Count; i++) {
                CapStamp stamp = capStamps[i];
                Vector2 ax = stamp.Outward * (stamp.Size.X * 0.5f);
                Vector2 ay = new Vector2(-stamp.Outward.Y, stamp.Outward.X) * (stamp.Size.Y * 0.5f);
                mesh.AppendQuad(stamp.Position - ax - ay, stamp.Position + ax - ay, stamp.Position + ax + ay, stamp.Position - ax + ay,
                    stamp.Color, Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY);
            }
        }

        /// <summary>描边整条路径</summary>
        public static void Append(VectorMesh mesh, VectorPath path, StrokeStyle style, in VectorTransform transform) {
            capStamps.Clear();
            if (!ResolveWindow(style, out float lo, out float hi)) {
                return;
            }
            Enter();
            try {
                bool fullWindow = style.IsFullWindow;
                for (int s = 0; s < path.SubPathCount; s++) {
                    EmitPrepared(mesh, style, PrepareSubPath(path, s, style, in transform, fullWindow, lo, hi));
                }
            }
            finally {
                Leave();
            }
        }

        /// <summary>描一条已在输出空间的点列，t 按点列自身归一</summary>
        public static void Append(VectorMesh mesh, ReadOnlySpan<Vector2> points, bool closed, StrokeStyle style) {
            capStamps.Clear();
            if (!ResolveWindow(style, out float lo, out float hi)) {
                return;
            }
            Enter();
            try {
                EmitPrepared(mesh, style, PrepareSpan(points, closed, style, style.IsFullWindow, lo, hi));
            }
            finally {
                Leave();
            }
        }

        private static void EmitPrepared(VectorMesh mesh, StrokeStyle style, PrepareResult result) {
            switch (result) {
                case PrepareResult.Dot:
                    EmitDot(mesh, style, pts[0], ts[0], style.StartCap, style.EndCap);
                    break;
                case PrepareResult.Polyline:
                    for (int i = 0; i < pieces.Count; i++) {
                        LoadPiece(i, style, out LineCap startCap, out LineCap endCap);
                        if (pts.Count == 1) {
                            EmitDot(mesh, style, pts[0], ts[0], startCap, endCap);
                        }
                        else {
                            Emit(mesh, style, workClosed, startCap, endCap);
                        }
                    }
                    break;
            }
        }

        [Conditional("DEBUG")]
        private static void Enter() {
            Debug.Assert(!busy, "[Vectors] StrokeTessellator re-entered: do not call DrawStroke / VectorPen from inside a WidthFunction / ColorFunction / Paint");
            busy = true;
        }

        [Conditional("DEBUG")]
        private static void Leave() => busy = false;

        //==================== 颜色 ====================

        /// <summary>按样式求某顶点的颜色：颜色函数 &gt; Paint（路径空间时先反变换）&gt; 常量，结果乘 <see cref="StrokeStyle.Opacity"/></summary>
        internal static Color Col(StrokeStyle style, float t, float side, Vector2 outputPos) {
            Color color;
            if (style.ColorFunction != null) {
                color = style.ColorFunction(t, side);
            }
            else {
                VectorPaint paint = style.Paint;
                color = paint != null
                    ? paint.Evaluate(paint.Space == PaintSpace.Path ? curTransform.ApplyInverse(outputPos) : outputPos)
                    : style.Color;
            }
            float opacity = style.Opacity;
            return opacity == 1f ? color : color * opacity;
        }

        //==================== 预处理 ====================

        /// <summary>解析弧长窗口；窗口为空时返回 false</summary>
        internal static bool ResolveWindow(StrokeStyle style, out float lo, out float hi) {
            lo = MathHelper.Clamp(MathF.Min(style.From, style.To), 0f, 1f);
            hi = MathHelper.Clamp(MathF.Max(style.From, style.To), 0f, 1f);
            return style.IsFullWindow || hi - lo > 1e-6f;
        }

        /// <summary>
        /// 把路径的第 <paramref name="s"/> 条子路径变换到输出空间、套弧长窗口、去重、细分、切片
        /// <br/>返回 <see cref="PrepareResult.Dot"/> 时结果在 <see cref="WorkPoints"/>[0]；返回 <see cref="PrepareResult.Polyline"/> 时逐片 <see cref="LoadPiece"/>
        /// </summary>
        internal static PrepareResult PrepareSubPath(VectorPath path, int s, StrokeStyle style, in VectorTransform transform, bool fullWindow, float lo, float hi) {
            curTransform = transform;
            VectorSubPath sub = path.SubPaths[s];
            int n = sub.Points.Length;
            if (n == 0) {
                return PrepareResult.None;
            }
            float gStart = path.SubPathStart(s);
            float gEnd = path.SubPathEnd(s);
            if (!fullWindow && (gEnd < lo || gStart > hi)) {
                return PrepareResult.None;
            }
            if (sub.SegmentCount == 0) {
                SetDot(transform.Apply(sub.Points[0]), gStart);
                return PrepareResult.Dot;
            }
            bool closed = sub.Closed;
            bool byIndex = style.Parameterization == StrokeParameterization.PointIndex;
            srcPts.Clear();
            srcTs.Clear();
            srcArcs.Clear();
            float subLen = sub.Length;
            int count = closed ? n + 1 : n;
            float acc = 0f;
            Vector2 prev = default;
            for (int i = 0; i < count; i++) {
                Vector2 p = transform.Apply(sub.Points[i % n]);
                if (i > 0) {
                    acc += Vector2.Distance(prev, p);
                }
                prev = p;
                float f;
                if (byIndex) {
                    f = count > 1 ? i / (float)(count - 1) : 0f;
                }
                else {
                    f = subLen > 0f ? sub.Arcs[i] / subLen : 0f;
                }
                srcPts.Add(p);
                srcTs.Add(gStart + (gEnd - gStart) * f);
                srcArcs.Add(acc);
            }
            return Prepare(style, closed, fullWindow, lo, hi);
        }

        /// <summary>
        /// 把一条输出空间点列套弧长窗口、去重、细分、切片，t 按点列自身归一
        /// </summary>
        internal static PrepareResult PrepareSpan(ReadOnlySpan<Vector2> points, bool closed, StrokeStyle style, bool fullWindow, float lo, float hi) {
            curTransform = VectorTransform.Identity;
            int n = points.Length;
            if (n == 0) {
                return PrepareResult.None;
            }
            closed &= n >= 3;
            if (n == 1) {
                SetDot(points[0], 0f);
                return PrepareResult.Dot;
            }
            srcPts.Clear();
            srcTs.Clear();
            srcArcs.Clear();
            int count = closed ? n + 1 : n;
            float acc = 0f;
            for (int i = 0; i < count; i++) {
                Vector2 p = points[i % n];
                if (i > 0) {
                    acc += Vector2.Distance(points[(i - 1) % n], p);
                }
                srcPts.Add(p);
                srcArcs.Add(acc);
            }
            bool byIndex = style.Parameterization == StrokeParameterization.PointIndex;
            for (int i = 0; i < count; i++) {
                float f = byIndex
                    ? (count > 1 ? i / (float)(count - 1) : 0f)
                    : (acc > 0f ? srcArcs[i] / acc : 0f);
                srcTs.Add(f);
            }
            return Prepare(style, closed, fullWindow, lo, hi);
        }

        /// <summary>把第 <paramref name="index"/> 片装入 <see cref="WorkPoints"/> / <see cref="WorkTs"/>，并算出这一片两端应套的端帽</summary>
        internal static void LoadPiece(int index, StrokeStyle style, out LineCap startCap, out LineCap endCap) {
            Piece piece = pieces[index];
            pts.Clear();
            ts.Clear();
            arcs.Clear();
            for (int i = 0; i < piece.Count; i++) {
                pts.Add(allPts[piece.Start + i]);
                ts.Add(allTs[piece.Start + i]);
                arcs.Add(allArcs[piece.Start + i]);
            }
            workClosed = piece.Closed;
            startCap = piece.PathStart || style.DashCaps ? style.StartCap : LineCap.Butt;
            endCap = piece.PathEnd || style.DashCaps ? style.EndCap : LineCap.Butt;
        }

        private static void SetDot(Vector2 p, float t) {
            pts.Clear();
            ts.Clear();
            arcs.Clear();
            pts.Add(p);
            ts.Add(t);
            arcs.Add(0f);
            pieces.Clear();
            workClosed = false;
        }

        //源折线 → 裁剪窗口 → 去重 → 细分 → 切片
        private static PrepareResult Prepare(StrokeStyle style, bool closed, bool fullWindow, float lo, float hi) {
            midPts.Clear();
            midTs.Clear();
            midArcs.Clear();
            bool emitClosed = closed;
            closeT = srcTs[^1];
            closeArc = srcArcs[^1];
            if (fullWindow) {
                int count = closed ? srcPts.Count - 1 : srcPts.Count;
                for (int i = 0; i < count; i++) {
                    AddDedupe(srcPts[i], srcTs[i], srcArcs[i]);
                }
            }
            else {
                emitClosed = false;
                for (int i = 1; i < srcPts.Count; i++) {
                    float ta = srcTs[i - 1];
                    float tb = srcTs[i];
                    if (tb <= ta || tb <= lo || ta >= hi) {
                        continue;
                    }
                    float inv = 1f / (tb - ta);
                    if (midPts.Count == 0) {
                        float fa = ta < lo ? (lo - ta) * inv : 0f;
                        AddDedupe(Vector2.Lerp(srcPts[i - 1], srcPts[i], fa), MathF.Max(ta, lo), MathHelper.Lerp(srcArcs[i - 1], srcArcs[i], fa));
                    }
                    float fb = tb > hi ? (hi - ta) * inv : 1f;
                    AddDedupe(Vector2.Lerp(srcPts[i - 1], srcPts[i], fb), MathF.Min(tb, hi), MathHelper.Lerp(srcArcs[i - 1], srcArcs[i], fb));
                }
            }
            //闭合时末点若与首点重合则去掉
            if (emitClosed && midPts.Count > 1 && Vector2.DistanceSquared(midPts[^1], midPts[0]) < MinSegmentLength * MinSegmentLength) {
                midPts.RemoveAt(midPts.Count - 1);
                midTs.RemoveAt(midTs.Count - 1);
                midArcs.RemoveAt(midArcs.Count - 1);
            }
            if (emitClosed && midPts.Count < 3) {
                emitClosed = false;
            }
            if (midPts.Count == 0) {
                pieces.Clear();
                return PrepareResult.None;
            }
            if (midPts.Count == 1) {
                SetDot(midPts[0], midTs[0]);
                return PrepareResult.Dot;
            }
            if (style.MaxSegmentLength > 0f) {
                Subdivide(style.MaxSegmentLength, emitClosed);
            }

            allPts.Clear();
            allTs.Clear();
            allArcs.Clear();
            pieces.Clear();
            if (style.IsDashed) {
                SliceDashes(style, emitClosed);
            }
            else {
                allPts.AddRange(midPts);
                allTs.AddRange(midTs);
                allArcs.AddRange(midArcs);
                pieces.Add(new Piece { Start = 0, Count = midPts.Count, Closed = emitClosed, PathStart = true, PathEnd = true });
            }
            return pieces.Count == 0 ? PrepareResult.None : PrepareResult.Polyline;
        }

        private static void AddDedupe(Vector2 p, float t, float arc) {
            if (midPts.Count > 0 && Vector2.DistanceSquared(midPts[^1], p) < MinSegmentLength * MinSegmentLength) {
                //重合点：保留位置，更新参数到较新的值
                midTs[^1] = t;
                midArcs[^1] = arc;
                return;
            }
            midPts.Add(p);
            midTs.Add(t);
            midArcs.Add(arc);
        }

        //把超过 maxLen 的段等分插点（闭合时也处理回合段：插到末尾）
        private static void Subdivide(float maxLen, bool closed) {
            int n = midPts.Count;
            int segCount = closed ? n : n - 1;
            allPts.Clear();
            allTs.Clear();
            allArcs.Clear();
            for (int i = 0; i < segCount; i++) {
                Vector2 a = midPts[i];
                bool last = i == n - 1;
                Vector2 b = last ? midPts[0] : midPts[i + 1];
                float ta = midTs[i];
                float tb = last ? closeT : midTs[i + 1];
                float arcA = midArcs[i];
                float arcB = last ? closeArc : midArcs[i + 1];
                allPts.Add(a);
                allTs.Add(ta);
                allArcs.Add(arcA);
                float len = Vector2.Distance(a, b);
                int parts = (int)MathF.Ceiling(len / maxLen);
                for (int k = 1; k < parts; k++) {
                    float f = k / (float)parts;
                    allPts.Add(Vector2.Lerp(a, b, f));
                    allTs.Add(MathHelper.Lerp(ta, tb, f));
                    allArcs.Add(MathHelper.Lerp(arcA, arcB, f));
                }
            }
            if (!closed) {
                allPts.Add(midPts[n - 1]);
                allTs.Add(midTs[n - 1]);
                allArcs.Add(midArcs[n - 1]);
            }
            midPts.Clear();
            midTs.Clear();
            midArcs.Clear();
            midPts.AddRange(allPts);
            midTs.AddRange(allTs);
            midArcs.AddRange(allArcs);
        }

        //按弧长把折线切成若干实段（虚线）；闭合路径先把回合段接上再切，结果一律开放
        private static void SliceDashes(StrokeStyle style, bool closed) {
            float[] dash = style.Dash;
            int patLen = (dash.Length & 1) == 1 ? dash.Length * 2 : dash.Length;
            float total = style.DashTotal;
            if (closed) {
                midPts.Add(midPts[0]);
                midTs.Add(closeT);
                midArcs.Add(closeArc);
            }
            int n = midPts.Count;
            //起始相位
            float offset = style.DashOffset % total;
            if (offset < 0f) {
                offset += total;
            }
            int patIdx = 0;
            float remain = MathF.Max(dash[0], 0f);
            while (offset > 0f) {
                if (offset >= remain) {
                    offset -= remain;
                    patIdx = (patIdx + 1) % patLen;
                    remain = MathF.Max(dash[patIdx % dash.Length], 0f);
                    if (offset <= 0f) {
                        break;
                    }
                }
                else {
                    remain -= offset;
                    offset = 0f;
                }
            }
            bool on = (patIdx & 1) == 0;
            bool open = false;
            int pieceStart = 0;
            bool pathStart = true;

            void Begin(Vector2 p, float t, float arc, bool atPathStart) {
                pieceStart = allPts.Count;
                allPts.Add(p);
                allTs.Add(t);
                allArcs.Add(arc);
                open = true;
                pathStart = atPathStart;
            }
            void Add(Vector2 p, float t, float arc) {
                if (Vector2.DistanceSquared(allPts[^1], p) < MinSegmentLength * MinSegmentLength) {
                    allTs[^1] = t;
                    allArcs[^1] = arc;
                    return;
                }
                allPts.Add(p);
                allTs.Add(t);
                allArcs.Add(arc);
            }
            void Close(bool atPathEnd) {
                int count = allPts.Count - pieceStart;
                if (count >= 1) {
                    pieces.Add(new Piece { Start = pieceStart, Count = count, Closed = false, PathStart = pathStart, PathEnd = atPathEnd });
                }
                open = false;
            }

            if (on) {
                Begin(midPts[0], midTs[0], midArcs[0], true);
            }
            for (int i = 0; i < n - 1; i++) {
                Vector2 a = midPts[i];
                Vector2 b = midPts[i + 1];
                float ta = midTs[i];
                float tb = midTs[i + 1];
                float arcA = midArcs[i];
                float arcB = midArcs[i + 1];
                float segLen = arcB - arcA;
                if (segLen <= 0f) {
                    continue;
                }
                float s = 0f;
                while (s < segLen - 1e-6f) {
                    float step = MathF.Min(remain, segLen - s);
                    float s2 = s + step;
                    float f = s2 / segLen;
                    Vector2 p = Vector2.Lerp(a, b, f);
                    float t = MathHelper.Lerp(ta, tb, f);
                    float arc = MathHelper.Lerp(arcA, arcB, f);
                    if (on) {
                        if (!open) {
                            float f0 = s / segLen;
                            Begin(Vector2.Lerp(a, b, f0), MathHelper.Lerp(ta, tb, f0), MathHelper.Lerp(arcA, arcB, f0), false);
                        }
                        Add(p, t, arc);
                    }
                    s = s2;
                    remain -= step;
                    if (remain <= 1e-6f) {
                        if (on && open) {
                            Close(false);
                        }
                        on = !on;
                        patIdx = (patIdx + 1) % patLen;
                        remain = MathF.Max(dash[patIdx % dash.Length], 0f);
                        //零长的实段：留一个点，圆帽 / 方帽下会画成点
                        if (on && remain <= 1e-6f) {
                            Begin(p, t, arc, false);
                            Close(false);
                            on = false;
                            patIdx = (patIdx + 1) % patLen;
                            remain = MathF.Max(dash[patIdx % dash.Length], 0f);
                            if (remain <= 1e-6f) {
                                //连续零长：避免死循环，强制吃掉本段剩余
                                remain = MathF.Max(segLen - s, 1e-3f);
                            }
                        }
                        else if (on && !open) {
                            Begin(p, t, arc, false);
                        }
                    }
                }
            }
            if (open) {
                Close(true);
            }
            else if (pieces.Count > 0) {
                Piece last = pieces[^1];
                last.PathEnd = true;
                pieces[^1] = last;
            }
        }

        //==================== 生成 ====================

        private static void Emit(VectorMesh mesh, StrokeStyle style, bool closed, LineCap startCap, LineCap endCap) {
            int n = pts.Count;
            if (n == 0) {
                return;
            }
            if (n == 1) {
                EmitDot(mesh, style, pts[0], ts[0], startCap, endCap);
                return;
            }
            //闭合回合段的弧长
            float closeLen = closed ? Vector2.Distance(pts[n - 1], pts[0]) : 0f;
            float totalLen = arcs[n - 1] - arcs[0] + closeLen;
            if (totalLen < MinSegmentLength) {
                EmitDot(mesh, style, pts[0], ts[0], startCap, endCap);
                return;
            }
            ComputeTangents(closed);
            //方帽：端点沿切向外延半宽
            if (!closed) {
                if (startCap == LineCap.Square) {
                    pts[0] -= tangents[0] * (style.WidthAt(ts[0]) * 0.5f);
                }
                if (endCap == LineCap.Square) {
                    pts[n - 1] += tangents[n - 1] * (style.WidthAt(ts[n - 1]) * 0.5f);
                }
            }
            //闭合条带收尾那一对顶点的参数：回到起点处的 t 与弧长
            float endT = closed ? closeT : ts[n - 1];
            float endArc = closed ? closeArc : arcs[n - 1];

            int firstA, firstB, lastA, lastB;
            if (style.Join == LineJoin.Averaged || style.Join == LineJoin.Miter) {
                if (!EmitStrip(mesh, style, closed, endT, endArc, out firstA, out firstB, out lastA, out lastB)) {
                    return;
                }
            }
            else {
                if (!EmitSegments(mesh, style, closed, endT, endArc, out firstA, out firstB, out lastA, out lastB)) {
                    return;
                }
            }
            if (!closed) {
                Vector2 n0 = new(-tangents[0].Y, tangents[0].X);
                Vector2 n1 = new(-tangents[n - 1].Y, tangents[n - 1].X);
                EmitCap(mesh, style, startCap, pts[0], -tangents[0], n0, ts[0], arcs[0], firstA, firstB, false);
                EmitCap(mesh, style, endCap, pts[n - 1], tangents[n - 1], n1, ts[n - 1], arcs[n - 1], lastA, lastB, true);
            }
        }

        //每个点的切向：内部点取前后两段的平均，端点取所在段
        private static void ComputeTangents(bool closed) {
            tangents.Clear();
            int n = pts.Count;
            for (int i = 0; i < n; i++) {
                Vector2 din = Vector2.Zero;
                Vector2 dout = Vector2.Zero;
                if (i > 0) {
                    din = Direction(pts[i - 1], pts[i]);
                }
                else if (closed) {
                    din = Direction(pts[n - 1], pts[0]);
                }
                if (i < n - 1) {
                    dout = Direction(pts[i], pts[i + 1]);
                }
                else if (closed) {
                    dout = Direction(pts[n - 1], pts[0]);
                }
                Vector2 t = din + dout;
                if (t.LengthSquared() < 1e-6f) {
                    //前后反向折死：退回单段方向
                    t = dout.LengthSquared() > 0f ? dout : din;
                    if (t.LengthSquared() < 1e-12f) {
                        t = Vector2.UnitX;
                    }
                }
                t.Normalize();
                tangents.Add(t);
            }
        }

        private static Vector2 Direction(Vector2 from, Vector2 to) {
            Vector2 d = to - from;
            float len = d.Length();
            return len > 1e-9f ? d / len : Vector2.Zero;
        }

        private static float U(StrokeStyle style, float t, float arc) {
            float u = style.UvMode == StrokeUvMode.Tile ? arc / MathF.Max(style.TileLength, 1e-3f) : t;
            return u + style.UvOffset;
        }

        //连续条带（平均 / 尖接）：半宽沿平均法线放大 min(1 / cos(转角 / 2), 上限) 以保持厚度
        private static bool EmitStrip(VectorMesh mesh, StrokeStyle style, bool closed, float endT, float endArc,
            out int firstA, out int firstB, out int lastA, out int lastB) {
            firstA = firstB = lastA = lastB = -1;
            int n = pts.Count;
            int pairs = closed ? n + 1 : n;
            if (!mesh.Reserve(pairs * 2, (pairs - 1) * 6)) {
                return false;
            }
            float v0 = style.FlipV ? 1f : 0f;
            float v1 = 1f - v0;
            float limit = style.Join == LineJoin.Miter ? MathF.Max(style.MiterLimit, 1f) : MathF.Max(style.AveragedLimit, 1f);
            sideA.Clear();
            sideB.Clear();
            for (int k = 0; k < pairs; k++) {
                int i = k % n;
                Vector2 p = pts[i];
                Vector2 tg = tangents[i];
                Vector2 nrm = new(-tg.Y, tg.X);
                float t = k == n ? endT : ts[i];
                float arc = k == n ? endArc : arcs[i];
                float hw = style.WidthAt(t) * 0.5f;
                if (closed || (i > 0 && i < n - 1)) {
                    //平均法线与入段法线的夹角余弦 = cos(转角/2)，保持厚度需要放大 1 / cos
                    Vector2 din = i > 0 ? Direction(pts[i - 1], pts[i]) : Direction(pts[n - 1], pts[0]);
                    Vector2 nin = new(-din.Y, din.X);
                    float c = Vector2.Dot(nrm, nin);
                    if (c > 1e-3f) {
                        hw *= MathF.Min(1f / c, limit);
                    }
                    else {
                        hw *= limit;
                    }
                }
                float u = U(style, t, arc);
                Vector2 off = nrm * hw;
                Vector2 pa = p + off;
                Vector2 pb = p - off;
                int a = mesh.AddVertex(pa, Col(style, t, 0f, pa), new Vector2(u, v0));
                int b = mesh.AddVertex(pb, Col(style, t, 1f, pb), new Vector2(u, v1));
                sideA.Add(a);
                sideB.Add(b);
            }
            for (int k = 0; k < pairs - 1; k++) {
                mesh.AddTriangle(sideA[k], sideB[k], sideA[k + 1]);
                mesh.AddTriangle(sideA[k + 1], sideB[k], sideB[k + 1]);
            }
            firstA = sideA[0];
            firstB = sideB[0];
            lastA = sideA[n - 1];
            lastB = sideB[n - 1];
            return true;
        }

        //逐段四边形 + 外角补片（圆接 / 斜接）
        private static bool EmitSegments(VectorMesh mesh, StrokeStyle style, bool closed, float endT, float endArc,
            out int firstA, out int firstB, out int lastA, out int lastB) {
            firstA = firstB = lastA = lastB = -1;
            int n = pts.Count;
            int segCount = closed ? n : n - 1;
            if (!mesh.Reserve(segCount * 4, segCount * 6)) {
                return false;
            }
            float v0 = style.FlipV ? 1f : 0f;
            float v1 = 1f - v0;
            for (int s = 0; s < segCount; s++) {
                int i0 = s;
                int i1 = (s + 1) % n;
                Vector2 p0 = pts[i0];
                Vector2 p1 = pts[i1];
                Vector2 d = Direction(p0, p1);
                Vector2 nrm = new(-d.Y, d.X);
                float t0 = ts[i0];
                float t1 = closed && s == segCount - 1 ? endT : ts[i1];
                float arc0 = arcs[i0];
                float arc1 = closed && s == segCount - 1 ? endArc : arcs[i1];
                float hw0 = style.WidthAt(t0) * 0.5f;
                float hw1 = style.WidthAt(t1) * 0.5f;
                float u0 = U(style, t0, arc0);
                float u1 = U(style, t1, arc1);
                Vector2 a0p = p0 + nrm * hw0;
                Vector2 b0p = p0 - nrm * hw0;
                Vector2 a1p = p1 + nrm * hw1;
                Vector2 b1p = p1 - nrm * hw1;
                int a0 = mesh.AddVertex(a0p, Col(style, t0, 0f, a0p), new Vector2(u0, v0));
                int b0 = mesh.AddVertex(b0p, Col(style, t0, 1f, b0p), new Vector2(u0, v1));
                int a1 = mesh.AddVertex(a1p, Col(style, t1, 0f, a1p), new Vector2(u1, v0));
                int b1 = mesh.AddVertex(b1p, Col(style, t1, 1f, b1p), new Vector2(u1, v1));
                mesh.AddTriangle(a0, b0, a1);
                mesh.AddTriangle(a1, b0, b1);
                if (s == 0) {
                    firstA = a0;
                    firstB = b0;
                }
                lastA = a1;
                lastB = b1;
            }
            //外角补片
            int jStart = closed ? 0 : 1;
            int jEnd = closed ? n : n - 1;
            for (int i = jStart; i < jEnd; i++) {
                Vector2 din = Direction(pts[(i - 1 + n) % n], pts[i]);
                Vector2 dout = Direction(pts[i], pts[(i + 1) % n]);
                float cross = din.X * dout.Y - din.Y * dout.X;
                float dot = Vector2.Dot(din, dout);
                if (MathF.Abs(cross) < StraightCross && dot > 0f) {
                    continue;
                }
                //转向所指的一侧是内侧，补片落在另一侧
                float side = cross > 0f ? -1f : 1f;
                Vector2 nin = new Vector2(-din.Y, din.X) * side;
                Vector2 nout = new Vector2(-dout.Y, dout.X) * side;
                float t = ts[i];
                float hw = style.WidthAt(t) * 0.5f;
                if (hw <= 0f) {
                    continue;
                }
                float u = U(style, t, arcs[i]);
                float vOuter = side > 0f ? v0 : v1;
                float sideOuter = side > 0f ? 0f : 1f;
                Vector2 p = pts[i];
                Color cCenter = Col(style, t, 0.5f, p);
                if (style.Join == LineJoin.Bevel) {
                    if (!mesh.Reserve(3, 3)) {
                        return true;
                    }
                    Vector2 pa = p + nin * hw;
                    Vector2 pb = p + nout * hw;
                    int c = mesh.AddVertex(p, cCenter, new Vector2(u, 0.5f));
                    int a = mesh.AddVertex(pa, Col(style, t, sideOuter, pa), new Vector2(u, vOuter));
                    int b = mesh.AddVertex(pb, Col(style, t, sideOuter, pb), new Vector2(u, vOuter));
                    mesh.AddTriangle(c, a, b);
                }
                else {
                    float ang = MathF.Atan2(nin.X * nout.Y - nin.Y * nout.X, Vector2.Dot(nin, nout));
                    int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(ang) / roundStep));
                    if (!mesh.Reserve(steps + 2, steps * 3)) {
                        return true;
                    }
                    int c = mesh.AddVertex(p, cCenter, new Vector2(u, 0.5f));
                    Vector2 first = p + nin * hw;
                    int prev = mesh.AddVertex(first, Col(style, t, sideOuter, first), new Vector2(u, vOuter));
                    for (int k = 1; k <= steps; k++) {
                        Vector2 dir = Rotate(nin, ang * k / steps);
                        Vector2 q = p + dir * hw;
                        int cur = mesh.AddVertex(q, Col(style, t, sideOuter, q), new Vector2(u, vOuter));
                        mesh.AddTriangle(c, prev, cur);
                        prev = cur;
                    }
                }
            }
            return true;
        }

        //贴图端帽：只收集印章，几何按 Butt 处理，由调用方（一步式 DrawStroke / VectorBatch）另行提交
        //叠印的每一次各占一枚印章（即一个四边形），仍落在同一块端帽网格里
        private static void AddCapStamp(StrokeStyle style, Vector2 p, Vector2 outward, float t, bool atEnd) {
            if (style.CapTexture == null) {
                return;
            }
            float w = style.WidthAt(t);
            if (w <= 0f) {
                return;
            }
            Color color = style.CapColor ?? Col(style, t, 0.5f, p);
            Vector2 size = new Vector2(w) * style.CapScaleAt(atEnd);
            style.ResolveCapRepeat(out int repeat, out float scale);
            for (int k = 0; k < repeat; k++) {
                capStamps.Add(new CapStamp {
                    Position = p,
                    Outward = outward,
                    Size = size,
                    Color = color,
                });
                size *= scale;
            }
        }

        //开放路径端帽；outward 指向路径之外，nrm 为条带在该端的法线（idxA 位于 p + nrm * 半宽），atEnd 区分这一片的起点 / 终点
        private static void EmitCap(VectorMesh mesh, StrokeStyle style, LineCap cap, Vector2 p, Vector2 outward, Vector2 nrm, float t, float arc, int idxA, int idxB, bool atEnd) {
            if (cap == LineCap.Texture) {
                AddCapStamp(style, p, outward, t, atEnd);
                return;
            }
            if (cap == LineCap.Butt || cap == LineCap.Square || idxA < 0 || idxB < 0) {
                return;
            }
            float hw = style.WidthAt(t) * 0.5f;
            if (hw <= 0f) {
                return;
            }
            float u = U(style, t, arc);
            if (cap == LineCap.Arrow) {
                float len = style.CapLength > 0f ? style.CapLength : hw * 2f;
                if (!mesh.Reserve(1, 3)) {
                    return;
                }
                Vector2 tipPos = p + outward * len;
                int tip = mesh.AddVertex(tipPos, Col(style, t, 0.5f, tipPos), new Vector2(u, 0.5f));
                mesh.AddTriangle(idxA, idxB, tip);
                return;
            }
            //圆帽：从法线正侧经 outward 扫到负侧的半圆扇；nrm 与 outward 垂直，按叉积决定绕向
            float sign = nrm.X * outward.Y - nrm.Y * outward.X > 0f ? 1f : -1f;
            int steps = Math.Max(2, (int)MathF.Ceiling(MathHelper.Pi / roundStep));
            if (!mesh.Reserve(steps, steps * 3)) {
                return;
            }
            float v0 = style.FlipV ? 1f : 0f;
            float v1 = 1f - v0;
            int center = mesh.AddVertex(p, Col(style, t, 0.5f, p), new Vector2(u, 0.5f));
            int prev = idxA;
            for (int k = 1; k < steps; k++) {
                float f = k / (float)steps;
                Vector2 dir = Rotate(nrm, sign * MathHelper.Pi * f);
                float v = MathHelper.Lerp(v0, v1, f);
                Vector2 q = p + dir * hw;
                int cur = mesh.AddVertex(q, Col(style, t, f, q), new Vector2(u, v));
                mesh.AddTriangle(center, prev, cur);
                prev = cur;
            }
            mesh.AddTriangle(center, prev, idxB);
        }

        //退化为一点：圆帽画圆片，方帽画方片，其余不画
        private static void EmitDot(VectorMesh mesh, StrokeStyle style, Vector2 p, float t, LineCap startCap, LineCap endCap) {
            //两端重合到一点，贴图端帽只印一次、朝向取 +X、尺寸取 CapScale
            if (startCap == LineCap.Texture || endCap == LineCap.Texture) {
                AddCapStamp(style, p, Vector2.UnitX, t, false);
            }
            bool round = startCap == LineCap.Round || endCap == LineCap.Round;
            bool square = startCap == LineCap.Square || endCap == LineCap.Square;
            if (!round && !square) {
                return;
            }
            float hw = style.WidthAt(t) * 0.5f;
            if (hw <= 0f) {
                return;
            }
            float u = U(style, t, 0f);
            Color color = Col(style, t, 0.5f, p);
            if (square) {
                if (!mesh.Reserve(4, 6)) {
                    return;
                }
                int a = mesh.AddVertex(p + new Vector2(-hw, -hw), color, new Vector2(u, 0f));
                int b = mesh.AddVertex(p + new Vector2(hw, -hw), color, new Vector2(u, 0f));
                int c = mesh.AddVertex(p + new Vector2(hw, hw), color, new Vector2(u, 1f));
                int d = mesh.AddVertex(p + new Vector2(-hw, hw), color, new Vector2(u, 1f));
                mesh.AddTriangle(a, b, c);
                mesh.AddTriangle(a, c, d);
                return;
            }
            int steps = Math.Max(6, (int)MathF.Ceiling(MathHelper.TwoPi / roundStep));
            if (!mesh.Reserve(steps + 1, steps * 3)) {
                return;
            }
            int center = mesh.AddVertex(p, color, new Vector2(u, 0.5f));
            int first = -1;
            int prev = -1;
            for (int k = 0; k < steps; k++) {
                float a = MathHelper.TwoPi * k / steps;
                int cur = mesh.AddVertex(p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * hw, color, new Vector2(u, 0.5f + 0.5f * MathF.Sin(a)));
                if (k == 0) {
                    first = cur;
                }
                else {
                    mesh.AddTriangle(center, prev, cur);
                }
                prev = cur;
            }
            mesh.AddTriangle(center, prev, first);
        }

        private static Vector2 Rotate(Vector2 v, float angle) {
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }
    }
}
