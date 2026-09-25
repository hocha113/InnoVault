using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 动画层的混合方式
    /// </summary>
    public enum Rig2DLayerBlend
    {
        /// <summary>
        /// 覆盖：按权重（与遮罩）把本层结果插进下层结果
        /// </summary>
        Override,
        /// <summary>
        /// 加性：本层结果当增量，乘权重后加上去（瞄准、呼吸、受击后仰）
        /// </summary>
        Additive,
    }

    /// <summary>
    /// 动画机的一层：播一个招式，或者挂一副由消费方逐帧改写的姿态
    /// <br/>时间由消费方写入（<see cref="Time"/> = 已同步的动作计时），或开 <see cref="AutoAdvance"/> 让动画机按步长推进
    /// </summary>
    public sealed class Rig2DAnimLayer
    {
        private readonly Rig2DAnimator owner;

        /// <summary>
        /// 层名
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// 混合方式
        /// </summary>
        public Rig2DLayerBlend Blend { get; set; } = Rig2DLayerBlend.Override;
        /// <summary>
        /// 权重（覆盖层 0~1，加性层为倍率）
        /// </summary>
        public float Weight { get; set; } = 1f;
        /// <summary>
        /// 遮罩：只作用于这些通道（下标同通道表）；<see langword="null"/> = 全部
        /// </summary>
        public int[] Mask { get; set; }
        /// <summary>
        /// 招式时间（帧）
        /// </summary>
        public float Time { get; set; }
        /// <summary>
        /// 自走倍率（<see cref="AutoAdvance"/> 时用）
        /// </summary>
        public float Speed { get; set; } = 1f;
        /// <summary>
        /// 由动画机按步长推进时间（<see cref="Rig2DAnimator.Hold"/> 时不推）；缺省关，时间由消费方写
        /// </summary>
        public bool AutoAdvance { get; set; }
        /// <summary>
        /// 当前招式（挂姿态时为空）
        /// </summary>
        public Rig2DMove Move { get; private set; }
        /// <summary>
        /// 当前挂的姿态（播招式时为空）；按引用持有，消费方可逐帧改写
        /// </summary>
        public Rig2DPose Pose { get; private set; }
        /// <summary>
        /// 起招那刻的底姿快照（招式里的 <c>@base</c>）
        /// </summary>
        public Rig2DPose StartPose { get; private set; }
        /// <summary>
        /// 本层最近一次的结果
        /// </summary>
        public Rig2DPose Result { get; private set; }
        /// <summary>
        /// 是否在出力
        /// </summary>
        public bool Active => Move != null || Pose != null;

        internal Rig2DAnimLayer(Rig2DAnimator owner, string name) {
            this.owner = owner;
            Name = name ?? string.Empty;
            Rebuild(owner.Rig.Definition);
        }

        internal void Rebuild(Rig2DDefinition def) {
            StartPose = new Rig2DPose(def);
            Result = new Rig2DPose(def);
            //热重载后招式对象换新：按名重取
            if (Move != null) {
                Move = def?.MoveValue(Move.Name);
            }
            if (Pose != null && Pose.Count != (def?.Channels.Count ?? 0)) {
                Pose = null;
            }
        }

        /// <summary>
        /// 播一个招式；<paramref name="start"/> 为空时拿动画机此刻的底姿（含运动层）当 <c>@base</c>
        /// </summary>
        public Rig2DAnimLayer Play(Rig2DMove move, float time = 0f, Rig2DPose start = null) {
            Move = move;
            Pose = null;
            Time = time;
            StartPose.CopyFrom(start ?? owner.SampleLocomotion());
            return this;
        }

        /// <summary>
        /// 按名播招式；找不到时记日志并停下
        /// </summary>
        public Rig2DAnimLayer Play(string moveName, float time = 0f, Rig2DPose start = null) {
            Rig2DMove move = owner.Rig.Definition?.MoveValue(moveName);
            if (move == null) {
                Rig2DPlatform.LogError($"[Rig2D:{owner.Rig.Name}]", $"move '{moveName}' not found");
                Stop();
                return this;
            }
            return Play(move, time, start);
        }

        /// <summary>
        /// 挂一副姿态（按引用）
        /// </summary>
        public Rig2DAnimLayer SetPose(Rig2DPose pose) {
            Pose = pose;
            Move = null;
            return this;
        }

        /// <summary>
        /// 停下（本层不再出力）
        /// </summary>
        public Rig2DAnimLayer Stop() {
            Move = null;
            Pose = null;
            return this;
        }

        /// <summary>
        /// 遮罩设为某通道组；组名为空或缺失时取消遮罩
        /// </summary>
        public Rig2DAnimLayer SetMask(string group) {
            int[] g = string.IsNullOrEmpty(group) ? null : owner.Rig.Definition?.ChannelGroup(group);
            Mask = g != null && g.Length > 0 ? g : null;
            return this;
        }

        /// <summary>
        /// 当前招式的事件是否落在 (<paramref name="from"/>, <paramref name="to"/>]
        /// </summary>
        public bool Crossed(string eventName, float from, float to) => Move != null && Move.Crossed(eventName, from, to);

        internal Rig2DPose Sample(float dt, bool hold) {
            if (AutoAdvance && !hold) {
                Time += dt * Speed;
            }
            if (Move != null) {
                Move.Sample(Time, StartPose, Result);
                return Result;
            }
            return Pose;
        }
    }

    /// <summary>
    /// 分层动画机：<see cref="Base"/>（消费方写的底姿，行走 / 站立 / 空中一类）→ 运动层 <see cref="Gait"/>（可空）→ 各层按顺序覆盖或加性叠加
    /// → 交叉淡化 → 地形适配（运动层开了 <see cref="Rig2DGait.TerrainWeight"/> 时）→ 写进实例通道
    /// <br/>交叉淡化从上一帧的<b>最终输出</b>快照（地形适配之前）淡到新结果（<see cref="CrossFade"/>，0 帧 = 硬切），<see cref="Hold"/>（顿帧）时淡化不推进
    /// <br/>两种用法：先 <see cref="Evaluate"/> 读 <see cref="Output"/> 再 <see cref="Rig2DInstance.Step"/>（Step 不会再求一次）；
    /// 或只 Step，动画机开着时自动求值
    /// </summary>
    public sealed class Rig2DAnimator
    {
        private readonly List<Rig2DAnimLayer> layers = [];
        private Rig2DPose last;
        private Rig2DPose fadeFrom;
        private bool hasLast;
        private float fadeLeft;
        private float fadeLen;

        /// <summary>
        /// 所属实例
        /// </summary>
        public Rig2DInstance Rig { get; }
        /// <summary>
        /// 是否参与 <see cref="Rig2DInstance.Step"/>（建第一层时自动打开）
        /// </summary>
        public bool Enabled { get; set; }
        /// <summary>
        /// 底姿（消费方逐帧写）
        /// </summary>
        public Rig2DPose Base { get; private set; }
        /// <summary>
        /// 最近一次的最终输出
        /// </summary>
        public Rig2DPose Output { get; private set; }
        /// <summary>
        /// 运动层（可空）：底姿之后、各层之前写进输出
        /// </summary>
        public Rig2DGait Gait { get; set; }
        /// <summary>
        /// 最近一次"底姿 + 运动层"的结果（各层之前）；动作层起招不给起点时就拿它当 <c>@base</c>
        /// </summary>
        public Rig2DPose Locomotion { get; private set; }
        /// <summary>
        /// 顿帧：淡化与自走层都不推进
        /// </summary>
        public bool Hold { get; set; }
        /// <summary>
        /// 是否在淡化中
        /// </summary>
        public bool Fading => fadeLeft > 0f;
        /// <summary>
        /// 全部层（按求值顺序）
        /// </summary>
        public IReadOnlyList<Rig2DAnimLayer> Layers => layers;
        /// <summary>
        /// 自上次 Step 以来是否已经求过值
        /// </summary>
        internal bool EvaluatedSinceStep { get; set; }

        internal Rig2DAnimator(Rig2DInstance rig) {
            Rig = rig;
            Rebuild(rig.Definition);
        }

        internal void Rebuild(Rig2DDefinition def) {
            Base = new Rig2DPose(def);
            Output = new Rig2DPose(def);
            Locomotion = new Rig2DPose(def);
            last = new Rig2DPose(def);
            fadeFrom = new Rig2DPose(def);
            hasLast = false;
            fadeLeft = 0f;
            foreach (Rig2DAnimLayer l in layers) {
                l.Rebuild(def);
            }
            //热重载后运动层定义换新：按名重取
            if (Gait?.Def != null) {
                Gait.Bind(def?.GaitValue(Gait.Def.Name));
            }
        }

        /// <summary>
        /// 按此刻的底姿与运动层现算一份 <see cref="Locomotion"/>（不推进任何状态）
        /// </summary>
        public Rig2DPose SampleLocomotion() {
            Locomotion.CopyFrom(Base);
            if (Gait != null && Gait.Weight > 0f) {
                Gait.Sample(Locomotion, Gait.Weight);
            }
            return Locomotion;
        }

        /// <summary>
        /// 取或建一层（新层排在最后）
        /// </summary>
        public Rig2DAnimLayer Layer(string name) {
            foreach (Rig2DAnimLayer l in layers) {
                if (string.Equals(l.Name, name, StringComparison.Ordinal)) {
                    return l;
                }
            }
            Rig2DAnimLayer layer = new(this, name);
            layers.Add(layer);
            Enabled = true;
            return layer;
        }

        /// <summary>
        /// 从上一帧的最终输出淡到之后的结果；<paramref name="frames"/> ≤ 0 或还没有输出过 = 硬切
        /// </summary>
        public void CrossFade(float frames) {
            if (!hasLast || frames <= 0f) {
                fadeLeft = 0f;
                return;
            }
            fadeFrom.CopyFrom(last);
            fadeLen = frames;
            fadeLeft = frames;
        }

        /// <summary>
        /// 求一次值并写进实例通道
        /// </summary>
        public Rig2DPose Evaluate(float dt = 1f) {
            Output.CopyFrom(SampleLocomotion());
            for (int i = 0; i < layers.Count; i++) {
                Rig2DAnimLayer l = layers[i];
                if (!l.Active || l.Weight <= 0f) {
                    continue;
                }
                Rig2DPose src = l.Sample(dt, Hold);
                if (src == null) {
                    continue;
                }
                if (l.Blend == Rig2DLayerBlend.Additive) {
                    if (l.Mask == null) {
                        Output.Add(src, l.Weight);
                    }
                    else {
                        Output.Add(src, l.Weight, l.Mask);
                    }
                    continue;
                }
                float w = MathF.Min(l.Weight, 1f);
                if (l.Mask == null) {
                    if (w >= 1f) {
                        Output.CopyFrom(src);
                    }
                    else {
                        Output.Lerp(Output, src, w);
                    }
                }
                else {
                    Output.Lerp(Output, src, w, l.Mask);
                }
            }
            if (fadeLeft > 0f) {
                float x = MathHelper.Clamp(1f - fadeLeft / fadeLen, 0f, 1f);
                Output.Lerp(fadeFrom, Output, x * x * (3f - 2f * x));
                //按步长推进：顿帧（Hold 或 Step(0)）时淡化停在原处
                if (!Hold && dt > 0f) {
                    fadeLeft -= dt;
                }
            }
            last.CopyFrom(Output);
            hasLast = true;
            if (Gait != null && Gait.TerrainWeight > 0f) {
                Gait.ApplyTerrain(Output);
            }
            Rig.Channels.Apply(Output);
            EvaluatedSinceStep = true;
            return Output;
        }
    }
}
