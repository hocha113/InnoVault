using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors.Tessellation
{
    /// <summary>
    /// 多边形 → 三角网格：
    /// <br/>单环走凸扇形 / 耳切；多环按 <see cref="FillRule"/> 建包含树判孔，孔用 Eberly 桥接法并入外环后耳切；不支持自交（自交会退回扇形保证不死循环）
    /// <br/>路径填充的三角剖分对仿射变换不变，结果由 <see cref="VectorPath.TryGetFillMesh"/> 缓存；本类的暂存缓冲为静态复用，只允许在渲染线程调用
    /// </summary>
    internal static class FillTessellator
    {
        private static readonly List<Vector2> local = new(128);
        private static readonly List<Vector2> world = new(128);
        private static readonly List<int> ring = new(128);
        private static readonly List<int> tris = new(384);
        private static readonly List<Vector2> merged = new(256);

        //建包含树用的环记录
        private sealed class Ring
        {
            public Vector2[] Points;
            public float Area;
            public int Depth;
            public int WindingOutside;
            public bool IsOuter;
            public bool IsHole;
            public int ParentOuter = -1;
            public readonly List<int> Holes = [];
        }

        /// <summary>填充路径（走 <see cref="VectorPath.TryGetFillMesh"/> 的缓存网格；有位置求色且设了 <see cref="FillStyle.MaxTriangleEdge"/> 时先均匀细分）</summary>
        public static void Append(VectorMesh mesh, VectorPath path, FillStyle style, in VectorTransform transform) {
            if (!path.TryGetFillMesh(style.Rule, out Vector2[] verts, out int[] triangles)) {
                return;
            }
            Vector2 min = path.BoundsMin;
            Vector2 size = path.BoundsSize;
            float invW = size.X > 1e-6f ? 1f / size.X : 0f;
            float invH = size.Y > 1e-6f ? 1f / size.Y : 0f;

            int level = 0;
            if (style.MaxTriangleEdge > 0f && style.UsesPositionColor) {
                //按输出空间最长边决定细分级数，整网格同级细分保证共享边一致、无裂缝
                float longest = 0f;
                for (int i = 0; i + 2 < triangles.Length; i += 3) {
                    Vector2 a = transform.Apply(verts[triangles[i]]);
                    Vector2 b = transform.Apply(verts[triangles[i + 1]]);
                    Vector2 c = transform.Apply(verts[triangles[i + 2]]);
                    longest = MathF.Max(longest, MathF.Max(Vector2.DistanceSquared(a, b), MathF.Max(Vector2.DistanceSquared(b, c), Vector2.DistanceSquared(c, a))));
                }
                longest = MathF.Sqrt(longest);
                while (level < 3 && longest > style.MaxTriangleEdge) {
                    longest *= 0.5f;
                    level++;
                }
            }
            if (level == 0) {
                if (!mesh.Reserve(verts.Length, triangles.Length)) {
                    return;
                }
                int baseIndex = mesh.VertexCount;
                for (int i = 0; i < verts.Length; i++) {
                    Vector2 lp = verts[i];
                    Vector2 wp = transform.Apply(lp);
                    mesh.AddVertex(wp, style.ColorAt(lp, wp), new Vector2((lp.X - min.X) * invW, (lp.Y - min.Y) * invH));
                }
                for (int i = 0; i < triangles.Length; i += 3) {
                    mesh.AddTriangle(baseIndex + triangles[i], baseIndex + triangles[i + 1], baseIndex + triangles[i + 2]);
                }
                return;
            }
            //细分：中点按顶点对共享
            refVerts.Clear();
            refVerts.AddRange(verts);
            refTris.Clear();
            refTris.AddRange(triangles);
            for (int l = 0; l < level; l++) {
                midCache.Clear();
                refNext.Clear();
                int count = refTris.Count;
                for (int i = 0; i + 2 < count; i += 3) {
                    int a = refTris[i];
                    int b = refTris[i + 1];
                    int c = refTris[i + 2];
                    int ab = Midpoint(a, b);
                    int bc = Midpoint(b, c);
                    int ca = Midpoint(c, a);
                    refNext.Add(a); refNext.Add(ab); refNext.Add(ca);
                    refNext.Add(ab); refNext.Add(b); refNext.Add(bc);
                    refNext.Add(ca); refNext.Add(bc); refNext.Add(c);
                    refNext.Add(ab); refNext.Add(bc); refNext.Add(ca);
                }
                (refTris, refNext) = (refNext, refTris);
                if (refVerts.Count > VectorMesh.MaxVertices) {
                    break;
                }
            }
            if (!mesh.Reserve(refVerts.Count, refTris.Count)) {
                return;
            }
            int baseIdx = mesh.VertexCount;
            for (int i = 0; i < refVerts.Count; i++) {
                Vector2 lp = refVerts[i];
                Vector2 wp = transform.Apply(lp);
                mesh.AddVertex(wp, style.ColorAt(lp, wp), new Vector2((lp.X - min.X) * invW, (lp.Y - min.Y) * invH));
            }
            for (int i = 0; i + 2 < refTris.Count; i += 3) {
                mesh.AddTriangle(baseIdx + refTris[i], baseIdx + refTris[i + 1], baseIdx + refTris[i + 2]);
            }
        }

        private static readonly List<Vector2> refVerts = new(512);
        private static List<int> refTris = new(1536);
        private static List<int> refNext = new(1536);
        private static readonly Dictionary<long, int> midCache = new();

        private static int Midpoint(int a, int b) {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (midCache.TryGetValue(key, out int m)) {
                return m;
            }
            m = refVerts.Count;
            refVerts.Add((refVerts[a] + refVerts[b]) * 0.5f);
            midCache[key] = m;
            return m;
        }

        /// <summary>填充一个输出空间的简单多边形（路径空间即输出空间，不缓存）</summary>
        public static void Append(VectorMesh mesh, ReadOnlySpan<Vector2> polygon, FillStyle style) {
            local.Clear();
            world.Clear();
            for (int i = 0; i < polygon.Length; i++) {
                if (i == polygon.Length - 1 && Vector2.DistanceSquared(polygon[i], polygon[0]) < 1e-10f) {
                    break;
                }
                local.Add(polygon[i]);
                world.Add(polygon[i]);
            }
            int n = local.Count;
            if (n < 3 || !Triangulate(world, tris)) {
                return;
            }
            //凸形三角化会追加质心顶点，local 同步
            while (local.Count < world.Count) {
                local.Add(world[local.Count]);
            }
            Vector2 min = new(float.MaxValue);
            Vector2 max = new(float.MinValue);
            for (int i = 0; i < n; i++) {
                min = Vector2.Min(min, local[i]);
                max = Vector2.Max(max, local[i]);
            }
            n = local.Count;
            if (!mesh.Reserve(n, tris.Count)) {
                return;
            }
            Vector2 size = max - min;
            float invW = size.X > 1e-6f ? 1f / size.X : 0f;
            float invH = size.Y > 1e-6f ? 1f / size.Y : 0f;
            int baseIndex = mesh.VertexCount;
            for (int i = 0; i < n; i++) {
                Vector2 lp = local[i];
                mesh.AddVertex(world[i], style.ColorAt(lp, world[i]), new Vector2((lp.X - min.X) * invW, (lp.Y - min.Y) * invH));
            }
            for (int i = 0; i < tris.Count; i += 3) {
                mesh.AddTriangle(baseIndex + tris[i], baseIndex + tris[i + 1], baseIndex + tris[i + 2]);
            }
        }

        //==================== 路径 → 填充网格（供 VectorPath 缓存） ====================

        /// <summary>
        /// 把路径的全部闭合环按 <paramref name="rule"/> 分类成外环与孔，桥接后三角化；输出路径空间顶点（含桥接重复点）与索引三元组
        /// </summary>
        internal static void BuildFillMesh(VectorPath path, FillRule rule, out Vector2[] vertices, out int[] triangles) {
            vertices = [];
            triangles = [];
            List<Ring> rings = [];
            for (int s = 0; s < path.SubPathCount; s++) {
                Vector2[] pts = path.SubPaths[s].Points;
                int n = pts.Length;
                if (n >= 2 && Vector2.DistanceSquared(pts[n - 1], pts[0]) < 1e-10f) {
                    n--;
                }
                if (n < 3) {
                    continue;
                }
                Vector2[] copy = new Vector2[n];
                Array.Copy(pts, copy, n);
                float area = SignedArea(copy);
                if (MathF.Abs(area) < 1e-8f) {
                    continue;
                }
                rings.Add(new Ring { Points = copy, Area = area });
            }
            if (rings.Count == 0) {
                return;
            }

            //包含关系：用环的一个顶点做点内测试
            for (int i = 0; i < rings.Count; i++) {
                Ring ri = rings[i];
                Vector2 probe = ri.Points[0];
                for (int j = 0; j < rings.Count; j++) {
                    if (i == j) {
                        continue;
                    }
                    if (PointInPolygon(probe, rings[j].Points)) {
                        ri.Depth++;
                        ri.WindingOutside += rings[j].Area > 0f ? 1 : -1;
                    }
                }
            }
            for (int i = 0; i < rings.Count; i++) {
                Ring r = rings[i];
                if (rule == FillRule.EvenOdd) {
                    r.IsOuter = (r.Depth & 1) == 0;
                    r.IsHole = !r.IsOuter;
                }
                else {
                    int inside = r.WindingOutside + (r.Area > 0f ? 1 : -1);
                    r.IsOuter = inside != 0 && r.WindingOutside == 0;
                    r.IsHole = inside == 0 && r.WindingOutside != 0;
                }
            }
            //孔挂到包含它的最深外环上
            for (int i = 0; i < rings.Count; i++) {
                Ring hole = rings[i];
                if (!hole.IsHole) {
                    continue;
                }
                int best = -1;
                int bestDepth = -1;
                for (int j = 0; j < rings.Count; j++) {
                    Ring outer = rings[j];
                    if (i == j || !outer.IsOuter || outer.Depth >= hole.Depth || outer.Depth <= bestDepth) {
                        continue;
                    }
                    if (PointInPolygon(hole.Points[0], outer.Points)) {
                        best = j;
                        bestDepth = outer.Depth;
                    }
                }
                if (best >= 0) {
                    hole.ParentOuter = best;
                    rings[best].Holes.Add(i);
                }
            }

            List<Vector2> outVerts = [];
            List<int> outTris = [];
            for (int i = 0; i < rings.Count; i++) {
                Ring outer = rings[i];
                if (!outer.IsOuter) {
                    continue;
                }
                merged.Clear();
                //外环统一正向，孔统一反向
                AppendOriented(merged, outer.Points, outer.Area > 0f);
                if (outer.Holes.Count > 0) {
                    //按最大 x 顶点从右到左依次桥接
                    List<Ring> holes = new(outer.Holes.Count);
                    foreach (int h in outer.Holes) {
                        holes.Add(rings[h]);
                    }
                    holes.Sort((a, b) => MaxX(b.Points).CompareTo(MaxX(a.Points)));
                    foreach (Ring h in holes) {
                        BridgeHole(merged, h.Points, h.Area < 0f);
                    }
                }
                if (!Triangulate(merged, tris)) {
                    continue;
                }
                int baseIndex = outVerts.Count;
                outVerts.AddRange(merged);
                for (int k = 0; k < tris.Count; k++) {
                    outTris.Add(baseIndex + tris[k]);
                }
            }
            vertices = [.. outVerts];
            triangles = [.. outTris];
        }

        //把环按要求的方向追加到列表
        private static void AppendOriented(List<Vector2> into, Vector2[] pts, bool alreadyCorrect) {
            if (alreadyCorrect) {
                into.AddRange(pts);
                return;
            }
            for (int i = pts.Length - 1; i >= 0; i--) {
                into.Add(pts[i]);
            }
        }

        //Eberly 桥接：把一个孔并入正向外环，返回后 poly 是含桥接重复点的弱简单多边形
        private static void BridgeHole(List<Vector2> poly, Vector2[] holePts, bool holeAlreadyClockwise) {
            //孔按反向（负面积）排列
            Vector2[] hole;
            if (holeAlreadyClockwise) {
                hole = holePts;
            }
            else {
                hole = new Vector2[holePts.Length];
                for (int i = 0; i < holePts.Length; i++) {
                    hole[i] = holePts[holePts.Length - 1 - i];
                }
            }
            //孔的最大 x 顶点 M
            int mi = 0;
            for (int i = 1; i < hole.Length; i++) {
                if (hole[i].X > hole[mi].X) {
                    mi = i;
                }
            }
            Vector2 m = hole[mi];
            int n = poly.Count;
            //从 M 向 +x 发射线，找最近的相交边
            float bestX = float.MaxValue;
            int bestEdge = -1;
            Vector2 hit = default;
            for (int i = 0; i < n; i++) {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                if (a.X < m.X && b.X < m.X) {
                    continue;
                }
                if ((a.Y > m.Y) == (b.Y > m.Y)) {
                    continue;
                }
                float t = (m.Y - a.Y) / (b.Y - a.Y);
                float x = a.X + t * (b.X - a.X);
                if (x >= m.X && x < bestX) {
                    bestX = x;
                    bestEdge = i;
                    hit = new Vector2(x, m.Y);
                }
            }
            int pIndex;
            if (bestEdge < 0) {
                //没有相交（数值退化）：取离 M 最近的顶点
                pIndex = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < n; i++) {
                    float d = Vector2.DistanceSquared(poly[i], m);
                    if (d < bestD) {
                        bestD = d;
                        pIndex = i;
                    }
                }
            }
            else {
                Vector2 a = poly[bestEdge];
                Vector2 b = poly[(bestEdge + 1) % n];
                if (Vector2.DistanceSquared(hit, a) < 1e-10f) {
                    pIndex = bestEdge;
                }
                else if (Vector2.DistanceSquared(hit, b) < 1e-10f) {
                    pIndex = (bestEdge + 1) % n;
                }
                else {
                    //候选 P = 相交边上 x 更大的端点；若三角形 (M, hit, P) 内有凹顶点，改取与 +x 夹角最小者
                    pIndex = a.X > b.X ? bestEdge : (bestEdge + 1) % n;
                    Vector2 p = poly[pIndex];
                    float bestAngle = float.MaxValue;
                    float bestDist = float.MaxValue;
                    int replaced = -1;
                    for (int i = 0; i < n; i++) {
                        if (i == pIndex) {
                            continue;
                        }
                        Vector2 r = poly[i];
                        if (!IsReflex(poly, i) || !PointInTriangleInclusive(r, m, hit, p)) {
                            continue;
                        }
                        Vector2 d = r - m;
                        float angle = MathF.Abs(MathF.Atan2(d.Y, d.X));
                        float dist = d.LengthSquared();
                        if (angle < bestAngle - 1e-6f || (MathF.Abs(angle - bestAngle) <= 1e-6f && dist < bestDist)) {
                            bestAngle = angle;
                            bestDist = dist;
                            replaced = i;
                        }
                    }
                    if (replaced >= 0) {
                        pIndex = replaced;
                    }
                }
            }
            //拼接：poly[0..P] + hole(M 起绕一圈回到 M) + P + poly[P+1..]
            Vector2 pv = poly[pIndex];
            List<Vector2> insert = new(hole.Length + 2);
            for (int k = 0; k <= hole.Length; k++) {
                insert.Add(hole[(mi + k) % hole.Length]);
            }
            insert.Add(pv);
            poly.InsertRange(pIndex + 1, insert);
        }

        private static bool IsReflex(List<Vector2> poly, int i) {
            int n = poly.Count;
            Vector2 a = poly[(i - 1 + n) % n];
            Vector2 b = poly[i];
            Vector2 c = poly[(i + 1) % n];
            return Cross(b - a, c - b) < 0f;
        }

        private static float MaxX(Vector2[] pts) {
            float x = float.MinValue;
            for (int i = 0; i < pts.Length; i++) {
                x = MathF.Max(x, pts[i].X);
            }
            return x;
        }

        //==================== 三角剖分 ====================

        /// <summary>
        /// 对（弱）简单多边形做三角剖分，输出顶点索引三元组；面积退化时返回 false
        /// </summary>
        public static bool Triangulate(List<Vector2> poly, List<int> outTriangles) {
            outTriangles.Clear();
            int n = poly.Count;
            if (n < 3) {
                return false;
            }
            float area = SignedArea(poly);
            if (MathF.Abs(area) < 1e-8f) {
                return false;
            }
            //统一成正向环绕，耳切按正向判凸
            ring.Clear();
            if (area > 0f) {
                for (int i = 0; i < n; i++) {
                    ring.Add(i);
                }
            }
            else {
                for (int i = n - 1; i >= 0; i--) {
                    ring.Add(i);
                }
            }
            if (IsConvex(poly, ring)) {
                //凸形从质心出扇（质心追加为新顶点）：内部有一个采样点，圆的径向渐变能直接插值出来
                Vector2 centroid = Vector2.Zero;
                for (int i = 0; i < n; i++) {
                    centroid += poly[i];
                }
                centroid /= n;
                int ci = poly.Count;
                poly.Add(centroid);
                for (int i = 0; i < n; i++) {
                    outTriangles.Add(ci);
                    outTriangles.Add(ring[i]);
                    outTriangles.Add(ring[(i + 1) % n]);
                }
                return true;
            }
            //耳切：耳三角形内部不得有其他顶点（与耳顶点同位置的桥接重复点不算）
            int guard = 0;
            while (ring.Count > 3 && guard++ < n * n) {
                bool clipped = false;
                int m = ring.Count;
                for (int k = 0; k < m; k++) {
                    int ia = ring[(k - 1 + m) % m];
                    int ib = ring[k];
                    int ic = ring[(k + 1) % m];
                    Vector2 a = poly[ia];
                    Vector2 b = poly[ib];
                    Vector2 c = poly[ic];
                    float cross = Cross(b - a, c - b);
                    if (cross <= 1e-9f) {
                        //凹角或共线
                        continue;
                    }
                    bool empty = true;
                    for (int j = 0; j < m; j++) {
                        int ij = ring[j];
                        if (ij == ia || ij == ib || ij == ic) {
                            continue;
                        }
                        Vector2 q = poly[ij];
                        if (SamePoint(q, a) || SamePoint(q, b) || SamePoint(q, c)) {
                            continue;
                        }
                        if (PointInTriangleStrict(q, a, b, c)) {
                            empty = false;
                            break;
                        }
                    }
                    if (!empty) {
                        continue;
                    }
                    outTriangles.Add(ia);
                    outTriangles.Add(ib);
                    outTriangles.Add(ic);
                    ring.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) {
                    //找不到耳（自交或数值退化）：剩余部分退回扇形，保证有输出且不死循环
                    for (int i = 1; i < ring.Count - 1; i++) {
                        outTriangles.Add(ring[0]);
                        outTriangles.Add(ring[i]);
                        outTriangles.Add(ring[i + 1]);
                    }
                    return true;
                }
            }
            if (ring.Count == 3) {
                outTriangles.Add(ring[0]);
                outTriangles.Add(ring[1]);
                outTriangles.Add(ring[2]);
            }
            return true;
        }

        private static float SignedArea(List<Vector2> poly) {
            float a = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++) {
                Vector2 p = poly[i];
                Vector2 q = poly[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5f;
        }

        private static float SignedArea(Vector2[] poly) {
            float a = 0f;
            int n = poly.Length;
            for (int i = 0; i < n; i++) {
                Vector2 p = poly[i];
                Vector2 q = poly[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5f;
        }

        private static bool IsConvex(List<Vector2> poly, List<int> order) {
            int n = order.Count;
            for (int i = 0; i < n; i++) {
                Vector2 a = poly[order[i]];
                Vector2 b = poly[order[(i + 1) % n]];
                Vector2 c = poly[order[(i + 2) % n]];
                if (Cross(b - a, c - b) < -1e-6f) {
                    return false;
                }
            }
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static bool SamePoint(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) < 1e-12f;

        //严格在正向三角形内部（边界上不算）
        private static bool PointInTriangleStrict(Vector2 p, Vector2 a, Vector2 b, Vector2 c) {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            return d1 > 1e-9f && d2 > 1e-9f && d3 > 1e-9f;
        }

        //含边界，不限方向
        private static bool PointInTriangleInclusive(Vector2 p, Vector2 a, Vector2 b, Vector2 c) {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool neg = d1 < -1e-9f || d2 < -1e-9f || d3 < -1e-9f;
            bool pos = d1 > 1e-9f || d2 > 1e-9f || d3 > 1e-9f;
            return !(neg && pos);
        }

        //奇偶规则点内测试
        private static bool PointInPolygon(Vector2 p, Vector2[] poly) {
            bool inside = false;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++) {
                Vector2 a = poly[i];
                Vector2 b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y)) {
                    float x = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < x) {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }
    }
}
