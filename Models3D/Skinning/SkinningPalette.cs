using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Models3D.Skinning
{
    /// <summary>
    /// 一个骨架的 skin matrix 调色板
    /// <br/>由 <see cref="Animation.AnimationPlayer.SamplePose"/> 在每帧绘制前写入，
    /// 再由 <see cref="ApplyToVertices"/> 把 bind-pose 顶点蒙皮到目标顶点缓冲
    /// <br/>对应 <see cref="Model3DSkeleton.JointCount"/> 个矩阵；未启用蒙皮的实例不会创建
    /// </summary>
    public sealed class SkinningPalette
    {
        /// <summary>
        /// 关联的骨架
        /// <br/>用于校验长度与回退到 <see cref="Model3DJoint.BindTranslation"/> 等默认值
        /// </summary>
        public Model3DSkeleton Skeleton { get; }
        /// <summary>
        /// skin matrix 数组
        /// <br/><c>Matrices[j] = InverseBindMatrices[j] * GlobalJointMatrix[j]</c>
        /// <br/>蒙皮路径下，<c>worldPos = Σ weight[k] * (Matrices[index[k]] * bindPos)</c>
        /// </summary>
        public Matrix[] Matrices { get; }

        /// <summary>
        /// 构造一个与骨架对应的调色板
        /// <br/>初始内容全部为单位矩阵
        /// </summary>
        /// <param name="skeleton">关联的骨架</param>
        public SkinningPalette(Model3DSkeleton skeleton) {
            Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            Matrices = new Matrix[Skeleton.JointCount];
            SetIdentity();
        }

        /// <summary>
        /// 把所有矩阵重置为单位矩阵
        /// <br/>动画停止 / 暂未播放时用，等同于"绑定姿态显示"
        /// </summary>
        public void SetIdentity() {
            for (int i = 0; i < Matrices.Length; i++) {
                Matrices[i] = Matrix.Identity;
            }
        }

        /// <summary>
        /// 把 mesh group 的 bind-pose 顶点按调色板蒙皮到目标缓冲
        /// <br/>调用方必须保证 <paramref name="dst"/> 长度等于 <see cref="Model3DMeshGroup.BindVertices"/>
        /// <br/>权重和不为 1 时函数不会强制归一化，外部应在加载阶段做好归一化
        /// </summary>
        /// <param name="group">蒙皮源</param>
        /// <param name="palette">调色板</param>
        /// <param name="dst">目标顶点数组</param>
        public static void ApplyToVertices(Model3DMeshGroup group, SkinningPalette palette
            , VertexPositionNormalTexture[] dst) {
            if (group == null || palette == null || dst == null) {
                return;
            }
            VertexPositionNormalTexture[] src = group.BindVertices;
            Joint4[] joints = group.JointIndices;
            Vector4[] weights = group.JointWeights;
            if (src == null || joints == null || weights == null) {
                return;
            }
            int count = src.Length;
            if (dst.Length < count || joints.Length < count || weights.Length < count) {
                return;
            }
            Matrix[] mats = palette.Matrices;
            int maxIndex = mats.Length;
            for (int v = 0; v < count; v++) {
                VertexPositionNormalTexture bind = src[v];
                Joint4 idx = joints[v];
                Vector4 w = weights[v];
                Matrix sum = default;
                bool wrote = false;

                AccumulateWeighted(ref sum, mats, idx.I0, w.X, maxIndex, ref wrote);
                AccumulateWeighted(ref sum, mats, idx.I1, w.Y, maxIndex, ref wrote);
                AccumulateWeighted(ref sum, mats, idx.I2, w.Z, maxIndex, ref wrote);
                AccumulateWeighted(ref sum, mats, idx.I3, w.W, maxIndex, ref wrote);

                if (!wrote) {
                    sum = Matrix.Identity;
                }

                Vector3 skinnedPos = Vector3.Transform(bind.Position, sum);
                Vector3 skinnedNormal = Vector3.TransformNormal(bind.Normal, sum);
                if (skinnedNormal.LengthSquared() > 1e-8f) {
                    skinnedNormal.Normalize();
                }
                else {
                    skinnedNormal = bind.Normal;
                }
                dst[v] = new VertexPositionNormalTexture(skinnedPos, skinnedNormal, bind.TextureCoordinate);
            }
        }

        private static void AccumulateWeighted(ref Matrix sum, Matrix[] mats, ushort index
            , float weight, int maxIndex, ref bool wrote) {
            if (weight <= 0f) {
                return;
            }
            if (index >= maxIndex) {
                return;
            }
            Matrix m = mats[index];
            sum.M11 += m.M11 * weight; sum.M12 += m.M12 * weight; sum.M13 += m.M13 * weight; sum.M14 += m.M14 * weight;
            sum.M21 += m.M21 * weight; sum.M22 += m.M22 * weight; sum.M23 += m.M23 * weight; sum.M24 += m.M24 * weight;
            sum.M31 += m.M31 * weight; sum.M32 += m.M32 * weight; sum.M33 += m.M33 * weight; sum.M34 += m.M34 * weight;
            sum.M41 += m.M41 * weight; sum.M42 += m.M42 * weight; sum.M43 += m.M43 * weight; sum.M44 += m.M44 * weight;
            wrote = true;
        }
    }
}
