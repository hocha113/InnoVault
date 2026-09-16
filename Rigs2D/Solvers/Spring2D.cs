using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 弹簧与平滑原件（纯函数，无状态）
    /// <br/>两种口味：临界阻尼弹簧（角频率 ω rad/s，秒制步长，无过冲、跟手速度只由 ω 决定）
    /// 与速度弹簧（每帧 <c>vel = (vel + (target − pos) × spring) × damping</c>，有滞后 / 过冲 / 余摆，质量感来源）
    /// </summary>
    public static class Spring2D
    {
        /// <summary>
        /// 一帧的秒数（60fps 基准）
        /// </summary>
        public const float FrameSeconds = 1f / 60f;

        /// <summary>
        /// 临界阻尼标量弹簧
        /// </summary>
        /// <param name="pos">当前值</param>
        /// <param name="vel">当前速度</param>
        /// <param name="target">目标值</param>
        /// <param name="omega">角频率 rad/s（慢摆 9，瞬发 24）</param>
        /// <param name="dt">步长（秒）</param>
        public static void Critical(ref float pos, ref float vel, float target, float omega, float dt = FrameSeconds) {
            float x = pos - target;
            float temp = (vel + x * omega) * dt;
            float decay = MathF.Exp(-omega * dt);
            vel = (vel - temp * omega) * decay;
            pos = target + (x + temp) * decay;
        }

        /// <summary>
        /// 临界阻尼角度弹簧（按最短弧差追，结果回绕到 [−π, π]）
        /// </summary>
        public static void CriticalAngle(ref float pos, ref float vel, float target, float omega, float dt = FrameSeconds) {
            float x = MathHelper.WrapAngle(pos - target);
            float temp = (vel + x * omega) * dt;
            float decay = MathF.Exp(-omega * dt);
            vel = (vel - temp * omega) * decay;
            pos = MathHelper.WrapAngle(target + (x + temp) * decay);
        }

        /// <summary>
        /// 临界阻尼向量弹簧
        /// </summary>
        public static void Critical(ref Vector2 pos, ref Vector2 vel, Vector2 target, float omega, float dt = FrameSeconds) {
            Vector2 x = pos - target;
            Vector2 temp = (vel + x * omega) * dt;
            float decay = MathF.Exp(-omega * dt);
            vel = (vel - temp * omega) * decay;
            pos = target + (x + temp) * decay;
        }

        /// <summary>
        /// 速度弹簧（每帧一步）：<c>vel = (vel + (target − pos) × spring) × damping; pos += vel</c>
        /// </summary>
        /// <param name="pos">当前值</param>
        /// <param name="vel">当前速度</param>
        /// <param name="target">目标值</param>
        /// <param name="spring">刚度（0.1 ~ 0.4）</param>
        /// <param name="damping">阻尼（0.6 ~ 0.9，越小越快停）</param>
        public static void Velocity(ref Vector2 pos, ref Vector2 vel, Vector2 target, float spring, float damping) {
            vel = (vel + (target - pos) * spring) * damping;
            pos += vel;
        }

        /// <summary>
        /// 标量速度弹簧
        /// </summary>
        public static void Velocity(ref float pos, ref float vel, float target, float spring, float damping) {
            vel = (vel + (target - pos) * spring) * damping;
            pos += vel;
        }

        /// <summary>
        /// 角度按最短弧线性追近（<c>rate</c> 为每帧比例）
        /// </summary>
        public static float AngleLerp(float from, float to, float rate)
            => MathHelper.WrapAngle(from + MathHelper.WrapAngle(to - from) * MathHelper.Clamp(rate, 0f, 1f));

        /// <summary>
        /// 三次平滑阶跃 <c>t²(3 − 2t)</c>
        /// </summary>
        public static float SmoothStep01(float t) {
            t = MathHelper.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 按帧步长缩放的指数追近比例：<c>1 − (1 − rate)^dt</c>
        /// </summary>
        public static float RateForDt(float rate, float dt) {
            if (dt == 1f) {
                return rate;
            }
            return 1f - MathF.Pow(1f - MathHelper.Clamp(rate, 0f, 1f), Math.Max(dt, 0f));
        }
    }
}
