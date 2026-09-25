using InnoVault.Rigs2D.Animation;
using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 实例上的通道值：动画层与消费方写这里，<see cref="Rig2DInstance.Step"/> 按各通道的绑定落到骨骼 / 求解器 / 件 / 带 / 根
    /// <br/>标量存在 X 分量。每个通道另有一份世界偏移（<see cref="SetWorldOffset"/>），只对空间型绑定生效，
    /// 用来叠加消费方按世界算的量（脚下地形高差、盆骨随地形下沉），不参与姿态插值
    /// </summary>
    public sealed class Rig2DChannels
    {
        private readonly Rig2DInstance rig;
        private Vector2[] values = [];
        private Vector2[] worldOffsets = [];

        internal Rig2DChannels(Rig2DInstance rig) {
            this.rig = rig;
        }

        /// <summary>
        /// 通道数
        /// </summary>
        public int Count => values.Length;

        /// <summary>
        /// 按名查通道索引，缺失 <c>-1</c>
        /// </summary>
        public int Index(string name) => rig.Definition?.ChannelIndex(name) ?? -1;

        /// <summary>
        /// 读标量（越界 0）
        /// </summary>
        public float Get(int channel) => (uint)channel < (uint)values.Length ? values[channel].X : 0f;

        /// <summary>
        /// 读向量（越界零向量）
        /// </summary>
        public Vector2 GetVector(int channel) => (uint)channel < (uint)values.Length ? values[channel] : Vector2.Zero;

        /// <summary>
        /// 写标量
        /// </summary>
        public void Set(int channel, float value) {
            if ((uint)channel < (uint)values.Length) {
                values[channel].X = value;
            }
        }

        /// <summary>
        /// 写向量
        /// </summary>
        public void Set(int channel, Vector2 value) {
            if ((uint)channel < (uint)values.Length) {
                values[channel] = value;
            }
        }

        /// <summary>
        /// 整副写入一张姿态（长度不符时写重叠部分）
        /// </summary>
        public void Apply(Rig2DPose pose) {
            if (pose == null) {
                return;
            }
            int n = Math.Min(values.Length, pose.Count);
            Array.Copy(pose.Values, values, n);
        }

        /// <summary>
        /// 把当前值抄进一张姿态缓冲
        /// </summary>
        public void CopyTo(Rig2DPose pose) {
            if (pose == null) {
                return;
            }
            int n = Math.Min(values.Length, pose.Count);
            Array.Copy(values, pose.Values, n);
        }

        /// <summary>
        /// 回到通道缺省值（世界偏移一并清零）
        /// </summary>
        public void Reset() {
            Rig2DDefinition def = rig.Definition;
            for (int i = 0; i < values.Length; i++) {
                values[i] = def.Channels[i].Default;
            }
            Array.Clear(worldOffsets);
        }

        /// <summary>
        /// 写某通道的世界偏移（世界像素，不乘 Scale、不参与镜像）；持久，直到改写或 <see cref="ClearWorldOffsets"/>
        /// </summary>
        public void SetWorldOffset(int channel, Vector2 offset) {
            if ((uint)channel < (uint)worldOffsets.Length) {
                worldOffsets[channel] = offset;
            }
        }

        /// <summary>
        /// 某通道的世界偏移
        /// </summary>
        public Vector2 WorldOffset(int channel) => (uint)channel < (uint)worldOffsets.Length ? worldOffsets[channel] : Vector2.Zero;

        /// <summary>
        /// 全部世界偏移清零
        /// </summary>
        public void ClearWorldOffsets() => Array.Clear(worldOffsets);

        internal void Rebuild(Rig2DDefinition def) {
            int n = def?.Channels.Count ?? 0;
            if (values.Length != n) {
                values = new Vector2[n];
                worldOffsets = new Vector2[n];
            }
            for (int i = 0; i < n; i++) {
                values[i] = def.Channels[i].Default;
            }
            Array.Clear(worldOffsets);
        }
    }
}
