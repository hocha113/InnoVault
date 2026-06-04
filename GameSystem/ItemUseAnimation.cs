using InnoVault.Cinematics;
using InnoVault.VaultNetworks;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 物品使用动画的工厂基类，以单实例形式存在<br/>
    /// 继承此类（通常是其内置风格子类 <see cref="AimedHoldAnimation"/> 或 <see cref="MeleeSwingAnimation"/>）
    /// 并设置 <see cref="TargetID"/> 与相关属性，即可在该物品被使用时自动接管其使用动画，
    /// 无需在物品类或 <see cref="ItemOverride"/> 中手写 UseStyle / UseItemFrame<br/>
    /// 子类会被 tModLoader 自动加载并注册（自动注册路径）；也可以通过 <see cref="Register(int, ItemUseAnimation)"/>
    /// 在运行时显式绑定一个实例到某个物品类型（显式注册路径，优先级更高）
    /// </summary>
    public abstract class ItemUseAnimation : VaultType<ItemUseAnimation>
    {
        #region Data
        /// <summary>
        /// 所有已注册的动画实例集合
        /// </summary>
        public new static List<ItemUseAnimation> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public new static Dictionary<Type, ItemUseAnimation> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 自动注册路径：从目标物品类型映射到对应的动画实例<br/>
        /// 与 <see cref="ItemOverride.ByID"/> 不同，这里每个物品只对应一个动画实例，
        /// 因为同一物品的使用动画不应当被多个系统同时接管（会争抢 <see cref="Player.itemRotation"/> 等状态）
        /// </summary>
        public new static Dictionary<int, ItemUseAnimation> ByID { get; internal set; } = [];
        /// <summary>
        /// 显式注册路径：从目标物品类型映射到显式绑定的动画实例，查表时优先于 <see cref="ByID"/>
        /// </summary>
        public static Dictionary<int, ItemUseAnimation> ExplicitByID { get; internal set; } = [];
        /// <summary>
        /// 该动画作用的目标物品类型，默认为 <see cref="ItemID.None"/> 表示不生效
        /// </summary>
        public virtual int TargetID => ItemID.None;
        #endregion

        /// <summary>
        /// 封闭注册逻辑，按 <see cref="TargetID"/> 将实例登记到静态表中
        /// </summary>
        protected sealed override void VaultRegister() {
            if (TargetID <= ItemID.None) {
                return;
            }
            Instances.Add(this);
            TypeToInstance[GetType()] = this;
            ByID[TargetID] = this;
        }

        /// <summary>
        /// 是否启用该动画，返回 <see langword="false"/> 时本帧完全跳过、放行原版与其它逻辑，默认返回 <see langword="true"/><br/>
        /// 这是动画的总开关，可在此根据任意条件决定是否启用——若需要在某些第三方模组接管了使用动画时主动让位，
        /// 应在子类中重写本方法自行判断；框架本身不内置任何针对特定模组的兼容处理
        /// </summary>
        public virtual bool Active(Item item, Player player) => true;

        /// <summary>
        /// 是否在当前状态下播放动画，用于处理物品自身的使用分支门控<br/>
        /// 例如某些物品在特定使用分支（右键、蓄力、持续引导等）下需要跳过常规动画，默认返回 <see langword="true"/>
        /// </summary>
        public virtual bool ShouldAnimate(Item item, Player player) => true;

        /// <summary>
        /// 综合门控：同时满足 <see cref="Active"/> 与 <see cref="ShouldAnimate"/> 时才会运行动画
        /// </summary>
        public bool CanRun(Item item, Player player)
            => Active(item, player) && ShouldAnimate(item, player);

        /// <summary>
        /// 获取动画使用的瞄准世界坐标<br/>
        /// 本地玩家直接返回实时 <see cref="Main.MouseWorld"/>；多人环境下的远程玩家会通过
        /// <see cref="PlayerNetwork"/> 续订并读取其同步的鼠标方向，重建出近似瞄准点，
        /// 使其它客户端也能看到正确的持握 / 瞄准朝向，各动画无需自行实现鼠标同步<br/>
        /// 如需精确的远程鼠标世界坐标或自定义瞄准来源，可重写此方法
        /// </summary>
        public virtual Vector2 GetAimWorldPosition(Player player) {
            if (player.whoAmI == Main.myPlayer) {
                return Main.MouseWorld;
            }
            //远程玩家：续订一次短期兴趣（内部按冷却节流，不会每帧发包），读取其同步鼠标方向重建的近似坐标
            PlayerNetwork.KeepAlive(player, PlayerNetworkDataFlags.BasicAim);
            if (PlayerNetwork.TryGetApproxMouseWorld(player, out Vector2 mouseWorld)) {
                return mouseWorld;
            }
            //首个同步快照到达前的回退
            return Main.MouseWorld;
        }

        /// <summary>
        /// 修改物品使用过程中的位置与旋转（对应 <see cref="Terraria.ModLoader.GlobalItem.UseStyle"/>），由风格子类实现
        /// </summary>
        public virtual void ApplyUseStyle(Item item, Player player, Rectangle heldItemFrame) { }

        /// <summary>
        /// 修改物品使用过程中的手臂 / 身体姿态（对应 <see cref="Terraria.ModLoader.GlobalItem.UseItemFrame"/>），由风格子类实现
        /// </summary>
        public virtual void ApplyUseItemFrame(Item item, Player player) { }

        /// <summary>
        /// 尝试根据物品类型获取已注册的动画实例，显式注册（<see cref="ExplicitByID"/>）优先于自动注册（<see cref="ByID"/>）
        /// </summary>
        public static bool TryGetByID(int itemType, out ItemUseAnimation animation) {
            if (ExplicitByID.TryGetValue(itemType, out animation)) {
                return true;
            }
            return ByID.TryGetValue(itemType, out animation);
        }

        /// <summary>
        /// 获取指定类型的动画实例（其单例）
        /// </summary>
        public static T GetAnimation<T>() where T : ItemUseAnimation
            => TypeToInstance.TryGetValue(typeof(T), out var instance) ? instance as T : null;

        /// <summary>
        /// 显式注册路径（预留入口）：在运行时将一个动画实例绑定到指定物品类型<br/>
        /// 适用于不便为某个物品单独写一个动画子类、希望直接复用已配置实例的场景
        /// </summary>
        /// <param name="itemType">目标物品类型</param>
        /// <param name="animation">要绑定的动画实例</param>
        public static void Register(int itemType, ItemUseAnimation animation) {
            if (itemType > ItemID.None && animation != null) {
                ExplicitByID[itemType] = animation;
            }
        }
    }

    /// <summary>
    /// 内置动画风格之一：瞄准持握<br/>
    /// 实现"武器朝鼠标方向持握 + 复合前臂跟随 + 可选开火后坐力 / 起手摆动 + 行走帧微调"，
    /// 适用于枪械、法器等需要朝鼠标方向持握瞄准的物品<br/>
    /// 继承本类，设置 <see cref="ItemUseAnimation.TargetID"/> 与下列属性即可完成适配；
    /// 需要多人鼠标同步时重写 <see cref="ItemUseAnimation.GetAimWorldPosition"/>，
    /// 需要在特定使用分支下跳过动画时重写 <see cref="ItemUseAnimation.ShouldAnimate"/>
    /// </summary>
    public abstract class AimedHoldAnimation : ItemUseAnimation
    {
        /// <summary>
        /// 武器持握时的中心相对玩家稳定中心的距离（像素），沿武器旋转方向偏移，默认 0
        /// </summary>
        public virtual float HoldDistance => 0f;

        /// <summary>
        /// 持握精灵相对其中心的原点偏移，用于把握把对准手部，默认 <see cref="Vector2.Zero"/>
        /// </summary>
        public virtual Vector2 HoldOrigin => Vector2.Zero;

        /// <summary>
        /// 持握锚定计算所用的精灵尺寸，为 <see langword="null"/> 时取物品自身尺寸 <see cref="Terraria.Entity.Size"/><br/>
        /// 当武器的绘制精灵尺寸与物品 hitbox 尺寸不一致时，重写此项返回真实精灵尺寸即可得到正确的持握锚点
        /// </summary>
        public virtual Vector2? HoldSpriteSize => null;

        /// <summary>
        /// 开火后坐力的最大回退距离（像素），为 0 时不产生后坐力，默认 0
        /// </summary>
        public virtual float RecoilStrength => 0f;

        /// <summary>
        /// 后坐力发生在使用动画的前段占比（0 到 1），仅在 <see cref="ItemUseAnimUtils.GetUseProgress"/> 小于该值时回退，默认 1/3
        /// </summary>
        public virtual float RecoilPhase => 1f / 3f;

        /// <summary>
        /// 当前是否启用开火后坐力，默认 <see langword="true"/>（即只要 <see cref="RecoilStrength"/> 大于 0 就回退）<br/>
        /// 可重写以按运行时状态临时关闭后坐力，例如持续照射 / 引导类使用分支下保持枪口稳定
        /// </summary>
        public virtual bool RecoilActive(Item item, Player player) => true;

        /// <summary>
        /// 起手摆动的最大角度幅度（弧度），为 0 时手臂直接指向鼠标、不做摆动，默认 0
        /// </summary>
        public virtual float SwingStrength => 0f;

        /// <summary>
        /// 起手摆动发生在使用动画的前段占比（0 到 1），仅在进度小于该值时叠加摆动，默认 0.4
        /// </summary>
        public virtual float SwingPhase => 0.4f;

        /// <summary>
        /// 是否在玩家行走帧时对持握位置做轻微纵向微调，默认 <see langword="true"/>
        /// </summary>
        public virtual bool EnableWalkFrameNudge => true;

        /// <summary>
        /// 设置物品使用时的旋转与位置：武器朝鼠标方向锚定，并在开火前段沿瞄准方向产生后坐力回退
        /// </summary>
        public override void ApplyUseStyle(Item item, Player player, Rectangle heldItemFrame) {
            Vector2 aimPos = GetAimWorldPosition(player);
            player.ChangeDir(Math.Sign(player.To(aimPos).X));

            float rotation = player.compositeFrontArm.rotation + MathHelper.PiOver2 * player.gravDir;
            Vector2 position = player.GetPlayerStabilityCenter() + rotation.ToRotationVector2() * HoldDistance;

            if (RecoilStrength > 0f && RecoilPhase > 0f && RecoilActive(item, player)) {
                float progress = ItemUseAnimUtils.GetUseProgress(player);
                if (progress < RecoilPhase) {
                    float kick = (RecoilPhase - progress) / RecoilPhase * RecoilStrength;
                    position -= player.To(aimPos).UnitVector() * kick;
                }
            }

            ItemUseAnimUtils.ApplyAnchoredHold(player, rotation, position, HoldSpriteSize ?? item.Size, HoldOrigin, EnableWalkFrameNudge);
        }

        /// <summary>
        /// 设置物品使用时的手臂姿态：复合前臂跟随鼠标方向，并在起手前段叠加摆动偏移，最后交由 <see cref="ApplyBackArm"/> 处理副手
        /// </summary>
        public override void ApplyUseItemFrame(Item item, Player player) {
            Vector2 aimPos = GetAimWorldPosition(player);
            player.ChangeDir(Math.Sign(player.To(aimPos).X));

            float progress = ItemUseAnimUtils.GetUseProgress(player);
            float rotation = (player.Center - aimPos).ToRotation() * player.gravDir + MathHelper.PiOver2;

            if (SwingStrength > 0f && SwingPhase > 0f && progress < SwingPhase) {
                float swingPhaseNorm = (SwingPhase - progress) / SwingPhase;
                float swingPower = swingPhaseNorm * swingPhaseNorm;
                rotation += -SwingStrength * swingPower * player.direction;
            }

            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, rotation);

            ApplyBackArm(item, player, rotation, progress);
        }

        /// <summary>
        /// 后手（副手）复合臂姿态的扩展钩子，在前手姿态设置完成后调用，默认空实现<br/>
        /// 重写此方法可通过 <see cref="Player.SetCompositeArmBack"/> 驱动副手动作，例如开火后的上膛 / 拉栓手势；
        /// 框架不内置任何具体手势，<see cref="Player.CompositeArmStretchAmount"/> 的取值映射由实现方自行决定
        /// </summary>
        /// <param name="item">正在使用的物品</param>
        /// <param name="player">使用物品的玩家</param>
        /// <param name="frontArmRotation">已叠加起手摆动后的前手旋转，便于副手相对前手做偏移</param>
        /// <param name="progress">当前使用进度（0 到 1），由 <see cref="ItemUseAnimUtils.GetUseProgress"/> 给出</param>
        protected virtual void ApplyBackArm(Item item, Player player, float frontArmRotation, float progress) { }
    }

    /// <summary>
    /// 内置动画风格之一：近战挥砍<br/>
    /// 用缓动曲线驱动武器在一段弧线内挥出，并根据武器朝向（俯仰）挑选身体帧，使挥砍更具个性<br/>
    /// 身体帧在 <see cref="ApplyUseItemFrame"/> 中设置（该时机晚于原版的帧计算，从而能稳定覆盖）；
    /// 武器旋转与位置在 <see cref="ApplyUseStyle"/> 中设置<br/>
    /// 说明：原版对 <see cref="Terraria.ID.ItemUseStyleID.Swing"/> 自身也会计算旋转，
    /// 因此挥砍弧线的最终观感可能需要在游戏内微调；如需对挥砍路径做完全接管，可在子类或后续追加 IL 钩子
    /// </summary>
    public abstract class MeleeSwingAnimation : ItemUseAnimation
    {
        /// <summary>
        /// 武器绘制缩放，默认 1
        /// </summary>
        public virtual float ItemScale => 1f;

        /// <summary>
        /// 挥砍覆盖的总弧度，默认 <see cref="MathHelper.Pi"/>（半圈）
        /// </summary>
        public virtual float SwingArc => MathHelper.Pi;

        /// <summary>
        /// 挥砍进度所用的缓动曲线，默认三次方减速（起手快、收尾缓）
        /// </summary>
        public virtual CutsceneEase SwingEase => CutsceneEase.CubicOut;

        /// <summary>
        /// 是否每次挥砍交替左右方向，默认 <see langword="true"/>
        /// </summary>
        public virtual bool FlipAttackEachSwing => true;

        /// <summary>
        /// 是否在挥砍时联动腿部姿态，默认 <see langword="false"/>；
        /// 启用后会调用 <see cref="AnimateLegFrame"/>，默认实现为空，可由子类重写以自定义腿部动作
        /// </summary>
        public virtual bool AnimateLegs => false;

        //按玩家槽位记录挥砍交替状态与上一帧的动画计时，用于检测新挥砍的开始
        private static readonly bool[] flipState = new bool[Main.maxPlayers + 1];
        private static readonly int[] lastItemAnimation = new int[Main.maxPlayers + 1];

        /// <summary>
        /// 获取挥砍的基准朝向角度，默认朝向 <see cref="ItemUseAnimation.GetAimWorldPosition"/>
        /// </summary>
        public virtual float GetSwingBaseAngle(Player player) => player.To(GetAimWorldPosition(player)).ToRotation();

        /// <summary>
        /// 根据进度计算当前挥砍旋转：在 [基准角 - 弧/2, 基准角 + 弧/2] 之间按缓动插值，并按朝向 / 交替翻转
        /// </summary>
        public virtual float GetSwingRotation(Item item, Player player, float progress) {
            float eased = ItemUseAnimUtils.EvaluateEase(SwingEase, progress);
            float baseAngle = GetSwingBaseAngle(player);
            float half = SwingArc * 0.5f;
            float min = baseAngle - half;
            float max = baseAngle + half;

            bool flipped = FlipAttackEachSwing && flipState[player.whoAmI];
            if ((player.direction < 0) ^ flipped) {
                (min, max) = (max, min);
            }

            return MathHelper.Lerp(min, max, eased);
        }

        /// <summary>
        /// 设置挥砍时的武器旋转与位置
        /// </summary>
        public override void ApplyUseStyle(Item item, Player player, Rectangle heldItemFrame) {
            UpdateFlipState(player);

            float progress = ItemUseAnimUtils.GetAnimationProgress(player);
            float rotation = GetSwingRotation(item, player, progress);

            player.itemRotation = MathHelper.WrapAngle(rotation) + MathHelper.PiOver4;
            if (player.direction < 0) {
                player.itemRotation += MathHelper.PiOver2;
            }

            player.itemLocation = player.GetPlayerStabilityCenter() + rotation.ToRotationVector2() * (item.Size.X * 0.5f * ItemScale);
        }

        /// <summary>
        /// 设置挥砍时的身体帧（根据武器俯仰挑选），可选地联动腿部
        /// </summary>
        public override void ApplyUseItemFrame(Item item, Player player) {
            float progress = ItemUseAnimUtils.GetAnimationProgress(player);
            float rotation = GetSwingRotation(item, player, progress);
            float pitch = ItemUseAnimUtils.RadiansToPitch(rotation);

            ApplyBodyFrame(player, pitch);

            if (AnimateLegs) {
                AnimateLegFrame(player, progress);
            }
        }

        /// <summary>
        /// 根据俯仰把身体帧设置为对应的挥砍姿态（高位上举 / 中位 / 低位下挥）
        /// </summary>
        protected static void ApplyBodyFrame(Player player, float pitch) {
            int frame;
            if (pitch > 0.66f) {
                frame = 4;//高位：上举
            }
            else if (pitch > 0.33f) {
                frame = 2;//中位
            }
            else {
                frame = 3;//低位：下挥
            }
            player.bodyFrame.Y = player.bodyFrame.Height * frame;
        }

        /// <summary>
        /// 腿部姿态联动的钩子，仅在 <see cref="AnimateLegs"/> 为 <see langword="true"/> 时调用；
        /// 默认实现为空，作为扩展点供子类重写
        /// </summary>
        protected virtual void AnimateLegFrame(Player player, float progress) { }

        //检测到使用动画计时回升（新一轮挥砍开始）时翻转交替状态
        private static void UpdateFlipState(Player player) {
            int current = player.itemAnimation;
            if (current > lastItemAnimation[player.whoAmI]) {
                flipState[player.whoAmI] = !flipState[player.whoAmI];
            }
            lastItemAnimation[player.whoAmI] = current;
        }
    }
}
