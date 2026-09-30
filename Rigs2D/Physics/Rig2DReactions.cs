using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Physics
{
    /// <summary>
    /// 骨架级受击反应层（<see cref="Rig2DInstance.Reactions"/>）：一记冲量打在某点，按"推力场"分到骨骼与肢体上，
    /// 用衰减弹簧加性叠加，自己弹回零
    /// <br/>推力场：作用点附近的一团速度 <c>v(x) = 冲量 × exp(−|x − 作用点|² / 半径²)</c>。按父先子后做一遍线性化正向运动学：
    /// 根被推走一点（<see cref="RootGain"/>），每根骨的近端跟着父骨的实际运动走，自己再转一个角让尖端尽量贴合推力场
    /// （胸口挨踹：脊柱下段后仰把胸口送走、头顶滞后 → 甩头，挂在肩上的手臂根被带走、手滞后 → 四肢往前甩）。
    /// 骨角弹簧写在静息传播之前（受链式求解器的瞄准链 / 体态接续），肢体末端（IK、趾行腿、指向、持械，见 <see cref="IRig2DReactive"/>）
    /// 另有一组目标偏移弹簧：末端相对肢根的滞后
    /// <br/>顿帧：<c>rig.Step(0)</c> 时弹簧不走，期间打进来的冲量只攒速度，放开那一帧一起弹出来
    /// <br/>空间：作用点与冲量都在骨架空间（画布骨架先换进画布空间，见 <see cref="Rig2DCanvas.Unmap"/>）；冲量单位 = 作用点处的速度（像素 / 帧）
    /// </summary>
    public sealed class Rig2DReactions
    {
        private float[] angle = [];
        private float[] angularVelocity = [];
        private float[] boneGain = [];
        private float[] kick = [];
        private Vector2[] proxVel = [];
        private Vector2 rootOffset;
        private Vector2 rootVelocity;
        private Vector2[] offset = [];
        private Vector2[] offsetVelocity = [];
        private float[] solverGain = [];
        private bool active;
        private bool wroteNonZero;

        internal Rig2DReactions(Rig2DInstance rig) {
            Rig = rig;
        }

        /// <summary>所属骨架</summary>
        public Rig2DInstance Rig { get; }
        /// <summary>推力场半径（Scale 为 1 的像素）：越大，一记打击牵动的骨越多</summary>
        public float Radius { get; set; } = 140f;
        /// <summary>骨角弹簧固有频率（赫兹）：越高回得越快</summary>
        public float Frequency { get; set; } = 2.4f;
        /// <summary>骨角弹簧阻尼比（&lt; 1 有过冲余摆）</summary>
        public float DampingRatio { get; set; } = 0.4f;
        /// <summary>骨角整体增益</summary>
        public float Gain { get; set; } = 1f;
        /// <summary>根被推走的比例（0 = 根钉死，只有骨链转）：站着挨打时骨盆被打得往后一坐</summary>
        public float RootGain { get; set; } = 0.35f;
        /// <summary>单骨附加角上限（弧度）</summary>
        public float MaxAngle { get; set; } = 0.7f;
        /// <summary>肢体末端偏移弹簧的固有频率（赫兹）</summary>
        public float EffectorFrequency { get; set; } = 3f;
        /// <summary>肢体末端偏移弹簧的阻尼比</summary>
        public float EffectorDampingRatio { get; set; } = 0.45f;
        /// <summary>肢体末端整体增益</summary>
        public float EffectorGain { get; set; } = 0.8f;
        /// <summary>末端偏移上限（Scale 为 1 的像素）</summary>
        public float MaxOffset { get; set; } = 90f;
        /// <summary>是否还有没回零的弹簧</summary>
        public bool Active => active;

        //==================== 配置 ====================

        internal void Rebuild() {
            Rig2DDefinition def = Rig.Definition;
            int n = def?.BoneCount ?? 0;
            int sc = Rig.Solvers.Length;
            if (angle.Length != n) {
                angle = new float[n];
                angularVelocity = new float[n];
                kick = new float[n];
                proxVel = new Vector2[n];
                float[] gains = new float[n];
                Array.Fill(gains, 1f);
                boneGain = gains;
            }
            if (offset.Length != sc) {
                offset = new Vector2[sc];
                offsetVelocity = new Vector2[sc];
                float[] gains = new float[sc];
                Array.Fill(gains, 1f);
                solverGain = gains;
            }
            //拓扑变了（热重载整副重建）：旧弹簧状态作废
            Clear();
            ApplyDefinition();
        }

        /// <summary>
        /// 从定义的 <c>"reactions"</c> 参数袋读取调参（没有这个块时不动当前值）：绑定与热重载后自动调用。
        /// 块里写了 <c>bones</c> / <c>solvers</c> 时，未列出的骨 / 求解器增益回到 1
        /// </summary>
        public void ApplyDefinition() {
            Solver2DDef p = Rig.Definition?.ReactionParams;
            if (p?.Params == null) {
                return;
            }
            Radius = p.GetFloat("radius", Radius);
            Frequency = p.GetFloat("frequency", Frequency);
            DampingRatio = p.GetFloat("dampingRatio", DampingRatio);
            Gain = p.GetFloat("gain", Gain);
            RootGain = p.GetFloat("rootGain", RootGain);
            MaxAngle = p.GetAngle("maxAngle", MaxAngle);
            EffectorFrequency = p.GetFloat("effectorFrequency", EffectorFrequency);
            EffectorDampingRatio = p.GetFloat("effectorDampingRatio", EffectorDampingRatio);
            EffectorGain = p.GetFloat("effectorGain", EffectorGain);
            MaxOffset = p.GetFloat("maxOffset", MaxOffset);
            if (p.Params["bones"] is Newtonsoft.Json.Linq.JObject bones) {
                Array.Fill(boneGain, 1f);
                foreach (Newtonsoft.Json.Linq.JProperty kv in bones.Properties()) {
                    int b = Rig.Bone(kv.Name);
                    if (b < 0) {
                        Rig2DPlatform.LogError($"[Rig2D:{Rig.Name}:reactions]", $"reactions.bones: bone '{kv.Name}' not found");
                        continue;
                    }
                    if (kv.Value.Type is Newtonsoft.Json.Linq.JTokenType.Float or Newtonsoft.Json.Linq.JTokenType.Integer) {
                        boneGain[b] = (float)kv.Value;
                    }
                }
            }
            if (p.Params["solvers"] is Newtonsoft.Json.Linq.JObject solvers) {
                Array.Fill(solverGain, 1f);
                foreach (Newtonsoft.Json.Linq.JProperty kv in solvers.Properties()) {
                    Rig2DSolver s = Rig.Solver(kv.Name);
                    if (s == null) {
                        Rig2DPlatform.LogError($"[Rig2D:{Rig.Name}:reactions]", $"reactions.solvers: solver '{kv.Name}' not found");
                        continue;
                    }
                    if (kv.Value.Type is Newtonsoft.Json.Linq.JTokenType.Float or Newtonsoft.Json.Linq.JTokenType.Integer) {
                        SetSolverGain(s, (float)kv.Value);
                    }
                }
            }
        }

        /// <summary>
        /// 单骨增益（缺省 1；0 = 这根骨不受击，例如不想让根骨转动带着全身晃）
        /// </summary>
        public void SetBoneGain(int bone, float gain) {
            if ((uint)bone < (uint)boneGain.Length) {
                boneGain[bone] = gain;
            }
        }

        /// <summary>
        /// 按名设单骨增益
        /// </summary>
        public void SetBoneGain(string bone, float gain) => SetBoneGain(Rig.Bone(bone), gain);

        /// <summary>
        /// 取单骨增益
        /// </summary>
        public float BoneGain(int bone) => (uint)bone < (uint)boneGain.Length ? boneGain[bone] : 0f;

        /// <summary>
        /// 单个肢体求解器的末端增益（缺省 1；0 = 不甩这条肢）
        /// </summary>
        public void SetSolverGain(Rig2DSolver solver, float gain) {
            if (solver != null && solver.Rig == Rig && (uint)solver.Slot < (uint)solverGain.Length) {
                solverGain[solver.Slot] = gain;
            }
        }

        /// <summary>
        /// 按名设肢体求解器的末端增益
        /// </summary>
        public void SetSolverGain(string solver, float gain) => SetSolverGain(Rig.Solver(solver), gain);

        /// <summary>
        /// 某骨当前的附加角（弧度，相对父骨的局部增量）
        /// </summary>
        public float BoneAngle(int bone) => (uint)bone < (uint)angle.Length ? angle[bone] : 0f;

        //==================== 打击 ====================

        /// <summary>
        /// 一记打击：<paramref name="contact"/> 处受 <paramref name="impulse"/>（作用点处的速度，像素 / 帧，骨架空间），
        /// 按推力场给每根骨一个角速度踢、每条肢体末端一个滞后速度。多记打击累加；顿帧期间只攒不动
        /// </summary>
        /// <param name="contact">作用点（骨架空间）</param>
        /// <param name="impulse">冲量（作用点处的速度）</param>
        /// <param name="gain">本记倍率</param>
        /// <param name="radius">本记推力场半径（Scale 为 1 的像素，≤ 0 取 <see cref="Radius"/>）</param>
        public void Hit(Vector2 contact, Vector2 impulse, float gain = 1f, float radius = -1f) {
            Rig2DDefinition def = Rig.Definition;
            if (def == null || Rig.Bones.Length == 0 || angle.Length != Rig.Bones.Length) {
                return;
            }
            if (impulse.LengthSquared() < 1e-8f || gain == 0f) {
                return;
            }
            float scale = MathF.Max(Rig.Scale, 0.001f);
            float r = (radius > 0f ? radius : Radius) * scale;
            float inv = 1f / MathF.Max(r * r, 1f);
            Vector2 j = impulse * gain;

            //根：被推走一截（弹簧拉回）
            Vector2 rootV = j * MathF.Exp(-Vector2.DistanceSquared(Rig.RootPosition, contact) * inv) * RootGain;
            rootVelocity += rootV;

            //骨：父先子后的线性化正向运动学。近端速度取父骨的实际运动，本骨再转一个角去贴合推力场在尖端的速度
            Bone2D[] bones = Rig.Bones;
            int[] order = def.EvaluationOrder;
            for (int k = 0; k < order.Length; k++) {
                int b = order[k];
                Bone2DDef bd = def.Bones[b];
                ref Bone2D bone = ref bones[b];
                int p = bd.ParentIndex;
                Vector2 vp;
                float inherited = 0f;
                if (p < 0) {
                    vp = rootV;
                }
                else {
                    //挂点随父骨转动：父近端速度 + 父角速度 × 挂点相对父近端的力臂
                    vp = proxVel[p] + Cross(kick[p], bone.Pos - bones[p].Pos);
                    if (bd.InheritRotation) {
                        inherited = kick[p];
                    }
                }
                Vector2 axis = bone.Tip - bone.Pos;
                float l2 = axis.LengthSquared();
                float g = boneGain[b] * Gain;
                float w = inherited;
                if (l2 >= 1f && g != 0f) {
                    Vector2 want = j * MathF.Exp(-Vector2.DistanceSquared(bone.Tip, contact) * inv);
                    //只看垂直于骨轴的分量（骨长不变）：让尖端贴合推力场所需的世界角速度
                    float desired = (axis.X * (want.Y - vp.Y) - axis.Y * (want.X - vp.X)) / l2;
                    w = inherited + (desired - inherited) * g;
                    angularVelocity[b] += w - inherited;
                }
                kick[b] = w;
                proxVel[b] = vp;
            }

            //肢体末端：相对肢根的滞后
            Rig2DSolver[] solvers = Rig.Solvers;
            for (int s = 0; s < solvers.Length && s < offset.Length; s++) {
                if (solvers[s] is not IRig2DReactive limb || !solvers[s].Enabled || limb.ReactionFollows || solverGain[s] == 0f) {
                    continue;
                }
                Vector2 vb = j * MathF.Exp(-Vector2.DistanceSquared(limb.ReactionBase, contact) * inv);
                Vector2 ve = j * MathF.Exp(-Vector2.DistanceSquared(limb.ReactionEffector, contact) * inv);
                offsetVelocity[s] += (ve - vb) * EffectorGain * solverGain[s];
            }
            active = true;
        }

        //ω × r（二维）
        private static Vector2 Cross(float w, Vector2 r) => new(-w * r.Y, w * r.X);

        /// <summary>
        /// 清空全部弹簧（瞬移、复位、死亡）
        /// </summary>
        public void Clear() {
            rootOffset = rootVelocity = Vector2.Zero;
            Array.Clear(angle);
            Array.Clear(angularVelocity);
            Array.Clear(offset);
            Array.Clear(offsetVelocity);
            active = false;
            WriteOut();
        }

        //==================== 推进 ====================

        internal void Advance(float dt) {
            if (!active) {
                if (wroteNonZero) {
                    WriteOut();
                }
                return;
            }
            if (dt > 0f) {
                const float perFrame = MathHelper.TwoPi / 60f;
                float w = MathF.Max(Frequency, 0.01f) * perFrame;
                float z = MathF.Max(DampingRatio, 0f);
                float maxA = MathF.Max(MaxAngle, 0f);
                bool any = false;
                for (int b = 0; b < angle.Length; b++) {
                    float a = angle[b];
                    float v = angularVelocity[b];
                    if (a == 0f && v == 0f) {
                        continue;
                    }
                    v += (-w * w * a - 2f * z * w * v) * dt;
                    a += v * dt;
                    if (a > maxA) {
                        a = maxA;
                        v = MathF.Min(v, 0f);
                    }
                    else if (a < -maxA) {
                        a = -maxA;
                        v = MathF.Max(v, 0f);
                    }
                    if (MathF.Abs(a) < 1e-4f && MathF.Abs(v) < 1e-4f) {
                        a = v = 0f;
                    }
                    else {
                        any = true;
                    }
                    angle[b] = a;
                    angularVelocity[b] = v;
                }
                if (rootOffset != Vector2.Zero || rootVelocity != Vector2.Zero) {
                    rootVelocity += (-w * w * rootOffset - 2f * z * w * rootVelocity) * dt;
                    rootOffset += rootVelocity * dt;
                    if (rootOffset.LengthSquared() < 0.01f && rootVelocity.LengthSquared() < 0.01f) {
                        rootOffset = rootVelocity = Vector2.Zero;
                    }
                    else {
                        any = true;
                    }
                }
                float we = MathF.Max(EffectorFrequency, 0.01f) * perFrame;
                float ze = MathF.Max(EffectorDampingRatio, 0f);
                float maxO = MathF.Max(MaxOffset, 0f) * MathF.Max(Rig.Scale, 0.001f);
                for (int s = 0; s < offset.Length; s++) {
                    Vector2 o = offset[s];
                    Vector2 u = offsetVelocity[s];
                    if (o == Vector2.Zero && u == Vector2.Zero) {
                        continue;
                    }
                    u += (-we * we * o - 2f * ze * we * u) * dt;
                    o += u * dt;
                    float len = o.Length();
                    if (len > maxO && len > 0f) {
                        o *= maxO / len;
                        //贴着上限时去掉向外的速度分量
                        Vector2 dir = o / maxO;
                        float outward = Vector2.Dot(u, dir);
                        if (outward > 0f) {
                            u -= dir * outward;
                        }
                    }
                    if (o.LengthSquared() < 0.01f && u.LengthSquared() < 0.01f) {
                        o = u = Vector2.Zero;
                    }
                    else {
                        any = true;
                    }
                    offset[s] = o;
                    offsetVelocity[s] = u;
                }
                active = any;
            }
            WriteOut();
        }

        private void WriteOut() {
            float[] target = Rig.reactionRotation;
            Rig.reactionRootOffset = rootOffset;
            bool nonZero = rootOffset != Vector2.Zero;
            int n = Math.Min(target.Length, angle.Length);
            for (int b = 0; b < n; b++) {
                target[b] = angle[b];
                nonZero |= angle[b] != 0f;
            }
            Rig2DSolver[] solvers = Rig.Solvers;
            for (int s = 0; s < solvers.Length && s < offset.Length; s++) {
                if (solvers[s] is IRig2DReactive limb) {
                    limb.ReactionOffset = offset[s];
                    nonZero |= offset[s] != Vector2.Zero;
                }
            }
            wroteNonZero = nonZero;
        }
    }
}
