using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 关键帧播放头：把片段采样结果写成实例的局部覆写（旋转 / 偏移 / 骨长），随后由静息传播与求解器接手
    /// <br/>支持单轨播放、停止淡出与两段之间的交叉淡入淡出；逐骨权重掩码可把关键帧只作用在部分骨骼上，
    /// 其余骨骼继续由程序化求解器驱动
    /// <br/>由 <see cref="Rig2DInstance.Step"/> 自动推进与应用，消费方只需 <see cref="Play(string, float)"/> / <see cref="Stop"/>
    /// </summary>
    public sealed class Rig2DClipPlayer
    {
        private readonly Rig2DInstance rig;
        private Rig2DClip current;
        private Rig2DClip previous;
        private float time;
        private float prevTime;
        private float fadeT = 1f;
        private float fadeLen;
        private float[] boneWeight = [];
        private bool[] touched = [];
        private float[] scratchRot = [];
        private Vector2[] scratchOff = [];
        private float[] scratchLen = [];
        private byte[] scratchMask = [];

        internal Rig2DClipPlayer(Rig2DInstance rig) {
            this.rig = rig;
        }

        /// <summary>
        /// 当前片段（<see langword="null"/> 表示空闲）
        /// </summary>
        public Rig2DClip Current => current;
        /// <summary>
        /// 当前片段的播放时间（帧）
        /// </summary>
        public float Time {
            get => time;
            set => time = value;
        }
        /// <summary>
        /// 播放速度倍率
        /// </summary>
        public float Speed { get; set; } = 1f;
        /// <summary>
        /// 主权重 0..1：关键帧姿态相对静息姿态的混入量
        /// </summary>
        public float Weight { get; set; } = 1f;
        /// <summary>
        /// 是否有片段在播放或淡出中
        /// </summary>
        public bool IsActive => current != null || previous != null;
        /// <summary>
        /// 非循环片段是否已播到末尾
        /// </summary>
        public bool Finished => current != null && !current.Loop && time >= current.Duration;
        /// <summary>
        /// 当前片段的归一化进度 0..1
        /// </summary>
        public float Progress => current == null || current.Duration <= 0f ? 0f : MathHelper.Clamp(current.WrapTime(time) / current.Duration, 0f, 1f);

        /// <summary>
        /// 按名播放定义里的片段；找不到时记日志并忽略
        /// </summary>
        /// <param name="clipName">片段名</param>
        /// <param name="fadeFrames">与当前姿态的交叉淡入帧数</param>
        public void Play(string clipName, float fadeFrames = 0f) {
            Rig2DClip clip = Find(clipName);
            if (clip == null) {
                VaultMod.LoggerError($"[Rig2D:{rig.Name}]", $"clip '{clipName}' not found");
                return;
            }
            Play(clip, fadeFrames);
        }

        /// <summary>
        /// 播放片段对象（可以不在定义里，但其轨道必须已对本骨架 <see cref="Rig2DClip.Resolve"/>）
        /// </summary>
        public void Play(Rig2DClip clip, float fadeFrames = 0f) {
            if (clip == null) {
                return;
            }
            if (ReferenceEquals(clip, current)) {
                time = 0f;
                return;
            }
            EnsureResolved(clip);
            previous = current;
            prevTime = time;
            current = clip;
            time = 0f;
            BeginFade(fadeFrames);
        }

        /// <summary>
        /// 停止播放，可选淡出回静息姿态
        /// </summary>
        public void Stop(float fadeFrames = 0f) {
            if (current == null) {
                return;
            }
            previous = current;
            prevTime = time;
            current = null;
            BeginFade(fadeFrames);
        }

        /// <summary>
        /// 设置某骨骼的关键帧权重（0 = 该骨完全交给程序化，1 = 完全关键帧）
        /// </summary>
        public void SetBoneWeight(int bone, float weight) {
            EnsureArrays();
            if (bone >= 0 && bone < boneWeight.Length) {
                boneWeight[bone] = MathHelper.Clamp(weight, 0f, 1f);
            }
        }

        /// <summary>
        /// 全部骨骼权重复位为 1
        /// </summary>
        public void ResetBoneWeights() {
            EnsureArrays();
            Array.Fill(boneWeight, 1f);
        }

        private Rig2DClip Find(string name) {
            if (rig.Definition == null || string.IsNullOrEmpty(name)) {
                return null;
            }
            foreach (Rig2DClip c in rig.Definition.Clips) {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) {
                    return c;
                }
            }
            return null;
        }

        private void EnsureResolved(Rig2DClip clip) {
            for (int i = 0; i < clip.Tracks.Count; i++) {
                if (clip.Tracks[i].BoneIndex < 0) {
                    clip.Resolve(rig.Definition);
                    return;
                }
            }
        }

        private void BeginFade(float frames) {
            if (frames <= 0f) {
                fadeT = 1f;
                fadeLen = 0f;
                previous = null;
                return;
            }
            fadeT = 0f;
            fadeLen = frames;
        }

        private void EnsureArrays() {
            int n = rig.Bones.Length;
            if (boneWeight.Length != n) {
                boneWeight = new float[n];
                Array.Fill(boneWeight, 1f);
                touched = new bool[n];
                scratchRot = new float[n];
                scratchOff = new Vector2[n];
                scratchLen = new float[n];
                scratchMask = new byte[n];
            }
        }

        internal void Advance(float dt) {
            if (current == null && previous == null) {
                return;
            }
            time += dt * Speed;
            if (previous != null) {
                prevTime += dt * Speed;
            }
            if (fadeT < 1f) {
                fadeT = fadeLen > 0f ? Math.Min(1f, fadeT + dt / fadeLen) : 1f;
                if (fadeT >= 1f) {
                    previous = null;
                }
            }
        }

        //掩码位：1 旋转 2 偏移 4 骨长
        internal void Apply() {
            EnsureArrays();
            //先把上一帧写过的覆写清掉，本帧没有采样到的骨骼回到定义值
            for (int b = 0; b < touched.Length; b++) {
                if (touched[b]) {
                    rig.ClearBoneOverrides(b);
                    touched[b] = false;
                }
            }
            if (current == null && previous == null) {
                return;
            }
            Array.Clear(scratchMask);
            Rig2DDefinition def = rig.Definition;

            //上一段（淡出）先铺底，再用当前段按 fadeT 混过去；只有一段时对静息姿态混
            if (previous != null) {
                Sample(previous, previous.WrapTime(prevTime), def, blendIn: false);
            }
            if (current != null) {
                Sample(current, current.WrapTime(time), def, blendIn: previous != null);
            }

            float master = MathHelper.Clamp(Weight, 0f, 1f);
            //淡出到空：整体权重随 fadeT 收敛到 0
            float fadeScale = current == null ? 1f - fadeT : (previous == null ? fadeT : 1f);
            for (int b = 0; b < scratchMask.Length; b++) {
                byte m = scratchMask[b];
                if (m == 0) {
                    continue;
                }
                float w = master * boneWeight[b] * fadeScale;
                if (w <= 0.0001f) {
                    continue;
                }
                Bone2DDef bd = def.Bones[b];
                if ((m & 1) != 0) {
                    float rest = bd.Rotation;
                    rig.SetBoneLocalRotation(b, rest + MathHelper.WrapAngle(scratchRot[b] - rest) * w);
                }
                if ((m & 2) != 0) {
                    rig.SetBoneLocalOffset(b, Vector2.Lerp(bd.Offset, scratchOff[b], w));
                }
                if ((m & 4) != 0) {
                    rig.SetBoneLocalLength(b, MathHelper.Lerp(bd.Length, scratchLen[b], w));
                }
                touched[b] = true;
            }
        }

        private void Sample(Rig2DClip clip, float t, Rig2DDefinition def, bool blendIn) {
            for (int i = 0; i < clip.Tracks.Count; i++) {
                Rig2DTrack track = clip.Tracks[i];
                int b = track.BoneIndex;
                if (b < 0 || b >= scratchMask.Length) {
                    continue;
                }
                Bone2DDef bd = def.Bones[b];
                if (track.SampleRotation(t, out float rot)) {
                    if (blendIn) {
                        float from = (scratchMask[b] & 1) != 0 ? scratchRot[b] : bd.Rotation;
                        scratchRot[b] = from + MathHelper.WrapAngle(rot - from) * fadeT;
                    }
                    else {
                        scratchRot[b] = rot;
                    }
                    scratchMask[b] |= 1;
                }
                if (track.SampleOffset(t, out Vector2 off)) {
                    if (blendIn) {
                        Vector2 from = (scratchMask[b] & 2) != 0 ? scratchOff[b] : bd.Offset;
                        scratchOff[b] = Vector2.Lerp(from, off, fadeT);
                    }
                    else {
                        scratchOff[b] = off;
                    }
                    scratchMask[b] |= 2;
                }
                if (track.SampleLength(t, out float len)) {
                    if (blendIn) {
                        float from = (scratchMask[b] & 4) != 0 ? scratchLen[b] : bd.Length;
                        scratchLen[b] = MathHelper.Lerp(from, len, fadeT);
                    }
                    else {
                        scratchLen[b] = len;
                    }
                    scratchMask[b] |= 4;
                }
            }
        }
    }
}
