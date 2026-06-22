using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Actors
{
    /// <summary>
    /// 全局Actor基类，用于对所有Actor进行全局性的修改和扩展
    /// </summary>
    public abstract class GlobalActor : VaultType<GlobalActor>
    {
        /// <summary>
        /// 在Actor的AI逻辑执行之前调用，返回false可以阻止Actor的AI执行
        /// </summary>
        /// <param name="actor">要处理的Actor实例</param>
        /// <returns>如果返回false，则阻止Actor的AI执行</returns>
        public virtual bool PreAI(Actor actor) {
            return true;
        }
        /// <summary>
        /// 在Actor的AI逻辑执行之后调用
        /// </summary>
        /// <param name="actor">要处理的Actor实例</param>
        public virtual void PostAI(Actor actor) {

        }
        /// <summary>
        /// 在Actor生成到世界中时调用
        /// </summary>
        /// <param name="actor">生成的Actor实例</param>
        public virtual void OnSpawn(Actor actor) {

        }
        /// <summary>
        /// 在Actor绘制之前调用，返回false可以阻止Actor的绘制
        /// </summary>
        /// <param name="spriteBatch">用于绘制的SpriteBatch</param>
        /// <param name="actor">要绘制的Actor实例</param>
        /// <param name="drawColor">绘制颜色</param>
        /// <returns>如果返回false，则阻止Actor的绘制</returns>
        public virtual bool PreDraw(SpriteBatch spriteBatch, Actor actor, Color drawColor) {
            return true;
        }
        /// <summary>
        /// 在Actor绘制之后调用
        /// </summary>
        /// <param name="spriteBatch">用于绘制的SpriteBatch</param>
        /// <param name="actor">要绘制的Actor实例</param>
        /// <param name="drawColor">绘制颜色</param>
        public virtual void PostDraw(SpriteBatch spriteBatch, Actor actor, Color drawColor) {

        }
        /// <summary>
        /// 当任意玩家被某个 <see cref="SolidActor"/> 承载时调用（在该实体自身 <see cref="SolidActor.CarryPlayer"/> 之后）
        /// <br>用于实现跨实体的全局承载效果（例如全局风场、统一的承载手感修正），通过继续累加
        /// <see cref="SolidActorCarryContext.Displacement"/> 贡献位移</br>
        /// </summary>
        /// <param name="actor">承载玩家的实体</param>
        /// <param name="ctx">承载上下文</param>
        public virtual void CarryPlayer(SolidActor actor, ref SolidActorCarryContext ctx) {

        }
        /// <summary>
        /// 注册内容
        /// </summary>
        protected sealed override void VaultRegister() {
            Instances.Add(this);
        }
        /// <summary>
        /// 封闭内容
        /// </summary>
        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }
    }
}