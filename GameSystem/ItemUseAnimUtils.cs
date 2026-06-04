using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 物品使用动画框架的共享数学工具<br/>
    /// 收口持握 / 挥砍中通用的进度、锚定与俯仰计算，供各动画风格复用
    /// </summary>
    public static class ItemUseAnimUtils
    {
        /// <summary>
        /// 获取基于 <see cref="Player.itemTime"/> 的使用进度，范围 0（刚开始）到 1（结束）<br/>
        /// 瞄准持握风格的后坐力 / 摆动偏移使用该进度
        /// </summary>
        public static float GetUseProgress(Player player) {
            if (player.itemTimeMax <= 0) {
                return 1f;
            }
            return 1f - player.itemTime / (float)player.itemTimeMax;
        }

        /// <summary>
        /// 获取基于 <see cref="Player.itemAnimation"/> 的动画进度，范围 0（刚开始）到 1（结束）<br/>
        /// 近战挥砍风格使用该进度，与原版挥砍的计时口径一致
        /// </summary>
        public static float GetAnimationProgress(Player player) {
            if (player.itemAnimationMax <= 0) {
                return 1f;
            }
            return 1f - player.itemAnimation / (float)player.itemAnimationMax;
        }

        //固定握距（像素）：精灵中心沿枪管再向手部一侧额外推移的量
        private const float GripPadding = 10f;

        /// <summary>
        /// 将武器精灵稳定地锚定到玩家手部一侧（"清爽持握"）：在给定旋转下沿枪管长轴把精灵推到握持点，
        /// 叠加自定义握点偏移与行走台阶补偿后，写入 <see cref="Player.itemRotation"/> 与 <see cref="Player.itemLocation"/>
        /// </summary>
        /// <param name="player">玩家</param>
        /// <param name="rotation">武器朝向旋转（未做朝向翻转修正）</param>
        /// <param name="anchorWorldPos">武器锚定的世界坐标，通常为玩家稳定中心加偏移</param>
        /// <param name="spriteSize">用于锚定计算的精灵尺寸</param>
        /// <param name="gripOffset">握点相对精灵中心的偏移，会按朝向 / 重力镜像</param>
        /// <param name="enableWalkFrameNudge">是否在行走台阶帧做纵向补偿，避免持握抖动</param>
        public static void ApplyAnchoredHold(Player player, float rotation, Vector2 anchorWorldPos, Vector2 spriteSize, Vector2 gripOffset, bool enableWalkFrameNudge = true) {
            int facing = player.direction;

            //面朝左时精灵被水平镜像，旋转整体 +π 才能让枪口仍指向原方向；该角度同时决定精灵的绘制朝向
            float drawRotation = facing < 0 ? rotation + MathHelper.Pi : rotation;
            player.itemRotation = drawRotation;

            //沿枪管长轴把锚点从精灵中心推向手部：推移量 = 半个精灵长 + 固定握距。
            //此处取原始 rotation 的轴向即可——面朝左引入的 +π 翻转与方向符号在数学上正好相互抵消
            float reachAlongBarrel = spriteSize.X * 0.5f + GripPadding;
            Vector2 gripShift = rotation.ToRotationVector2() * -reachAlongBarrel;

            //自定义握点偏移：先按朝向 / 重力镜像，再随最终旋转一起转动
            Vector2 mirroredGrip = new Vector2(gripOffset.X * facing, gripOffset.Y * player.gravDir);
            Vector2 gripCorrection = mirroredGrip.RotatedBy(drawRotation);

            //仅需把精灵在垂直方向居中到锚点（水平方向的居中量在推移与收尾间已自行抵消，无需再加）
            Vector2 location = anchorWorldPos + gripShift - gripCorrection;
            location.Y -= spriteSize.Y * 0.5f;

            //行走抬腿的台阶帧里身体会上抬，持握随之下压以稳定视觉
            if (enableWalkFrameNudge && IsSteppingFrame(player)) {
                location.Y -= 2f;
            }

            player.itemLocation = location;
        }

        //玩家行走动画中身体上抬的"台阶帧"区间，持握需在这些帧做纵向补偿
        private static bool IsSteppingFrame(Player player) {
            int frame = player.bodyFrame.Y / player.bodyFrame.Height;
            return frame is (> 6 and < 10) or (> 13 and < 17);
        }

        /// <summary>
        /// 将一个弧度旋转映射为 0 到 1 的"挥砍高度"（俯仰）<br/>
        /// 0 表示武器指向下方，1 表示指向上方（屏幕坐标 Y 轴向下），用于挑选挥砍时的身体帧
        /// </summary>
        public static float RadiansToPitch(float radians) {
            float sin = (float)Math.Sin(radians);
            return MathHelper.Clamp((1f - sin) * 0.5f, 0f, 1f);
        }

        /// <summary>
        /// 使用 <see cref="CutsceneEase"/> 缓动曲线对进度求值，复用 InnoVault 既有的缓动实现<br/>
        /// 近战挥砍风格用它把线性进度重映射为更有手感的挥砍曲线
        /// </summary>
        public static float EvaluateEase(CutsceneEase ease, float progress) => CutsceneEaseHelper.Evaluate(ease, progress);
    }
}
