using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D
{
    /// <summary>
    /// 核心用到的几个 <c>Terraria.Utils</c> 扩展的逐字复刻（同一公式、同一精度路径），核心因此不引用 Terraria 程序集而数值不变
    /// </summary>
    public static class Rig2DMath
    {
        /// <summary>
        /// 角 → 单位向量（同 <c>Utils.ToRotationVector2</c>：双精度三角函数再截成单精度）
        /// </summary>
        public static Vector2 Dir(float angle) => new((float)Math.Cos(angle), (float)Math.Sin(angle));

        /// <summary>
        /// 向量 → 角（同 <c>Utils.ToRotation</c>）
        /// </summary>
        public static float Angle(Vector2 v) => (float)Math.Atan2(v.Y, v.X);

        /// <summary>
        /// 安全归一化（同 <c>Utils.SafeNormalize</c>：零向量或含 NaN 时返回缺省值）
        /// </summary>
        public static Vector2 SafeNormalize(Vector2 v, Vector2 fallback) {
            if (v == Vector2.Zero || float.IsNaN(v.X) || float.IsNaN(v.Y)) {
                return fallback;
            }
            return Vector2.Normalize(v);
        }

        /// <summary>
        /// 逐通道相乘（同 <c>Utils.MultiplyRGBA</c>：按字节乘后除 255 截断）
        /// </summary>
        public static Color MultiplyRGBA(Color a, Color b) => new(
            (byte)((float)(a.R * b.R) / 255f),
            (byte)((float)(a.G * b.G) / 255f),
            (byte)((float)(a.B * b.B) / 255f),
            (byte)((float)(a.A * b.A) / 255f));
    }
}
