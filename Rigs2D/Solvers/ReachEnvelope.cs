using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 可达包络：把越界目标拉回边界，而不是让骨链去够
    /// <br/>"腿不许乱甩"的几何声明：半径窗 + 相对静息方向的摆角窗
    /// </summary>
    public static class ReachEnvelope
    {
        /// <summary>
        /// 半径钳制：目标到原点的距离压进 [<paramref name="minRadius"/>, <paramref name="maxRadius"/>]
        /// </summary>
        /// <param name="origin">原点（髋 / 肩）</param>
        /// <param name="target">目标</param>
        /// <param name="minRadius">下限</param>
        /// <param name="maxRadius">上限</param>
        /// <param name="fallbackDir">目标与原点重合时的方向</param>
        public static Vector2 ClampRadius(Vector2 origin, Vector2 target, float minRadius, float maxRadius, Vector2 fallbackDir) {
            Vector2 d = target - origin;
            float len = d.Length();
            if (len < 0.001f) {
                return origin + fallbackDir * minRadius;
            }
            float clamped = MathHelper.Clamp(len, minRadius, maxRadius);
            return origin + d * (clamped / len);
        }

        /// <summary>
        /// 半径 + 摆角钳制：目标压进原点周围以 <paramref name="restDir"/> 为中心、±<paramref name="maxSwing"/> 的扇形环带
        /// </summary>
        /// <param name="origin">原点</param>
        /// <param name="restDir">静息方向（单位向量）</param>
        /// <param name="target">目标</param>
        /// <param name="minRadius">半径下限</param>
        /// <param name="maxRadius">半径上限</param>
        /// <param name="maxSwing">相对静息方向的最大摆角（弧度）</param>
        public static Vector2 ClampSwing(Vector2 origin, Vector2 restDir, Vector2 target, float minRadius, float maxRadius, float maxSwing) {
            Vector2 d = target - origin;
            float len = d.Length();
            float restAng = (float)Math.Atan2(restDir.Y, restDir.X);
            if (len < 1f) {
                return origin + restDir * minRadius;
            }
            float ang = MathHelper.Clamp(MathHelper.WrapAngle((float)Math.Atan2(d.Y, d.X) - restAng), -maxSwing, maxSwing);
            len = MathHelper.Clamp(len, minRadius, maxRadius);
            float a = restAng + ang;
            return origin + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * len;
        }

        /// <summary>
        /// 目标是否在半径窗内
        /// </summary>
        public static bool InRadius(Vector2 origin, Vector2 target, float minRadius, float maxRadius) {
            float d2 = Vector2.DistanceSquared(origin, target);
            return d2 >= minRadius * minRadius && d2 <= maxRadius * maxRadius;
        }
    }
}
