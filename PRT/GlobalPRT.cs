using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace InnoVault.PRT
{
    /// <summary>
    /// 一个全局的粒子类，继承它用于修改全局粒子效果
    /// </summary>
    public abstract class GlobalPRT : VaultType
    {
        /// <summary>
        /// 所有全局粒子实例的列表
        /// </summary>
        public readonly static List<GlobalPRT> Instance = [];
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }
            Instance.Add(this);
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public override void SetupContent() {
            if (!CanLoad()) {
                return;
            }
            SetStaticDefaults();
        }
        /// <summary>
        /// 当粒子被创建到世界时调用，此时粒子已经被装载进实例列表
        /// </summary>
        /// <param name="prt"></param>
        public virtual void OnSpawn(BasePRT prt) {

        }
        /// <summary>
        /// 每帧调用一次，用于在所有粒子更新之前进行一些操作，返回<see langword="false"/>会阻止粒子的原版更新逻辑
        /// </summary>
        /// <returns></returns>
        public virtual bool PreUpdatePRTAll() {
            return true;
        }
        /// <summary>
        /// 每帧调用一次，用于在所有粒子更新之后进行一些操作
        /// </summary>
        public virtual void PostUpdatePRTAll() {

        }
        /// <summary>
        /// 每帧调用一次，用于在粒子绘制之前进行一些操作，返回<see langword="false"/>会阻止粒子的原版绘制逻辑
        /// </summary>
        /// <returns></returns>
        public virtual bool PreDrawPRT(SpriteBatch spriteBatch, BasePRT prt) {
            return true;
        }
        /// <summary>
        /// 每帧调用一次，用于在粒子绘制之后进行一些操作
        /// </summary>
        public virtual void PostDrawPRT(SpriteBatch spriteBatch, BasePRT prt) {

        }
    }
}
