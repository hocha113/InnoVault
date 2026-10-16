using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 网格件的加载期蒙皮：校验拓扑、解析纹理坐标、求权重（显式或谐和），压成运行时逐顶点蒙皮表
    /// <br/>谐和权重：在网格上解 Laplace 方程，每根影响骨的控制柄顶点钉 1、其余控制柄钉 0，自由顶点取边权加权平均——
    /// 余切边权（每个角的余切下限 1e-3，保证 M 矩阵从而权重落在 0..1），Jacobi 预条件共轭梯度求解。
    /// 同一连通块内全部控制柄的权重之和恒为 1；不连到控制柄的孤岛权重为 0、整块随宿主骨
    /// </summary>
    internal static class Mesh2DSkin
    {
        /// <summary>权重小于此值的条目丢弃</summary>
        private const float WeightEpsilon = 1e-4f;
        private const int MaxIterations = 4000;
        private const double Tolerance = 1e-7;

        /// <summary>
        /// 按绑定姿态解析一张网格；失败经 <paramref name="fail"/> 报告并返回 <see langword="false"/>（解析结果清空）
        /// </summary>
        public static bool Build(Mesh2DDef m, Bone2D[] bind, Action<string> fail) {
            Clear(m);
            string who = $"mesh '{m.DisplayName}'";
            float[] verts = m.Vertices ?? [];
            if (verts.Length < 6 || verts.Length % 2 != 0) {
                fail($"{who}: vertices must be x, y pairs (at least 3 vertices)");
                return false;
            }
            int n = verts.Length / 2;
            if (n > short.MaxValue) {
                fail($"{who}: {n} vertices exceed the 32767 limit");
                return false;
            }
            int[] tris = m.Triangles ?? [];
            if (tris.Length < 3 || tris.Length % 3 != 0) {
                fail($"{who}: triangles must be index triples");
                return false;
            }
            for (int i = 0; i < tris.Length; i++) {
                if (tris[i] < 0 || tris[i] >= n) {
                    fail($"{who}: triangle index {tris[i]} out of range (0..{n - 1})");
                    return false;
                }
            }
            if ((uint)m.BoneIndex >= (uint)bind.Length) {
                return false;
            }

            Bone2D host = bind[m.BoneIndex];
            Vector2[] local = new Vector2[n];
            Vector2[] pos = new Vector2[n];
            for (int v = 0; v < n; v++) {
                local[v] = new Vector2(verts[v * 2], verts[v * 2 + 1]);
                pos[v] = host.ToWorld(local[v]);
            }

            if (!BuildTexCoords(m, local, n, who, fail, out Vector2[] uv)) {
                return false;
            }

            int k = m.BoneIndices.Length;
            float[] weights = null;
            if (k > 0) {
                float[] given = m.Weights ?? [];
                if (given.Length > 0) {
                    if (given.Length != n * k) {
                        fail($"{who}: weights has {given.Length} values, expected vertices × bones = {n * k}");
                        return false;
                    }
                    weights = given;
                }
                else {
                    weights = Harmonic(pos, tris, bind, m.BoneIndices, MathF.Max(m.HandleRadius, 0f), who);
                }
            }

            //压缩蒙皮表：影响骨条目 + 宿主骨补足余量
            List<int> start = new(n + 1);
            List<int> bones = new(n * 2);
            List<float> ws = new(n * 2);
            List<Vector2> locals = new(n * 2);
            for (int v = 0; v < n; v++) {
                start.Add(bones.Count);
                float sum = 0f;
                int first = bones.Count;
                for (int i = 0; i < k; i++) {
                    float w = weights[v * k + i];
                    if (!(w > WeightEpsilon)) {
                        continue;
                    }
                    int b = m.BoneIndices[i];
                    bones.Add(b);
                    ws.Add(w);
                    locals.Add(bind[b].ToLocal(pos[v]));
                    sum += w;
                }
                if (sum > 1f) {
                    for (int e = first; e < ws.Count; e++) {
                        ws[e] /= sum;
                    }
                    sum = 1f;
                }
                float rest = 1f - sum;
                if (rest > WeightEpsilon || bones.Count == first) {
                    bones.Add(m.BoneIndex);
                    ws.Add(MathF.Max(rest, 0f));
                    locals.Add(local[v]);
                }
            }
            start.Add(bones.Count);

            short[] indices = new short[tris.Length];
            for (int i = 0; i < tris.Length; i++) {
                indices[i] = (short)tris[i];
            }
            m.VertexCount = n;
            m.Indices = indices;
            m.TexCoords = uv;
            m.BindPositions = pos;
            m.SkinStart = start.ToArray();
            m.SkinBone = bones.ToArray();
            m.SkinWeight = ws.ToArray();
            m.SkinLocal = locals.ToArray();
            return true;
        }

        private static void Clear(Mesh2DDef m) {
            m.VertexCount = 0;
            m.Indices = [];
            m.TexCoords = [];
            m.BindPositions = [];
            m.SkinStart = [];
            m.SkinBone = [];
            m.SkinWeight = [];
            m.SkinLocal = [];
        }

        private static bool BuildTexCoords(Mesh2DDef m, Vector2[] local, int n, string who, Action<string> fail, out Vector2[] uv) {
            uv = new Vector2[n];
            float[] given = m.Uvs ?? [];
            if (given.Length > 0) {
                if (given.Length != n * 2) {
                    fail($"{who}: uvs has {given.Length} values, expected {n * 2}");
                    return false;
                }
                for (int v = 0; v < n; v++) {
                    uv[v] = new Vector2(given[v * 2], given[v * 2 + 1]);
                }
                return true;
            }
            float x, y, w, h;
            float[] rect = m.UvRect ?? [];
            if (rect.Length >= 4) {
                x = rect[0];
                y = rect[1];
                w = rect[2];
                h = rect[3];
            }
            else {
                Vector2 min = new(float.MaxValue);
                Vector2 max = new(float.MinValue);
                for (int v = 0; v < n; v++) {
                    min = Vector2.Min(min, local[v]);
                    max = Vector2.Max(max, local[v]);
                }
                x = min.X;
                y = min.Y;
                w = max.X - min.X;
                h = max.Y - min.Y;
            }
            if (MathF.Abs(w) < 1e-4f || MathF.Abs(h) < 1e-4f) {
                fail($"{who}: uv rect is degenerate");
                return false;
            }
            for (int v = 0; v < n; v++) {
                uv[v] = new Vector2((local[v].X - x) / w, (local[v].Y - y) / h);
            }
            return true;
        }

        //==================== 谐和权重 ====================

        private static float[] Harmonic(Vector2[] pos, int[] tris, Bone2D[] bind, int[] influence, float radius, string who) {
            int n = pos.Length;
            int k = influence.Length;
            BuildLaplacian(pos, tris, out int[] rowStart, out int[] cols, out double[] vals, out double[] diag);

            //控制柄：离影响骨静息线段不超过半径的顶点；同一顶点被几根骨争抢时归最近的那根
            int[] handleOf = new int[n];
            float[] handleDist = new float[n];
            Array.Fill(handleOf, -1);
            Array.Fill(handleDist, float.MaxValue);
            for (int i = 0; i < k; i++) {
                Bone2D b = bind[influence[i]];
                Vector2 a0 = b.Pos;
                Vector2 a1 = b.Tip;
                for (int v = 0; v < n; v++) {
                    float d = SegmentDistance(pos[v], a0, a1);
                    if (d <= radius && d < handleDist[v]) {
                        handleOf[v] = i;
                        handleDist[v] = d;
                    }
                }
            }
            bool[] has = new bool[k];
            for (int v = 0; v < n; v++) {
                if (handleOf[v] >= 0) {
                    has[handleOf[v]] = true;
                }
            }
            for (int i = 0; i < k; i++) {
                if (has[i]) {
                    continue;
                }
                Bone2D b = bind[influence[i]];
                int best = -1;
                float bestD = float.MaxValue;
                for (int v = 0; v < n; v++) {
                    if (handleOf[v] >= 0) {
                        continue;
                    }
                    float d = SegmentDistance(pos[v], b.Pos, b.Tip);
                    if (d < bestD) {
                        bestD = d;
                        best = v;
                    }
                }
                if (best >= 0) {
                    handleOf[best] = i;
                    handleDist[best] = bestD;
                }
                else {
                    Rig2DPlatform.LogError($"[Rig2D:{who}]", $"{who}: no free vertex left for handle bone #{i}; its weight stays 0");
                }
            }

            //自由顶点：不是控制柄、且经网格边连得到某个控制柄的顶点；连不到的孤岛权重恒为 0（随宿主骨），不进方程
            bool[] reach = new bool[n];
            Queue<int> queue = new();
            for (int v = 0; v < n; v++) {
                if (handleOf[v] >= 0) {
                    reach[v] = true;
                    queue.Enqueue(v);
                }
            }
            while (queue.Count > 0) {
                int v = queue.Dequeue();
                for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                    int u = cols[e];
                    if (!reach[u]) {
                        reach[u] = true;
                        queue.Enqueue(u);
                    }
                }
            }
            int[] freeIndex = new int[n];
            int freeCount = 0;
            for (int v = 0; v < n; v++) {
                freeIndex[v] = handleOf[v] < 0 && reach[v] ? freeCount++ : -1;
            }
            int[] freeVerts = new int[freeCount];
            for (int v = 0; v < n; v++) {
                if (freeIndex[v] >= 0) {
                    freeVerts[freeIndex[v]] = v;
                }
            }

            //同一张网格的全部控制柄共用一个系数矩阵：先尝试带状 Cholesky 分解一次、每个柄只做回代；带宽过大时退回共轭梯度
            BandedCholesky chol = BandedCholesky.TryFactor(rowStart, cols, vals, diag, freeIndex, freeVerts);
            float[] weights = new float[n * k];
            double[] x = new double[freeCount];
            double[] rhs = new double[freeCount];
            double[] r = chol == null ? new double[freeCount] : null;
            double[] z = chol == null ? new double[freeCount] : null;
            double[] p = chol == null ? new double[freeCount] : null;
            double[] ap = chol == null ? new double[freeCount] : null;
            for (int i = 0; i < k; i++) {
                //右端：自由顶点与本柄顶点相邻的边权之和（其余柄为 0）
                for (int f = 0; f < freeCount; f++) {
                    int v = freeVerts[f];
                    double sum = 0.0;
                    for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                        if (handleOf[cols[e]] == i) {
                            sum += vals[e];
                        }
                    }
                    rhs[f] = sum;
                }
                if (chol != null) {
                    chol.Solve(rhs, x);
                }
                else {
                    Array.Clear(x);
                    SolveCG(rowStart, cols, vals, diag, freeIndex, freeVerts, rhs, x, r, z, p, ap);
                }
                for (int v = 0; v < n; v++) {
                    float w = handleOf[v] >= 0 ? (handleOf[v] == i ? 1f : 0f)
                        : freeIndex[v] >= 0 ? (float)Math.Clamp(x[freeIndex[v]], 0.0, 1.0) : 0f;
                    weights[v * k + i] = w;
                }
            }
            return weights;
        }

        /// <summary>
        /// 对称正定稀疏矩阵的带状 Cholesky：Reverse Cuthill-McKee 重排把非零元压到对角线附近，再按带宽分解。
        /// 二维网格的带宽约为 2√n，千把个顶点的网格分解只要几毫秒，之后每个右端回代 O(n·带宽)
        /// </summary>
        private sealed class BandedCholesky
        {
            /// <summary>带状存储上限（double 个数），超过就不分解、交给共轭梯度</summary>
            private const long MaxBandEntries = 6_000_000;

            private int m;
            private int band;
            private int[] perm;     //新序 → 自由下标
            private double[] low;   //下三角带：low[i * (band + 1) + (i − j)] = L[i, j]，j ∈ [i − band, i]
            private double[] work;

            public static BandedCholesky TryFactor(int[] rowStart, int[] cols, double[] vals, double[] diag, int[] freeIndex, int[] freeVerts) {
                int m = freeVerts.Length;
                if (m == 0) {
                    return null;
                }
                int[] perm = ReverseCuthillMcKee(rowStart, cols, freeIndex, freeVerts);
                int[] rank = new int[m];
                for (int i = 0; i < m; i++) {
                    rank[perm[i]] = i;
                }
                int band = 0;
                for (int f = 0; f < m; f++) {
                    int v = freeVerts[f];
                    for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                        int g = freeIndex[cols[e]];
                        if (g >= 0) {
                            band = Math.Max(band, Math.Abs(rank[f] - rank[g]));
                        }
                    }
                }
                if ((long)m * (band + 1) > MaxBandEntries) {
                    return null;
                }
                int w = band + 1;
                double[] low = new double[m * w];
                for (int f = 0; f < m; f++) {
                    int i = rank[f];
                    int v = freeVerts[f];
                    low[i * w] = diag[v];
                    for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                        int g = freeIndex[cols[e]];
                        if (g < 0) {
                            continue;
                        }
                        int j = rank[g];
                        if (j < i) {
                            low[i * w + (i - j)] -= vals[e];
                        }
                    }
                }
                //逐行分解：L[i, j] = (A[i, j] − Σ L[i, t]·L[j, t]) / L[j, j]，对角取平方根
                for (int i = 0; i < m; i++) {
                    int j0 = Math.Max(0, i - band);
                    for (int j = j0; j <= i; j++) {
                        double sum = low[i * w + (i - j)];
                        int t0 = Math.Max(j0, j - band);
                        for (int t = t0; t < j; t++) {
                            sum -= low[i * w + (i - t)] * low[j * w + (j - t)];
                        }
                        if (j == i) {
                            if (!(sum > 1e-12)) {
                                return null;
                            }
                            low[i * w] = Math.Sqrt(sum);
                        }
                        else {
                            low[i * w + (i - j)] = sum / low[j * w];
                        }
                    }
                }
                return new BandedCholesky { m = m, band = band, perm = perm, low = low, work = new double[m] };
            }

            public void Solve(double[] rhs, double[] x) {
                int w = band + 1;
                double[] y = work;
                //前代 L·y = P·b
                for (int i = 0; i < m; i++) {
                    double sum = rhs[perm[i]];
                    int j0 = Math.Max(0, i - band);
                    for (int j = j0; j < i; j++) {
                        sum -= low[i * w + (i - j)] * y[j];
                    }
                    y[i] = sum / low[i * w];
                }
                //回代 Lᵀ·z = y，原地写回 y
                for (int i = m - 1; i >= 0; i--) {
                    double sum = y[i];
                    int j1 = Math.Min(m - 1, i + band);
                    for (int j = i + 1; j <= j1; j++) {
                        sum -= low[j * w + (j - i)] * y[j];
                    }
                    y[i] = sum / low[i * w];
                }
                for (int i = 0; i < m; i++) {
                    x[perm[i]] = y[i];
                }
            }

            //RCM：每个连通块从度最小的顶点出发广度优先，邻居按度升序入队，最后整体反转
            private static int[] ReverseCuthillMcKee(int[] rowStart, int[] cols, int[] freeIndex, int[] freeVerts) {
                int m = freeVerts.Length;
                int[] degree = new int[m];
                for (int f = 0; f < m; f++) {
                    int v = freeVerts[f];
                    for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                        if (freeIndex[cols[e]] >= 0) {
                            degree[f]++;
                        }
                    }
                }
                int[] order = new int[m];
                bool[] seen = new bool[m];
                int head = 0;
                int tail = 0;
                List<int> nbr = new(16);
                while (tail < m) {
                    int startNode = -1;
                    for (int f = 0; f < m; f++) {
                        if (!seen[f] && (startNode < 0 || degree[f] < degree[startNode])) {
                            startNode = f;
                        }
                    }
                    seen[startNode] = true;
                    order[tail++] = startNode;
                    while (head < tail) {
                        int f = order[head++];
                        int v = freeVerts[f];
                        nbr.Clear();
                        for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                            int g = freeIndex[cols[e]];
                            if (g >= 0 && !seen[g]) {
                                seen[g] = true;
                                nbr.Add(g);
                            }
                        }
                        nbr.Sort((a, b) => degree[a].CompareTo(degree[b]));
                        foreach (int g in nbr) {
                            order[tail++] = g;
                        }
                    }
                }
                Array.Reverse(order);
                return order;
            }
        }

        //余切 Laplacian（对称、行压缩）：边权 = Σ 0.5·max(cot 对角, 1e-3)，对角 = 行边权和
        private static void BuildLaplacian(Vector2[] pos, int[] tris, out int[] rowStart, out int[] cols, out double[] vals, out double[] diag) {
            int n = pos.Length;
            Dictionary<long, double> edges = new(tris.Length * 2);
            void AddEdge(int a, int c, double w) {
                long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                edges[key] = edges.TryGetValue(key, out double old) ? old + w : w;
            }
            for (int t = 0; t < tris.Length; t += 3) {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                Vector2 p0 = pos[i0];
                Vector2 p1 = pos[i1];
                Vector2 p2 = pos[i2];
                double area2 = Math.Abs((double)(p1.X - p0.X) * (p2.Y - p0.Y) - (double)(p1.Y - p0.Y) * (p2.X - p0.X));
                if (area2 < 1e-6) {
                    continue;
                }
                //每个角的余切对应对边
                AddEdge(i1, i2, 0.5 * Math.Max(Cot(p0, p1, p2, area2), 1e-3));
                AddEdge(i2, i0, 0.5 * Math.Max(Cot(p1, p2, p0, area2), 1e-3));
                AddEdge(i0, i1, 0.5 * Math.Max(Cot(p2, p0, p1, area2), 1e-3));
            }
            int[] count = new int[n];
            foreach (long key in edges.Keys) {
                count[(int)(key >> 32)]++;
                count[(int)(key & 0xFFFFFFFF)]++;
            }
            rowStart = new int[n + 1];
            for (int v = 0; v < n; v++) {
                rowStart[v + 1] = rowStart[v] + count[v];
            }
            cols = new int[rowStart[n]];
            vals = new double[rowStart[n]];
            diag = new double[n];
            int[] fill = new int[n];
            foreach (KeyValuePair<long, double> kv in edges) {
                int a = (int)(kv.Key >> 32);
                int c = (int)(kv.Key & 0xFFFFFFFF);
                int ea = rowStart[a] + fill[a]++;
                cols[ea] = c;
                vals[ea] = kv.Value;
                int ec = rowStart[c] + fill[c]++;
                cols[ec] = a;
                vals[ec] = kv.Value;
                diag[a] += kv.Value;
                diag[c] += kv.Value;
            }
        }

        //顶点 corner 处的余切（两边夹角）
        private static double Cot(Vector2 corner, Vector2 a, Vector2 b, double area2) {
            double ux = a.X - corner.X;
            double uy = a.Y - corner.Y;
            double vx = b.X - corner.X;
            double vy = b.Y - corner.Y;
            return (ux * vx + uy * vy) / area2;
        }

        //Jacobi 预条件共轭梯度：只在自由顶点上解 A·x = b，A = 自由块的 Laplacian（与控制柄相连的边已移到右端）
        private static void SolveCG(int[] rowStart, int[] cols, double[] vals, double[] diag, int[] freeIndex, int[] freeVerts,
            double[] b, double[] x, double[] r, double[] z, double[] p, double[] ap) {
            int m = freeVerts.Length;
            if (m == 0) {
                return;
            }
            double bNorm = 0.0;
            for (int f = 0; f < m; f++) {
                r[f] = b[f];
                bNorm += b[f] * b[f];
            }
            if (bNorm <= 0.0) {
                return;
            }
            double rz = 0.0;
            for (int f = 0; f < m; f++) {
                double d = diag[freeVerts[f]];
                z[f] = d > 0.0 ? r[f] / d : 0.0;
                p[f] = z[f];
                rz += r[f] * z[f];
            }
            double stop = Tolerance * Tolerance * bNorm;
            for (int iter = 0; iter < MaxIterations; iter++) {
                double pap = 0.0;
                for (int f = 0; f < m; f++) {
                    int v = freeVerts[f];
                    double sum = diag[v] * p[f];
                    for (int e = rowStart[v]; e < rowStart[v + 1]; e++) {
                        int fi = freeIndex[cols[e]];
                        if (fi >= 0) {
                            sum -= vals[e] * p[fi];
                        }
                    }
                    ap[f] = sum;
                    pap += p[f] * sum;
                }
                if (pap <= 0.0) {
                    return;
                }
                double alpha = rz / pap;
                double rr = 0.0;
                for (int f = 0; f < m; f++) {
                    x[f] += alpha * p[f];
                    r[f] -= alpha * ap[f];
                    rr += r[f] * r[f];
                }
                if (rr <= stop) {
                    return;
                }
                double rzNew = 0.0;
                for (int f = 0; f < m; f++) {
                    double d = diag[freeVerts[f]];
                    z[f] = d > 0.0 ? r[f] / d : 0.0;
                    rzNew += r[f] * z[f];
                }
                double beta = rzNew / rz;
                rz = rzNew;
                for (int f = 0; f < m; f++) {
                    p[f] = z[f] + beta * p[f];
                }
            }
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b) {
            Vector2 ab = b - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-8f) {
                return Vector2.Distance(p, a);
            }
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
