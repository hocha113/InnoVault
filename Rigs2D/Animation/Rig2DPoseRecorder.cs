using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 位姿录制：把实例每帧的程序化解算结果换算成各骨骼的局部量（旋转 / 偏移 / 骨长）写进关键帧片段，
    /// 让 IK / 步态 / 弹簧跑出来的动作可以被"冻结"成 clip 回放、导出 JSON、再手工修键
    /// <br/>用法：<c>Record(rig)</c> 每帧在 <c>rig.Step()</c> 之后调一次，<c>Finish()</c> 取片段；
    /// 回放时把被录骨骼的求解器 <c>Enabled = false</c>（或用 <see cref="Rig2DClipPlayer.SetBoneWeight"/> 压掉程序化）
    /// </summary>
    public sealed class Rig2DPoseRecorder
    {
        private readonly Rig2DClip clip;
        private readonly Rig2DTrack[] tracks;
        private readonly int[] bones;
        private readonly bool recordOffset;
        private readonly bool recordLength;
        private readonly int interval;
        private int frame;
        private int sinceLast;
        private bool finished;

        /// <summary>
        /// 已录帧数
        /// </summary>
        public int Frames => frame;
        /// <summary>
        /// 是否已结束
        /// </summary>
        public bool Finished => finished;

        /// <summary>
        /// 新建录制器
        /// </summary>
        /// <param name="rig">被录实例（决定骨骼表）</param>
        /// <param name="clipName">片段名</param>
        /// <param name="boneNames">要录的骨骼名；空或 <see langword="null"/> = 全部骨骼</param>
        /// <param name="interval">采样间隔（帧），1 = 逐帧</param>
        /// <param name="recordOffset">是否录局部偏移（链 / verlet 需要，纯旋转骨架可关）</param>
        /// <param name="recordLength">是否录骨长（拉伸骨需要）</param>
        public Rig2DPoseRecorder(Rig2DInstance rig, string clipName, IEnumerable<string> boneNames = null,
            int interval = 1, bool recordOffset = true, bool recordLength = true) {
            ArgumentNullException.ThrowIfNull(rig);
            Rig2DDefinition def = rig.Definition;
            List<int> picked = [];
            if (boneNames != null) {
                foreach (string name in boneNames) {
                    int b = def.BoneIndex(name);
                    if (b >= 0) {
                        picked.Add(b);
                    }
                }
            }
            if (picked.Count == 0) {
                for (int b = 0; b < def.BoneCount; b++) {
                    picked.Add(b);
                }
            }
            bones = picked.ToArray();
            clip = new Rig2DClip(clipName, 1f) { Loop = true };
            tracks = new Rig2DTrack[bones.Length];
            for (int i = 0; i < bones.Length; i++) {
                tracks[i] = clip.Track(def.Bones[bones[i]].Name);
            }
            this.interval = Math.Max(interval, 1);
            this.recordOffset = recordOffset;
            this.recordLength = recordLength;
        }

        /// <summary>
        /// 采一帧（在 <c>rig.Step()</c> 之后调用）
        /// </summary>
        public void Record(Rig2DInstance rig) {
            if (finished || rig == null || rig.Definition == null) {
                return;
            }
            if (sinceLast > 0) {
                sinceLast--;
                frame++;
                return;
            }
            sinceLast = interval - 1;
            float t = frame;
            float invScale = 1f / Math.Max(rig.Scale, 0.0001f);
            for (int i = 0; i < bones.Length; i++) {
                int b = bones[i];
                Bone2DDef d = rig.Definition.Bones[b];
                ref Bone2D bone = ref rig.Bones[b];
                Vector2 anchor;
                float parDir;
                if (d.ParentIndex < 0) {
                    anchor = rig.RootPosition;
                    parDir = rig.RootRotation;
                }
                else {
                    ref Bone2D p = ref rig.Bones[d.ParentIndex];
                    anchor = d.AtParentTip ? p.Tip : p.Pos;
                    parDir = p.Dir;
                }
                float rot = d.InheritRotation ? MathHelper.WrapAngle(bone.Dir - parDir) : bone.Dir;
                tracks[i].Rotation.Add(new Rig2DKey<float>(t, rot));
                if (recordOffset) {
                    Vector2 world = bone.Pos - anchor;
                    float cos = MathF.Cos(parDir);
                    float sin = MathF.Sin(parDir);
                    //世界差向量转回父骨骼局部系（x 沿父轴、y 沿父 Side），再除掉 Scale
                    Vector2 local = new Vector2(world.X * cos + world.Y * sin, -world.X * sin + world.Y * cos) * invScale;
                    tracks[i].Offset.Add(new Rig2DKey<Vector2>(t, local));
                }
                if (recordLength) {
                    tracks[i].Length.Add(new Rig2DKey<float>(t, bone.Length * invScale));
                }
            }
            frame++;
        }

        /// <summary>
        /// 结束录制并返回片段（时长 = 已录帧数）；可选把片段加进定义以便 <see cref="Rig2DJson.ToJsonText"/> 导出
        /// </summary>
        public Rig2DClip Finish(Rig2DDefinition addTo = null) {
            finished = true;
            clip.Duration = Math.Max(frame, 1);
            for (int i = 0; i < tracks.Length; i++) {
                tracks[i].Sort();
            }
            if (addTo != null) {
                clip.Resolve(addTo);
                addTo.Clips.RemoveAll(c => string.Equals(c.Name, clip.Name, StringComparison.Ordinal));
                addTo.Clips.Add(clip);
            }
            return clip;
        }
    }
}
