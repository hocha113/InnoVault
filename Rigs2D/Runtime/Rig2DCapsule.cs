using Microsoft.Xna.Framework;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一个胶囊（线段 + 半径）的世界量快照：判定、受击盒、攻击体积共用的形状单元
    /// <br/>由 <see cref="Rig2DHit.GatherGroup"/> 从骨架的胶囊组取，也可以手工构造（武器、飞行物）
    /// </summary>
    public readonly struct Rig2DCapsule
    {
        /// <summary>
        /// 一端
        /// </summary>
        public readonly Vector2 A;
        /// <summary>
        /// 另一端
        /// </summary>
        public readonly Vector2 B;
        /// <summary>
        /// 半径（世界像素）
        /// </summary>
        public readonly float Radius;
        /// <summary>
        /// 来源序号（组内序号、骨索引，由构造方定义；手工构造缺省 -1）
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// 建一个胶囊
        /// </summary>
        public Rig2DCapsule(Vector2 a, Vector2 b, float radius, int index = -1) {
            A = a;
            B = b;
            Radius = radius;
            Index = index;
        }

        /// <summary>
        /// 圆（两端重合的胶囊）
        /// </summary>
        public static Rig2DCapsule Circle(Vector2 center, float radius, int index = -1) => new(center, center, radius, index);

        /// <summary>
        /// 中点
        /// </summary>
        public Vector2 Center => (A + B) * 0.5f;

        /// <summary>
        /// 线段长
        /// </summary>
        public float Length => Vector2.Distance(A, B);

        /// <summary>
        /// 平移后的副本
        /// </summary>
        public Rig2DCapsule Offset(Vector2 delta) => new(A + delta, B + delta, Radius, Index);

        /// <summary>
        /// 外接矩形（整数像素，向外取整）
        /// </summary>
        public Rectangle Bounds {
            get {
                float minX = MathHelper.Min(A.X, B.X) - Radius;
                float minY = MathHelper.Min(A.Y, B.Y) - Radius;
                float maxX = MathHelper.Max(A.X, B.X) + Radius;
                float maxY = MathHelper.Max(A.Y, B.Y) + Radius;
                int x = (int)System.MathF.Floor(minX);
                int y = (int)System.MathF.Floor(minY);
                return new Rectangle(x, y, (int)System.MathF.Ceiling(maxX) - x, (int)System.MathF.Ceiling(maxY) - y);
            }
        }
    }

    /// <summary>
    /// 一处接触：接触点、法线（从对方指向己方 / 从地面指向物体）、穿深（正 = 已重叠）
    /// </summary>
    public struct Rig2DContact
    {
        /// <summary>
        /// 接触点（世界）：两表面之间的中点，贴地接触取地面上的点
        /// </summary>
        public Vector2 Point;
        /// <summary>
        /// 单位法线：把己方推离对方的方向
        /// </summary>
        public Vector2 Normal;
        /// <summary>
        /// 穿深（像素）：正 = 重叠量；负 = 仍有间隙（推测接触）
        /// </summary>
        public float Depth;

        /// <summary>
        /// 建一处接触
        /// </summary>
        public Rig2DContact(Vector2 point, Vector2 normal, float depth) {
            Point = point;
            Normal = normal;
            Depth = depth;
        }
    }

    /// <summary>
    /// 两组胶囊之间最深的一处接触及双方的组内序号
    /// </summary>
    public struct Rig2DGroupContact
    {
        /// <summary>
        /// 接触（法线从 B 组指向 A 组）
        /// </summary>
        public Rig2DContact Contact;
        /// <summary>
        /// A 组里命中的胶囊序号
        /// </summary>
        public int IndexA;
        /// <summary>
        /// B 组里命中的胶囊序号
        /// </summary>
        public int IndexB;
    }
}
