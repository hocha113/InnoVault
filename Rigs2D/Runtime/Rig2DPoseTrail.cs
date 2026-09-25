using System;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 骨架位姿快照环：爆发段隔帧记录整副骨骼位姿，渲染层以低透明重绘成"同素材残影"
    /// <br/>纯本地表现量；断档超过阈值自动清环，防上一次爆发的陈旧位姿闪现
    /// </summary>
    public sealed class Rig2DPoseTrail
    {
        /// <summary>
        /// 一帧快照
        /// </summary>
        public sealed class Snapshot
        {
            /// <summary>
            /// 骨骼位姿（下标同实例 <see cref="Rig2DInstance.Bones"/>）
            /// </summary>
            public Bone2D[] Bones = [];
            /// <summary>
            /// 消费方自定义附加量（尾扇张合等随位姿一起冻结的标量）
            /// </summary>
            public float[] Extra = [];
            /// <summary>
            /// 是否有效
            /// </summary>
            public bool Valid;
            /// <summary>
            /// 捕获帧号
            /// </summary>
            public uint Tick;
        }

        private readonly Snapshot[] ring;
        private readonly int extraCount;
        private int head;
        private uint lastCaptureTick;

        /// <summary>
        /// 槽位数
        /// </summary>
        public int Slots => ring.Length;
        /// <summary>
        /// 相邻两次捕获的最小间隔（帧）
        /// </summary>
        public uint MinInterval { get; set; } = 2;
        /// <summary>
        /// 断档超过此帧数即清环
        /// </summary>
        public uint GapReset { get; set; } = 20;
        /// <summary>
        /// 强度低于此值不捕获
        /// </summary>
        public float MinStrength { get; set; } = 0.05f;

        /// <summary>
        /// 构造快照环
        /// </summary>
        /// <param name="slots">槽位数</param>
        /// <param name="extraCount">每个快照附带的自定义标量个数</param>
        public Rig2DPoseTrail(int slots = 4, int extraCount = 0) {
            ring = new Snapshot[Math.Max(slots, 1)];
            for (int i = 0; i < ring.Length; i++) {
                ring[i] = new Snapshot();
            }
            this.extraCount = Math.Max(extraCount, 0);
        }

        /// <summary>
        /// 按新旧取快照：age 0 = 最新 … Slots−1 = 最旧；无效返回 <see langword="null"/>
        /// </summary>
        public Snapshot Get(int age) {
            Snapshot s = ring[(head - age % ring.Length + ring.Length) % ring.Length];
            return s.Valid ? s : null;
        }

        /// <summary>
        /// 清环：全部失效
        /// </summary>
        public void Clear() {
            for (int i = 0; i < ring.Length; i++) {
                ring[i].Valid = false;
            }
        }

        /// <summary>
        /// 尝试捕获一帧；返回被写入的快照（调用方可继续写 <see cref="Snapshot.Extra"/>），未捕获返回 <see langword="null"/>
        /// </summary>
        /// <param name="rig">实例</param>
        /// <param name="strength">残影强度门控</param>
        public Snapshot Capture(Rig2DInstance rig, float strength) {
            if (rig == null || strength <= MinStrength) {
                return null;
            }
            uint now = Rig2DPlatform.Tick;
            if (now - lastCaptureTick > GapReset) {
                Clear();
            }
            if (lastCaptureTick != 0 && now - lastCaptureTick < MinInterval) {
                return null;
            }
            lastCaptureTick = now;

            head = (head + 1) % ring.Length;
            Snapshot s = ring[head];
            if (s.Bones.Length != rig.Bones.Length) {
                s.Bones = new Bone2D[rig.Bones.Length];
            }
            Array.Copy(rig.Bones, s.Bones, rig.Bones.Length);
            if (s.Extra.Length != extraCount) {
                s.Extra = new float[extraCount];
            }
            s.Tick = now;
            s.Valid = true;
            return s;
        }
    }
}
