using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Models3D.Skinning
{
    /// <summary>
    /// 一个骨架资源
    /// <br/>来自 glTF <c>skins[]</c>，一个 <see cref="Runtime.Vault3DModel"/> 可能拥有多个骨架
    /// （Scroll 模型就有 2 个独立骨架，分别绑两块布料）
    /// <br/>骨架本身是不可变快照：joint 数组、父索引数组、逆绑定矩阵在加载完成后不再变化；
    /// 运行时姿态保存在 <see cref="Skinning.SkinningPalette"/> 和 <see cref="Animation.AnimationPlayer"/> 中
    /// </summary>
    public sealed class Model3DSkeleton
    {
        /// <summary>
        /// 骨架名称
        /// <br/>来自 glTF 节点名（root joint 的名字），主要用于调试
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// 骨架在所属 <see cref="Runtime.Vault3DModel.Skeletons"/> 中的索引
        /// <br/>同模型不同 <see cref="Runtime.Model3DMeshGroup"/> 可以通过 <see cref="Runtime.Model3DMeshGroup.SkinIndex"/> 引用此值
        /// </summary>
        public int Index { get; }
        /// <summary>
        /// 顺序与 glTF <c>skin.joints</c> 一致的骨骼数组
        /// <br/>顶点 <c>JOINTS_0</c> 索引、动画通道的 <see cref="Animation.Model3DAnimationChannel.JointIndex"/> 都基于此顺序
        /// </summary>
        public Model3DJoint[] Joints { get; }
        /// <summary>
        /// 每根骨骼的父骨骼索引
        /// <br/><c>-1</c> 表示骨架根（不再有父级）
        /// <br/>下标与 <see cref="Joints"/> 一一对应
        /// </summary>
        public int[] ParentIndices { get; }
        /// <summary>
        /// 每根骨骼的逆绑定矩阵
        /// <br/>来源 glTF <c>skin.inverseBindMatrices</c>，蒙皮矩阵计算为
        /// <c>skinMatrix[j] = inverseBindMatrix[j] * globalJointMatrix[j]</c>
        /// </summary>
        public Matrix[] InverseBindMatrices { get; }
        /// <summary>
        /// 拓扑序遍历顺序
        /// <br/>对父骨骼总是先于子骨骼出现的索引序列，
        /// 用于动画采样后逐 joint 计算 global matrix 时保证父矩阵已经就绪
        /// </summary>
        public int[] EvaluationOrder { get; }
        /// <summary>
        /// 每根骨骼对应的"场景图父节点世界矩阵"
        /// <br/>仅对 root joint（<see cref="ParentIndices"/>[j] &lt; 0）有意义：用作 jointGlobal 计算的起点
        /// <br/>非 root joint 此值不会被消费；构造时未提供则填充单位阵
        /// <br/>该矩阵把"skin.joints 集合之外的祖先链"上的静态 TRS 与轴向（如 glTF 的 Y-up↔Z-up）保留进来，
        /// 保证蒙皮结果与 <see cref="InverseBindMatrices"/> 处于同一 scene-root 空间
        /// </summary>
        public Matrix[] RootAncestorMatrices { get; }

        /// <summary>
        /// 骨骼数量
        /// <br/>等同于 <see cref="Joints"/> 长度，仅作便利访问器
        /// </summary>
        public int JointCount => Joints.Length;

        /// <summary>
        /// 构造一个骨架
        /// <br/>三个数组长度应保持一致；任意为 <see langword="null"/> 时会被替换为空数组
        /// <br/><paramref name="rootAncestorMatrices"/> 为可选参数：缺省时会按 joint 数量填充单位阵，
        /// 保持与旧调用方的向后兼容
        /// </summary>
        /// <param name="name">骨架名</param>
        /// <param name="index">在 <see cref="Runtime.Vault3DModel.Skeletons"/> 中的索引</param>
        /// <param name="joints">骨骼数组</param>
        /// <param name="parentIndices">父骨骼索引</param>
        /// <param name="inverseBindMatrices">逆绑定矩阵</param>
        /// <param name="rootAncestorMatrices">每根 root joint 的场景图静态祖先矩阵，缺省 = 单位阵</param>
        public Model3DSkeleton(string name, int index
            , Model3DJoint[] joints, int[] parentIndices, Matrix[] inverseBindMatrices
            , Matrix[] rootAncestorMatrices = null) {
            Name = name ?? string.Empty;
            Index = index;
            Joints = joints ?? Array.Empty<Model3DJoint>();
            ParentIndices = parentIndices ?? Array.Empty<int>();
            InverseBindMatrices = inverseBindMatrices ?? Array.Empty<Matrix>();
            EvaluationOrder = BuildEvaluationOrder(ParentIndices);
            RootAncestorMatrices = NormalizeRootAncestors(rootAncestorMatrices, Joints.Length);
        }

        //当外部未提供（或长度不符）祖先矩阵时，回落到全单位阵：等价于旧行为，保持向后兼容
        private static Matrix[] NormalizeRootAncestors(Matrix[] provided, int jointCount) {
            if (provided != null && provided.Length == jointCount) {
                return provided;
            }
            Matrix[] result = new Matrix[jointCount];
            for (int i = 0; i < jointCount; i++) {
                result[i] = Matrix.Identity;
            }
            return result;
        }

        //深度优先：从所有根（parent < 0）开始递归把节点加入序列，保证父总在子之前
        //失败情况下（出现环或越界）回落到 [0..N) 自然序，配合运行时的"未就绪父矩阵→以本地矩阵兜底"策略
        private static int[] BuildEvaluationOrder(int[] parents) {
            int n = parents.Length;
            int[] order = new int[n];
            if (n == 0) {
                return order;
            }
            //先按父收集子，O(n)
            List<int>[] children = new List<int>[n];
            for (int i = 0; i < n; i++) {
                int p = parents[i];
                if (p < 0 || p >= n) {
                    continue;
                }
                children[p] ??= new List<int>();
                children[p].Add(i);
            }
            int cursor = 0;
            Stack<int> stack = new Stack<int>(n);
            //从根反向入栈，弹出时形成"父在前"的序列
            for (int i = n - 1; i >= 0; i--) {
                int p = parents[i];
                if (p < 0 || p >= n) {
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
            //如果存在环或孤立节点，按自然序补齐
            for (int i = 0; i < n && cursor < n; i++) {
                if (!visited[i]) {
                    order[cursor++] = i;
                }
            }
            return order;
        }
    }
}
