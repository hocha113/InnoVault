using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一根骨骼的运行时世界位姿：近端位置、轴向角、有效长度
    /// <br/>所有量都是世界坐标（像素、弧度），求解器与渲染器直接消费；Scale 已乘进 <see cref="Length"/>
    /// </summary>
    public struct Bone2D
    {
        /// <summary>
        /// 近端关节的世界位置
        /// </summary>
        public Vector2 Pos;
        /// <summary>
        /// 轴向世界角（弧度），从近端指向尖端
        /// </summary>
        public float Dir;
        /// <summary>
        /// 有效骨长（像素，已含 Scale；被拉伸时与静息长不同）
        /// </summary>
        public float Length;

        /// <summary>
        /// 轴向单位向量
        /// </summary>
        public readonly Vector2 Forward => new((float)Math.Cos(Dir), (float)Math.Sin(Dir));
        /// <summary>
        /// 轴向顺时针转 90° 的单位向量（屏幕系 y 向下，即骨骼"右侧"）
        /// </summary>
        public readonly Vector2 Side => new(-(float)Math.Sin(Dir), (float)Math.Cos(Dir));
        /// <summary>
        /// 尖端世界位置
        /// </summary>
        public readonly Vector2 Tip => Pos + Forward * Length;
        /// <summary>
        /// 骨骼中点
        /// </summary>
        public readonly Vector2 Center => Pos + Forward * (Length * 0.5f);

        /// <summary>
        /// 直接构造
        /// </summary>
        public Bone2D(Vector2 pos, float dir, float length) {
            Pos = pos;
            Dir = dir;
            Length = length;
        }

        /// <summary>
        /// 用两点构造：近端 → 尖端
        /// </summary>
        public static Bone2D FromPoints(Vector2 from, Vector2 to) {
            Vector2 d = to - from;
            float len = d.Length();
            float dir = len > 0.0001f ? (float)Math.Atan2(d.Y, d.X) : 0f;
            return new Bone2D(from, dir, len);
        }

        /// <summary>
        /// 把局部点（x 沿轴向、y 沿 <see cref="Side"/>）变换到世界
        /// </summary>
        public readonly Vector2 ToWorld(Vector2 local) => Pos + Forward * local.X + Side * local.Y;

        /// <summary>
        /// 把世界点变换到本骨骼局部系
        /// </summary>
        public readonly Vector2 ToLocal(Vector2 world) {
            Vector2 d = world - Pos;
            Vector2 f = Forward;
            Vector2 s = Side;
            return new Vector2(Vector2.Dot(d, f), Vector2.Dot(d, s));
        }

        /// <summary>
        /// 调试显示
        /// </summary>
        public override readonly string ToString() => $"Bone2D(pos={Pos}, dir={Dir:F3}, len={Length:F1})";
    }
}
