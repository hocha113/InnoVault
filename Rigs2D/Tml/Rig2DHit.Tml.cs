using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    //游戏宿主：胶囊对矩形沿用原版 AABB 对线段判定
    public static partial class Rig2DHit
    {
        /// <summary>
        /// 骨骼胶囊是否与矩形相交（实体 hitbox 一类）
        /// </summary>
        /// <param name="bone">骨骼</param>
        /// <param name="radius">胶囊半径（像素）</param>
        /// <param name="rect">世界矩形</param>
        public static bool BoneCapsuleIntersects(in Bone2D bone, float radius, Rectangle rect) {
            if (bone.Length < 0.5f) {
                return CircleIntersects(bone.Pos, radius, rect);
            }
            float point = 0f;
            return Collision.CheckAABBvLineCollision(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height),
                bone.Pos, bone.Tip, Math.Max(radius * 2f, 1f), ref point);
        }

        /// <summary>
        /// 一组骨骼里第一根与矩形相交的胶囊（按给定顺序），命中返回其骨骼索引
        /// </summary>
        /// <param name="rig">实例</param>
        /// <param name="bones">要测的骨骼索引</param>
        /// <param name="radius">胶囊半径（Scale 为 1 的量，内部乘实例 Scale）</param>
        /// <param name="rect">世界矩形</param>
        /// <param name="hitBone">命中的骨骼索引，未命中为 -1</param>
        public static bool AnyBoneIntersects(Rig2DInstance rig, ReadOnlySpan<int> bones, float radius, Rectangle rect, out int hitBone) {
            hitBone = -1;
            if (rig == null) {
                return false;
            }
            float r = radius * Math.Max(rig.Scale, 0.001f);
            for (int k = 0; k < bones.Length; k++) {
                int b = bones[k];
                if (b < 0 || b >= rig.Bones.Length) {
                    continue;
                }
                if (BoneCapsuleIntersects(in rig.Bones[b], r, rect)) {
                    hitBone = b;
                    return true;
                }
            }
            return false;
        }
    }
}
