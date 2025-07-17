using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace InnoVault.GameContent.BaseEntity
{
    /// <summary>
    /// 设定一个基本的手持弹幕类，包含了手持弹幕所需要的基本行为和工具，并自动处理必要的网络同步
    /// </summary>
    public abstract class BaseHeldProj : ModProjectile, ITetheredPlayer
    {
        #region Data
        private bool _initialize;
        private bool old_downLeftValue;
        private bool downLeftValue;
        private bool old_downRightValue;
        private bool downRightValue;
        private Vector2 _toMouseVecterDate;
        private Vector2 _old_toMouseVecterDate;
        private const float toMouseVer_variationModeSQ = 0.01f;
        /// <summary>
        /// 主纹理资源，应该在子类中被实际使用
        /// </summary>
        public virtual Texture2D TextureValue => TextureAssets.Projectile[Type].Value;
        /// <summary>
        /// 一般情况下我们默认该弹幕的玩家作为弹幕主人，因此，弹幕的<see cref="Projectile.owner"/>属性需要被正确设置
        /// </summary>
        public virtual Player Owner => Main.player[Projectile.owner];
        /// <summary>
        /// 手持物品实例
        /// </summary>
        public Item Item => Owner.GetItem();
        /// <summary>
        /// 玩家左键控制
        /// </summary>
        public bool DownLeft { get; set; }
        /// <summary>
        /// 玩家右键控制
        /// </summary>
        public bool DownRight { get; set; }
        /// <summary>
        /// 获取玩家到鼠标的向量
        /// </summary>
        public virtual Vector2 ToMouse { get; set; }
        /// <summary>
        /// 获取玩家鼠标的位置
        /// </summary>
        public virtual Vector2 InMousePos { get; set; }
        /// <summary>
        /// 获取玩家到鼠标的角度
        /// </summary>
        public virtual float ToMouseA { get; set; }
        /// <summary>
        /// 获取玩家鼠标的单位向量
        /// </summary>
        public virtual Vector2 UnitToMouseV { get; set; }
        /// <summary>
        /// 安全的获取一个重力倒转值
        /// </summary>
        public int SafeGravDir => Math.Sign(Owner.gravDir);
        /// <summary>
        /// 弹幕的理论朝向
        /// </summary>
        public virtual int DirSign => Owner.direction * SafeGravDir;
        /// <summary>
        /// 是否处于开火时间，默认为<see langword="true"/>
        /// </summary>
        public virtual bool CanFire => true;
        /// <summary>
        /// 是否处于活跃状态，默认为<see langword="false"/>
        /// </summary>
        public virtual bool CanMouseNet => false;
        /// <summary>
        /// 获得原射击方法信息
        /// </summary>
        public readonly static MethodInfo ItemCheck_Shoot_Method = typeof(Player).GetMethod("ItemCheck_Shoot", BindingFlags.NonPublic | BindingFlags.Instance);
        #endregion

        /// <summary>
        /// 单独处理玩家到鼠标的方向向量，同时处理对应的网络逻辑
        /// </summary>
        /// <returns></returns>
        private Vector2 UpdateToMouse() {
            if (Projectile.IsOwnedByLocalPlayer()) {
                _toMouseVecterDate = Owner.GetPlayerStabilityCenter().To(Main.MouseWorld);

                int grgDir = 1;
                if (ModLoader.HasMod("GravityDontFlipScreen") && SafeGravDir < 0) {
                    grgDir *= -1;
                }
                //因为重力翻转后进行了坐标变换，这里反转一下Y值以保证旋转角正常
                _toMouseVecterDate.Y *= grgDir;
                //避免很多不必要的敏感抖动发包
                bool difference = _toMouseVecterDate.DistanceSQ(_old_toMouseVecterDate) > toMouseVer_variationModeSQ;
                if (difference && (CanFire || CanMouseNet)) {
                    NetUpdate();
                }
                _old_toMouseVecterDate = _toMouseVecterDate;
            }
            return _toMouseVecterDate;
        }
        /// <summary>
        /// 处理左键点击的更新逻辑，同时处理对应的网络逻辑
        /// </summary>
        /// <returns></returns>
        private bool UpdateDownLeftStart() {
            if (Projectile.IsOwnedByLocalPlayer()) {
                downLeftValue = Owner.PressKey();
                if (old_downLeftValue != downLeftValue) {
                    NetUpdate();
                }
                old_downLeftValue = downLeftValue;
            }
            return downLeftValue;
        }
        /// <summary>
        /// 处理右键点击的更新逻辑，同时处理对应的网络逻辑
        /// </summary>
        /// <returns></returns>
        private bool UpdateDownRightStart() {
            if (Projectile.IsOwnedByLocalPlayer()) {
                downRightValue = Owner.PressKey(false);
                if (old_downRightValue != downRightValue) {
                    NetUpdate();
                }
                old_downRightValue = downRightValue;
            }
            return downRightValue;
        }

        /// <summary>
        /// 更新玩家到鼠标的相关数据
        /// </summary>
        private void UpdateMouseData() {
            DownLeft = UpdateDownLeftStart();
            DownRight = UpdateDownRightStart();
            ToMouse = UpdateToMouse();
            ToMouseA = ToMouse.ToRotation();
            UnitToMouseV = ToMouse.UnitVector();
            InMousePos = ToMouse + Owner.GetPlayerStabilityCenter();
        }
        /// <summary>
        /// 初始化函数，只在弹幕生成后调用一次，运行在<see cref="ExtraPreSet"/>之后
        /// </summary>
        public virtual void Initialize() {

        }
        /// <summary>
        /// 在AI更新前进行数据更新
        /// </summary>
        public sealed override bool PreAI() {
            UpdateMouseData();
            if (!ExtraPreSet()) {
                return false;
            }
            if (!_initialize) {
                Initialize();
                _initialize = true;
            }
            return PreUpdate();
        }
        /// <inheritdoc/>
        public sealed override void PostAI() {
            if (!Owner.Alives()) {
                Projectile.Kill();
                NetUpdate();
            }
        }
        /// <summary>
        /// 发送一个比特体，存储8个栏位的布尔值，
        /// 如果子类准备重写，需要尊重父类的使用逻辑，当前已经占用至1号位
        /// </summary>
        /// <param name="flags"></param>
        /// <returns></returns>
        [Obsolete("已经被弃用，应该使用 SendBitsByte ")]
        public virtual BitsByte SandBitsByte(BitsByte flags) {
            flags[0] = downLeftValue;
            flags[1] = downRightValue;
            return flags;
        }
        /// <summary>
        /// 发送一个比特体，存储8个栏位的布尔值，
        /// 如果子类准备重写，需要尊重父类的使用逻辑，当前已经占用至1号位
        /// </summary>
        /// <param name="flags"></param>
        /// <returns></returns>
        public virtual BitsByte SendBitsByte(BitsByte flags) {
            flags[0] = downLeftValue;
            flags[1] = downRightValue;
            return flags;
        }
        /// <summary>
        /// 接受一个比特体，最多处理8个布尔属性的网络更新，
        /// 如果子类准备重写，需要尊重父类的使用逻辑，当前已经占用至1号位
        /// </summary>
        /// <param name="flags"></param>
        public virtual void ReceiveBitsByte(BitsByte flags) {
            downLeftValue = flags[0];
            downRightValue = flags[1];
        }
        /// <inheritdoc/>
        public sealed override void SendExtraAI(BinaryWriter writer) {
            writer.WriteVector2(_toMouseVecterDate);
            writer.Write(SendBitsByte(new BitsByte()));
            NetHeldSend(writer);
        }
        /// <inheritdoc/>
        public sealed override void ReceiveExtraAI(BinaryReader reader) {
            _toMouseVecterDate = reader.ReadVector2();
            ReceiveBitsByte(reader.ReadByte());
            NetHeldReceive(reader);
        }
        /// <summary>
        /// 如果需要发送更多的网络数据，重写这个函数
        /// </summary>
        /// <param name="writer"></param>
        public virtual void NetHeldSend(BinaryWriter writer) {

        }
        /// <summary>
        /// 重写这个函数与<see cref="NetHeldSend(BinaryWriter)"/>的发送对应
        /// </summary>
        /// <param name="reader"></param>
        public virtual void NetHeldReceive(BinaryReader reader) {

        }
        /// <summary>
        /// 运行在最开始的时期，用于设置一些非常靠前的数据
        /// </summary>
        /// <returns>返回<see langword="false"/>可以阻止后续逻辑的运行</returns>
        public virtual bool ExtraPreSet() {
            return true;
        }
        /// <summary>
        /// 相当于<see cref="PreAI"/>的重写
        /// </summary>
        /// <returns></returns>
        public virtual bool PreUpdate() {
            return true;
        }
        /// <summary>
        /// 将弹幕设置为玩家的手持对象
        /// </summary>
        protected virtual void SetHeld() => Owner.heldProj = Projectile.whoAmI;
        /// <summary>
        /// 设置玩家朝向鼠标方向
        /// </summary>
        protected virtual void SetDirection() => Owner.direction = Math.Sign(ToMouse.X);
        /// <summary>
        /// 调用网络更新
        /// </summary>
        protected virtual void NetUpdate() => Projectile.netUpdate = true;
        /// <summary>
        /// 注意，该方法用于调用原物品的射击行为，会正常出发附加效果，比如饰品效果，所以在使用该函数时注意其他机制的运行避免效果重叠
        /// </summary>
        public virtual void OrigItemShoot() => ItemCheck_Shoot_Method.Invoke(Owner, [Owner.whoAmI, Item, Owner.GetWeaponDamage(Item)]);
    }
}
