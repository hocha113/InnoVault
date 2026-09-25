using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 串联骨链的重排工具（瞄准链 / 体态共用）。<see cref="Capture"/> 记下并铺好链的基准解：本轮已被排在前面的求解器解过的骨取那份解，
    /// 否则按父骨骼现算静息（含通道写进的局部角）；<see cref="Lay"/> 按"逐骨附加世界角 + 链首位移"整条重铺，
    /// 附加角沿链继承（给第 k 节加的角，后面各节跟着转）。链内父在前，中间可隔着不参与的骨（重铺时顺手传播）
    /// </summary>
    internal sealed class Rig2DChainLayout
    {
        private int[] bones = [];
        private bool[] directChild = [];
        private float[] rel = [];
        private float[] lengths = [];
        private Vector2 origin;

        public int Count => bones.Length;

        public Vector2 Origin => origin;

        public void Bind(Rig2DInstance rig, int[] chain) {
            bones = chain ?? [];
            int n = bones.Length;
            directChild = new bool[n];
            rel = new float[n];
            lengths = new float[n];
            if (rig?.Definition == null) {
                return;
            }
            for (int k = 1; k < n; k++) {
                int b = bones[k];
                directChild[k] = b >= 0 && bones[k - 1] >= 0 && rig.Definition.Bones[b].ParentIndex == bones[k - 1];
            }
        }

        /// <summary>
        /// 记下基准解并写进骨骼（附加角为零、链首不移）
        /// </summary>
        public void Capture(Rig2DInstance rig) {
            for (int k = 0; k < bones.Length; k++) {
                int b = bones[k];
                if (b < 0) {
                    continue;
                }
                if (k > 0 && !directChild[k] && bones[k - 1] >= 0) {
                    rig.PropagateDescendants(bones[k - 1]);
                }
                ref Bone2D bone = ref rig.Bones[b];
                if (!rig.SolvedThisPass(b)) {
                    bone.Pos = rig.RestPosition(b);
                    bone.Dir = rig.RestDirection(b);
                    bone.Length = rig.RestLength(b);
                }
                if (k == 0) {
                    origin = bone.Pos;
                    rel[0] = bone.Dir;
                }
                else {
                    rel[k] = bone.Dir - (bones[k - 1] >= 0 ? rig.Bones[bones[k - 1]].Dir : 0f);
                }
                lengths[k] = bone.Length;
            }
        }

        /// <summary>
        /// 按逐骨附加世界角（弧度，沿链继承）与链首位移重铺整条链
        /// </summary>
        public void Lay(Rig2DInstance rig, ReadOnlySpan<float> extraWorld, Vector2 offset) {
            for (int k = 0; k < bones.Length; k++) {
                int b = bones[k];
                if (b < 0) {
                    continue;
                }
                float add = k < extraWorld.Length ? extraWorld[k] : 0f;
                ref Bone2D bone = ref rig.Bones[b];
                if (k == 0) {
                    bone.Pos = origin + offset;
                    bone.Dir = rel[0] + add;
                }
                else {
                    int prev = bones[k - 1];
                    if (!directChild[k] && prev >= 0) {
                        rig.PropagateDescendants(prev);
                    }
                    bone.Pos = rig.RestPosition(b);
                    bone.Dir = (prev >= 0 ? rig.Bones[prev].Dir : 0f) + rel[k] + add;
                }
                bone.Length = lengths[k];
            }
        }

        /// <summary>
        /// 世界角 → 朝向系自竖直向上量的前倾角（弧度，正 = 朝面向一侧倾；镜像时按面向折算）
        /// </summary>
        public static float Lean(float worldDir, float mirrorSign) {
            float facing = mirrorSign < 0f ? MathHelper.Pi - worldDir : worldDir;
            return MathHelper.WrapAngle(facing + MathHelper.PiOver2);
        }
    }
}
