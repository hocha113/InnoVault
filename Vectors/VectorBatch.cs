using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 多笔合批：<see cref="Begin"/> 后连续 <see cref="Stroke(VectorPath, StrokeStyle, in VectorTransform, in VectorDrawOptions)"/> / <see cref="Fill(VectorPath, FillStyle, in VectorTransform, in VectorDrawOptions)"/>，
    /// 按提交选项（着色器 / 贴图 / 混合 / 采样 / 空间 / 矩阵 / SDF 参数）分组攒进各自的网格，<see cref="End"/> 时每组一次提交
    /// <br/>提交顺序 = 各组首次出现的顺序，组内按加入顺序；不同组之间因此没有严格层序，需要严格层序的元素放同一组或分多次 Begin / End
    /// <br/>一组顶点超过预算的 3/4 时自动开新网格接着攒。只允许在渲染线程调用
    /// </summary>
    public sealed class VectorBatch
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly Effect Effect;
            public readonly Texture2D Texture;
            public readonly VectorSpace Space;
            public readonly Matrix? CustomMatrix;
            public readonly BlendState Blend;
            public readonly SamplerState Sampler;
            public readonly string MatrixParameter;
            public readonly SpriteBatch SpriteBatch;
            public readonly float Antialias;
            public readonly float Glow;
            public readonly float GlowPower;

            public Key(in VectorDrawOptions o) {
                Effect = o.Effect;
                Texture = o.Texture;
                Space = o.Space;
                CustomMatrix = o.CustomMatrix;
                Blend = o.Blend;
                Sampler = o.Sampler;
                MatrixParameter = o.MatrixParameter;
                SpriteBatch = o.SpriteBatch;
                Antialias = o.Antialias;
                Glow = o.Glow;
                GlowPower = o.GlowPower;
            }

            public VectorDrawOptions ToOptions() => new(Space, Effect, Texture) {
                CustomMatrix = CustomMatrix,
                Blend = Blend,
                Sampler = Sampler,
                MatrixParameter = MatrixParameter,
                SpriteBatch = SpriteBatch,
                Antialias = Antialias,
                Glow = Glow,
                GlowPower = GlowPower,
            };

            public bool Equals(Key other)
                => ReferenceEquals(Effect, other.Effect)
                && ReferenceEquals(Texture, other.Texture)
                && Space == other.Space
                && Nullable.Equals(CustomMatrix, other.CustomMatrix)
                && ReferenceEquals(Blend, other.Blend)
                && ReferenceEquals(Sampler, other.Sampler)
                && string.Equals(MatrixParameter, other.MatrixParameter, StringComparison.Ordinal)
                && ReferenceEquals(SpriteBatch, other.SpriteBatch)
                && Antialias == other.Antialias
                && Glow == other.Glow
                && GlowPower == other.GlowPower;

            public override bool Equals(object obj) => obj is Key k && Equals(k);

            public override int GetHashCode() => HashCode.Combine(Effect, Texture, (int)Space, Blend, Sampler, Antialias, Glow);
        }

        private sealed class Group
        {
            public Key Key;
            public readonly List<VectorMesh> Meshes = [];
        }

        //一组顶点超过这个比例就开新网格，给单笔留够余量
        private const float SplitRatio = 0.75f;

        private readonly List<Group> groups = [];
        private readonly Stack<VectorMesh> pool = new();
        private readonly Stack<Group> groupPool = new();
        private bool begun;

        /// <summary>上一次 <see cref="End"/> 实际发出的提交次数（网格数）</summary>
        public int LastDrawCalls { get; private set; }
        /// <summary>当前累积的组数</summary>
        public int GroupCount => groups.Count;
        /// <summary>是否处于 Begin 与 End 之间</summary>
        public bool IsBegun => begun;

        /// <summary>开始一批；未 End 的上一批会被丢弃</summary>
        public void Begin() {
            if (begun) {
                Recycle();
            }
            begun = true;
        }

        /// <summary>攒一笔路径描边</summary>
        public void Stroke(VectorPath path, StrokeStyle style, in VectorTransform transform, in VectorDrawOptions options) {
            if (!begun || path == null || style == null || path.IsEmpty) {
                return;
            }
            Acquire(in options).AppendStroke(path, style, in transform);
        }

        /// <summary>攒一笔路径描边（恒等变换）</summary>
        public void Stroke(VectorPath path, StrokeStyle style, in VectorDrawOptions options) => Stroke(path, style, in VectorTransform.Identity, in options);

        /// <summary>攒一笔点列描边</summary>
        public void Stroke(ReadOnlySpan<Vector2> points, StrokeStyle style, in VectorDrawOptions options, bool closed = false) {
            if (!begun || style == null || points.Length == 0) {
                return;
            }
            Acquire(in options).AppendStroke(points, style, closed);
        }

        /// <summary>攒一笔路径填充</summary>
        public void Fill(VectorPath path, FillStyle style, in VectorTransform transform, in VectorDrawOptions options) {
            if (!begun || path == null || style == null || path.IsEmpty) {
                return;
            }
            Acquire(in options).AppendFill(path, style, in transform);
        }

        /// <summary>攒一笔路径填充（恒等变换）</summary>
        public void Fill(VectorPath path, FillStyle style, in VectorDrawOptions options) => Fill(path, style, in VectorTransform.Identity, in options);

        /// <summary>攒一笔多边形填充（输出空间）</summary>
        public void Fill(ReadOnlySpan<Vector2> polygon, FillStyle style, in VectorDrawOptions options) {
            if (!begun || style == null || polygon.Length < 3) {
                return;
            }
            Acquire(in options).AppendFill(polygon, style);
        }

        /// <summary>直接往某组网格追加（自定义几何时用），返回可写网格</summary>
        public VectorMesh GetMesh(in VectorDrawOptions options) => begun ? Acquire(in options) : null;

        /// <summary>提交全部分组并回收网格</summary>
        public void End() {
            if (!begun) {
                return;
            }
            int calls = 0;
            for (int g = 0; g < groups.Count; g++) {
                Group group = groups[g];
                VectorDrawOptions options = group.Key.ToOptions();
                for (int m = 0; m < group.Meshes.Count; m++) {
                    VectorMesh mesh = group.Meshes[m];
                    if (!mesh.IsEmpty) {
                        VectorRenderer.Draw(mesh, in options);
                        calls++;
                    }
                }
            }
            LastDrawCalls = calls;
            Recycle();
            begun = false;
        }

        //==================== 内部 ====================

        private VectorMesh Acquire(in VectorDrawOptions options) {
            Key key = new(in options);
            Group group = null;
            for (int i = groups.Count - 1; i >= 0; i--) {
                if (groups[i].Key.Equals(key)) {
                    group = groups[i];
                    break;
                }
            }
            if (group == null) {
                group = groupPool.Count > 0 ? groupPool.Pop() : new Group();
                group.Key = key;
                groups.Add(group);
            }
            VectorMesh mesh = group.Meshes.Count > 0 ? group.Meshes[^1] : null;
            if (mesh == null || mesh.VertexCount > VectorMesh.MaxVertices * SplitRatio) {
                mesh = pool.Count > 0 ? pool.Pop() : new VectorMesh(1024, 3072);
                mesh.Clear();
                group.Meshes.Add(mesh);
            }
            return mesh;
        }

        private void Recycle() {
            for (int g = 0; g < groups.Count; g++) {
                Group group = groups[g];
                foreach (VectorMesh mesh in group.Meshes) {
                    mesh.Clear();
                    pool.Push(mesh);
                }
                group.Meshes.Clear();
                group.Key = default;
                groupPool.Push(group);
            }
            groups.Clear();
        }
    }
}
