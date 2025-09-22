using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于修改所有尘埃的行为
    /// </summary>
    public abstract class GlobalDust : VaultType
    {
        /// <summary>
        /// 所有已注册的实例
        /// </summary>
        public readonly static List<GlobalDust> Instance = [];
        /// <summary>
        /// 注册这个实例
        /// </summary>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }
            Instance.Add(this);
        }
        /// <summary>
        /// 封闭内容
        /// </summary>
        public override void SetupContent() {
            if (!CanLoad()) {
                return;
            }
            SetStaticDefaults();
        }
        /// <summary>
        /// 当粒子被创建到世界时调用，该钩子会在
        /// <see cref="Dust.NewDust(Vector2, int, int, int, float, float, int, Color, float)"/>
        /// 与
        /// <see cref="Dust.NewDustDirect(Vector2, int, int, int, float, float, int, Color, float)"/>
        /// 与
        /// <see cref="Dust.NewDustPerfect(Vector2, int, Vector2?, int, Color, float)"/>
        /// 运行之后被调用
        /// </summary>
        /// <param name="dust"></param>
        public virtual void OnSpawn(Dust dust) {

        }
        /// <summary>
        /// 每帧调用一次，用于在尘埃更新之前进行一些操作，返回<see langword="false"/>会阻止尘埃的原版更新逻辑
        /// </summary>
        /// <returns></returns>
        public virtual bool PreUpdateDustAll() {
            return true;
        }
        /// <summary>
        /// 每帧调用一次，用于在尘埃更新之后进行一些操作
        /// </summary>
        public virtual void PostUpdateDustAll() {

        }
        /// <summary>
        /// 每帧调用一次，用于在尘埃被绘制之前进行一些操作，返回<see langword="false"/>会阻止尘埃的原版绘制逻辑
        /// </summary>
        /// <returns></returns>
        public virtual bool PreDrawAll() {
            return true;
        }
        /// <summary>
        /// 每帧调用一次，用于在尘埃被绘制之后进行一些操作
        /// </summary>
        public virtual void PostDrawAll() {

        }
    }
}
