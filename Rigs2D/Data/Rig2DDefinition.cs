using System;
using System.Collections.Generic;
using System.Text;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 一副 2D 骨架的完整静态定义：骨骼树、贴图件、求解器
    /// <br/>定义本身与任何贴图或游戏实体无关，可以来自 JSON（<see cref="Rig2DJson"/>）也可以来自代码（<see cref="Rig2DBuilder"/>）；
    /// <see cref="Resolve"/> 之后名字全部换成索引并算出父先子后的求值顺序，运行时不再做字符串查找
    /// </summary>
    public sealed class Rig2DDefinition
    {
        /// <summary>
        /// 骨架名（日志与调试显示）
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 根位姿单帧位移超过此距离（乘以实例 Scale）视为瞬移，整副骨架硬重建防抽搐
        /// </summary>
        public float SnapDistance { get; set; } = 340f;
        /// <summary>
        /// 骨骼定义表；<see cref="Resolve"/> 后各项 <see cref="Bone2DDef.Index"/> 与此表下标一致
        /// </summary>
        public List<Bone2DDef> Bones { get; } = [];
        /// <summary>
        /// 贴图件定义表
        /// </summary>
        public List<Piece2DDef> Pieces { get; } = [];
        /// <summary>
        /// 求解器定义表；运行时按此顺序逐个 <c>Step</c>，被依赖的求解器必须排在前面
        /// </summary>
        public List<Solver2DDef> Solvers { get; } = [];
        /// <summary>
        /// 带状件定义表（可选）：沿骨链铺的纹理条带，与整图件共用层序键
        /// </summary>
        public List<Ribbon2DDef> Ribbons { get; } = [];
        /// <summary>
        /// 关键帧片段表（可选），运行时由 <c>Rig2DClipPlayer</c> 播放
        /// </summary>
        public List<Animation.Rig2DClip> Clips { get; } = [];

        /// <summary>
        /// 父先子后的骨骼求值顺序，由 <see cref="Resolve"/> 生成
        /// </summary>
        public int[] EvaluationOrder { get; private set; } = [];
        /// <summary>
        /// 每根骨骼的直接子骨骼索引，由 <see cref="Resolve"/> 生成
        /// </summary>
        public int[][] Children { get; private set; } = [];
        /// <summary>
        /// 是否已成功解析
        /// </summary>
        public bool Resolved { get; private set; }

        private readonly Dictionary<string, int> boneLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> pieceLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> solverLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> ribbonLookup = new(StringComparer.Ordinal);

        /// <summary>
        /// 按名查骨骼索引，缺失返回 <c>-1</c>
        /// </summary>
        public int BoneIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return boneLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查贴图件索引（件名或骨骼名），缺失返回 <c>-1</c>
        /// </summary>
        public int PieceIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return pieceLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查求解器索引，缺失返回 <c>-1</c>
        /// </summary>
        public int SolverIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return solverLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查带状件索引（件名或其首骨名），缺失返回 <c>-1</c>
        /// </summary>
        public int RibbonIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return ribbonLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 骨骼数量
        /// </summary>
        public int BoneCount => Bones.Count;

        /// <summary>
        /// 解析全部名字引用并生成求值顺序
        /// <br/>失败时返回 <see langword="false"/> 并给出可读的错误汇总；成功后 <see cref="Resolved"/> 为真
        /// <br/>允许多个根骨骼；父引用成环或指向自身视为错误
        /// </summary>
        public bool Resolve(out string error) {
            Resolved = false;
            StringBuilder sb = null;
            void Fail(string msg) {
                sb ??= new StringBuilder();
                sb.Append(msg).Append("; ");
            }

            boneLookup.Clear();
            pieceLookup.Clear();
            solverLookup.Clear();
            ribbonLookup.Clear();

            if (Bones.Count == 0) {
                Fail("no bones");
            }

            for (int i = 0; i < Bones.Count; i++) {
                Bone2DDef b = Bones[i];
                b.Index = i;
                if (string.IsNullOrEmpty(b.Name)) {
                    Fail($"bone #{i} has no name");
                    continue;
                }
                if (!boneLookup.TryAdd(b.Name, i)) {
                    Fail($"duplicate bone name '{b.Name}'");
                }
            }

            int[] parents = new int[Bones.Count];
            for (int i = 0; i < Bones.Count; i++) {
                Bone2DDef b = Bones[i];
                if (b.IsRoot) {
                    parents[i] = -1;
                }
                else {
                    int p = BoneIndex(b.Parent);
                    if (p < 0) {
                        Fail($"bone '{b.Name}' parent '{b.Parent}' not found");
                    }
                    else if (p == i) {
                        Fail($"bone '{b.Name}' parents itself");
                        p = -1;
                    }
                    parents[i] = p;
                }
                b.ParentIndex = parents[i];
            }

            //环检测：沿父链走不超过 N 步就该到根
            for (int i = 0; i < Bones.Count; i++) {
                int cur = i;
                int steps = 0;
                while (cur >= 0 && steps <= Bones.Count) {
                    cur = parents[cur];
                    steps++;
                }
                if (cur >= 0) {
                    Fail($"bone '{Bones[i].Name}' is in a parent cycle");
                }
            }

            EvaluationOrder = BuildEvaluationOrder(parents);
            Children = BuildChildren(parents);

            for (int i = 0; i < Pieces.Count; i++) {
                Piece2DDef p = Pieces[i];
                p.Index = i;
                p.BoneIndex = BoneIndex(p.Bone);
                if (p.BoneIndex < 0) {
                    Fail($"piece '{p.DisplayName}' bone '{p.Bone}' not found");
                }
                if (p.Frames < 1) {
                    p.Frames = 1;
                }
                string key = p.DisplayName;
                if (!string.IsNullOrEmpty(key)) {
                    pieceLookup.TryAdd(key, i);
                }
            }

            for (int i = 0; i < Solvers.Count; i++) {
                Solver2DDef s = Solvers[i];
                if (string.IsNullOrEmpty(s.Name)) {
                    s.Name = $"{s.Type}_{i}";
                }
                if (!solverLookup.TryAdd(s.Name, i)) {
                    Fail($"duplicate solver name '{s.Name}'");
                }
                int[] idx = new int[s.Bones.Count];
                for (int k = 0; k < idx.Length; k++) {
                    idx[k] = BoneIndex(s.Bones[k]);
                    if (idx[k] < 0) {
                        Fail($"solver '{s.Name}' bone '{s.Bones[k]}' not found");
                    }
                }
                s.BoneIndices = idx;
            }

            for (int i = 0; i < Ribbons.Count; i++) {
                Ribbon2DDef r = Ribbons[i];
                r.Index = i;
                if (r.Bones.Count == 0) {
                    Fail($"ribbon '{r.DisplayName}' has no bones");
                }
                int[] idx = new int[r.Bones.Count];
                for (int k = 0; k < idx.Length; k++) {
                    idx[k] = BoneIndex(r.Bones[k]);
                    if (idx[k] < 0) {
                        Fail($"ribbon '{r.DisplayName}' bone '{r.Bones[k]}' not found");
                    }
                }
                r.BoneIndices = idx;
                string key = r.DisplayName;
                if (!string.IsNullOrEmpty(key)) {
                    ribbonLookup.TryAdd(key, i);
                }
            }

            for (int i = 0; i < Clips.Count; i++) {
                Clips[i]?.Resolve(this);
            }

            error = sb?.ToString();
            Resolved = sb == null;
            return Resolved;
        }

        //深度优先：所有根依序入栈，弹出即输出，保证父总在子之前；孤立/异常节点按自然序补齐
        private static int[] BuildEvaluationOrder(int[] parents) {
            int n = parents.Length;
            int[] order = new int[n];
            if (n == 0) {
                return order;
            }
            List<int>[] children = new List<int>[n];
            for (int i = 0; i < n; i++) {
                int p = parents[i];
                if (p < 0 || p >= n) {
                    continue;
                }
                children[p] ??= [];
                children[p].Add(i);
            }
            int cursor = 0;
            Stack<int> stack = new(n);
            for (int i = n - 1; i >= 0; i--) {
                if (parents[i] < 0 || parents[i] >= n) {
                    stack.Push(i);
                }
            }
            bool[] visited = new bool[n];
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (visited[node]) {
                    continue;
                }
                visited[node] = true;
                order[cursor++] = node;
                List<int> kids = children[node];
                if (kids == null) {
                    continue;
                }
                for (int k = kids.Count - 1; k >= 0; k--) {
                    stack.Push(kids[k]);
                }
            }
            for (int i = 0; i < n && cursor < n; i++) {
                if (!visited[i]) {
                    order[cursor++] = i;
                }
            }
            return order;
        }

        private static int[][] BuildChildren(int[] parents) {
            int n = parents.Length;
            List<int>[] lists = new List<int>[n];
            for (int i = 0; i < n; i++) {
                int p = parents[i];
                if (p < 0 || p >= n) {
                    continue;
                }
                lists[p] ??= [];
                lists[p].Add(i);
            }
            int[][] result = new int[n][];
            for (int i = 0; i < n; i++) {
                result[i] = lists[i] != null ? lists[i].ToArray() : [];
            }
            return result;
        }

        /// <summary>
        /// 深拷贝整份定义（未解析状态，需要调用方再 <see cref="Resolve"/>）
        /// </summary>
        public Rig2DDefinition Clone() {
            Rig2DDefinition c = new() {
                Name = Name,
                SnapDistance = SnapDistance,
            };
            foreach (Bone2DDef b in Bones) {
                c.Bones.Add(b.Clone());
            }
            foreach (Piece2DDef p in Pieces) {
                c.Pieces.Add(p.Clone());
            }
            foreach (Solver2DDef s in Solvers) {
                c.Solvers.Add(s.Clone());
            }
            foreach (Ribbon2DDef r in Ribbons) {
                c.Ribbons.Add(r.Clone());
            }
            foreach (Animation.Rig2DClip clip in Clips) {
                if (clip != null) {
                    c.Clips.Add(clip.Clone());
                }
            }
            return c;
        }

        /// <summary>
        /// 两份定义的骨骼拓扑是否一致（数量、名字、父级都相同）；热重载据此决定能否原地换参
        /// </summary>
        public bool SameTopology(Rig2DDefinition other) {
            if (other == null || other.Bones.Count != Bones.Count) {
                return false;
            }
            for (int i = 0; i < Bones.Count; i++) {
                Bone2DDef a = Bones[i];
                Bone2DDef b = other.Bones[i];
                if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                    || !string.Equals(a.Parent ?? string.Empty, b.Parent ?? string.Empty, StringComparison.Ordinal)) {
                    return false;
                }
            }
            if (other.Pieces.Count != Pieces.Count || other.Solvers.Count != Solvers.Count || other.Ribbons.Count != Ribbons.Count) {
                return false;
            }
            for (int i = 0; i < Solvers.Count; i++) {
                if (!string.Equals(Solvers[i].Name, other.Solvers[i].Name, StringComparison.Ordinal)
                    || !string.Equals(Solvers[i].Type, other.Solvers[i].Type, StringComparison.Ordinal)) {
                    return false;
                }
            }
            for (int i = 0; i < Ribbons.Count; i++) {
                if (!string.Equals(Ribbons[i].DisplayName, other.Ribbons[i].DisplayName, StringComparison.Ordinal)) {
                    return false;
                }
            }
            return true;
        }
    }
}
