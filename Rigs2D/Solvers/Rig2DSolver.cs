using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 能按序号给出世界目标点的对象；IK 类求解器可以把目标接到另一个求解器（例如步态给出的足端）上
    /// </summary>
    public interface IRig2DTargetSource
    {
        /// <summary>
        /// 取第 <paramref name="index"/> 个目标点；不可用时返回 <see langword="false"/>
        /// </summary>
        bool TryGetTarget(int index, out Vector2 target);
    }

    /// <summary>
    /// 求解器基类：每帧在骨骼树静息传播之后运行，把自己负责的骨骼写成求解结果
    /// <br/>契约：
    /// <list type="bullet">
    /// <item>求解核心禁用 <c>Main.rand</c> 与全局时钟，装饰噪声只从 <see cref="Rig2DInstance.Seed"/> / <see cref="Rig2DInstance.Time"/> 派生，各端同输入同输出</item>
    /// <item>被写入的骨骼由 <see cref="DrivenBones"/> 声明，实例据此跳过它们的静息传播并在求解后重传播其子树</item>
    /// <item><see cref="Snap"/> 必须能从任意脏状态硬重建（瞬移 / 首帧 / 热重载）</item>
    /// <item>状态存在实例上（每个 <see cref="Rig2DInstance"/> 有独立求解器对象），不得用静态可变字段</item>
    /// </list>
    /// </summary>
    public abstract class Rig2DSolver
    {
        /// <summary>
        /// 实例名（定义中的 <c>name</c>）
        /// </summary>
        public string Name { get; internal set; } = string.Empty;
        /// <summary>
        /// 类型名（注册表键）
        /// </summary>
        public string TypeName { get; internal set; } = string.Empty;
        /// <summary>
        /// 所属实例
        /// </summary>
        public Rig2DInstance Rig { get; internal set; }
        /// <summary>
        /// 是否参与求解；关掉后其骨骼回到静息传播
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 定义里列出的骨骼索引（顺序同定义）
        /// </summary>
        protected int[] bones = [];

        /// <summary>
        /// 参与的骨骼索引（只读视图）
        /// </summary>
        public ReadOnlySpan<int> Bones => bones;

        /// <summary>
        /// 本求解器会写入的骨骼；默认即 <see cref="Bones"/>。只读取骨骼、不改写的求解器（如步态）应覆写为空
        /// </summary>
        public virtual ReadOnlySpan<int> DrivenBones => bones;

        /// <summary>
        /// 骨架整体倍率
        /// </summary>
        protected float Scale => Rig?.Scale ?? 1f;

        /// <summary>
        /// 骨架镜像符号（+1 / −1，见 <see cref="Rig2DInstance.Mirrored"/>）：
        /// 凡是"在父骨骼局部系里选左右"的极性参数（肘向、卷向、弓向、提示向量的侧向分量）都要乘它；
        /// 世界系的偏好向量（重力向、膝朝上）不乘
        /// </summary>
        protected float MirrorSign => Rig?.MirrorSign ?? 1f;

        /// <summary>
        /// 取第 i 个参与骨骼的引用
        /// </summary>
        protected ref Bone2D B(int i) => ref Rig.Bones[bones[i]];

        /// <summary>
        /// 取任意骨骼的引用
        /// </summary>
        protected ref Bone2D Bone(int boneIndex) => ref Rig.Bones[boneIndex];

        /// <summary>
        /// 第 i 个参与骨骼的父骨骼近端位置（根骨骼返回实例根位置）
        /// </summary>
        protected Vector2 ParentPos(int i) {
            int p = Rig.Definition.Bones[bones[i]].ParentIndex;
            return p >= 0 ? Rig.Bones[p].Pos : Rig.RootPosition;
        }

        /// <summary>
        /// 第 i 个参与骨骼的父骨骼轴向（根骨骼返回实例根朝向）
        /// </summary>
        protected float ParentDir(int i) {
            int p = Rig.Definition.Bones[bones[i]].ParentIndex;
            return p >= 0 ? Rig.Bones[p].Dir : Rig.RootRotation;
        }

        /// <summary>
        /// 第 i 个参与骨骼的静息长度（含 Scale 与长度覆写）
        /// </summary>
        protected float RestLength(int i) => Rig.RestLength(bones[i]);

        /// <summary>
        /// 第 i 个参与骨骼按父骨骼现算的静息近端位置（锚点；被接管的骨骼自己的 Pos 是上一帧解，不能当锚点用）
        /// </summary>
        protected Vector2 RestPosition(int i) => Rig.RestPosition(bones[i]);

        /// <summary>
        /// 第 i 个参与骨骼按父骨骼现算的静息轴向
        /// </summary>
        protected float RestDirection(int i) => Rig.RestDirection(bones[i]);

        /// <summary>
        /// 第 i 个参与骨骼静息轴向的单位向量
        /// </summary>
        protected Vector2 RestForward(int i) {
            float d = RestDirection(i);
            return new Vector2((float)Math.Cos(d), (float)Math.Sin(d));
        }

        internal void Bind(Rig2DInstance rig, Solver2DDef def) {
            Rig = rig;
            Name = def.Name;
            TypeName = def.Type;
            bones = (int[])def.BoneIndices.Clone();
            Configure(def);
        }

        /// <summary>
        /// 从定义读取参数并分配状态；绑定与热重载时都会调用，实现应可重入（保留能保留的运行状态）
        /// </summary>
        protected abstract void Configure(Solver2DDef def);

        /// <summary>
        /// 全部求解器创建完毕后调用一次，用于解析对其他求解器的引用
        /// </summary>
        protected internal virtual void PostBind() { }

        /// <summary>
        /// 实例 <see cref="Rig2DInstance.Mirrored"/> 变化后、本帧求解之前调用一次：清掉带左右极性的迟滞量（肘侧、膝侧），
        /// 让新极性下的第一帧重新选边；默认空实现
        /// </summary>
        protected internal virtual void OnMirrorChanged() { }

        /// <summary>
        /// <see cref="Enabled"/> 从假翻回真之后、本帧 <see cref="Step"/> 之前调用一次（停用期间至少跳过了一次求解）：
        /// 带内部平滑状态的求解器在这里从骨骼<b>当前</b>位姿重新播种（停用期间这些骨可能被别的求解器或外部写接管过，
        /// 旧状态早已过期，直接续算会从陈旧姿态猛甩过去）；默认空实现
        /// </summary>
        protected internal virtual void OnEnabled() { }

        /// <summary>
        /// 硬重建：从当前静息传播结果直接摆好姿态，清空一切平滑量
        /// </summary>
        public abstract void Snap();

        /// <summary>
        /// 推进一帧并写入骨骼
        /// </summary>
        /// <param name="dt">帧步长（60fps 基准，1 = 一帧）</param>
        public abstract void Step(float dt);

        /// <summary>
        /// 调试叠层绘制（世界坐标已换成屏幕坐标的 <paramref name="toScreen"/> 由叠层提供）
        /// </summary>
        public virtual void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) { }

        /// <summary>
        /// 解析 <c>targetSolver</c> / <c>targetIndex</c> 两个参数为目标源；缺失返回 <see langword="null"/>
        /// </summary>
        protected IRig2DTargetSource ResolveTargetSource(Solver2DDef def, out int index) {
            index = def.GetInt("targetIndex", 0);
            string name = def.GetString("targetSolver", null);
            if (string.IsNullOrEmpty(name) || Rig == null) {
                return null;
            }
            Rig2DSolver other = Rig.Solver(name);
            if (other is IRig2DTargetSource src) {
                return src;
            }
            if (other == null) {
                VaultMod.LoggerError($"[Rig2D:{Rig.Name}/{Name}]", $"targetSolver '{name}' not found");
            }
            else {
                VaultMod.LoggerError($"[Rig2D:{Rig.Name}/{Name}]", $"targetSolver '{name}' is not a target source");
            }
            return null;
        }
    }
}
