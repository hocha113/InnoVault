using Terraria.ModLoader;
using static InnoVault.UIHandles.UIHandleLoader;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// 对于UI的一个全局类，你可以使用它来进行一些统一的操作
    /// </summary>
    public abstract class GlobalUIHandle : VaultType<GlobalUIHandle>
    {
        /// <summary>
        /// 所属的Mod
        /// </summary>
        public new Mod Mod => TypeToMod[GetType()];

        /// <summary>
        /// 封闭内容
        /// </summary>
        protected override void VaultRegister() {
            UIHandleGlobalHooks.Add(this);
        }

        /// <summary>
        /// 加载内容
        /// </summary>
        public override void VaultSetup() {
            SetStaticDefaults();
        }

        /// <summary>
        /// 按键状态更新时运行
        /// </summary>
        public virtual void UpdateKeyState() {

        }

        /// <summary>
        /// UI元素更新前运行
        /// </summary>
        public virtual bool PreUIHanderElementUpdate(UIHandle handle) {
            return true;
        }

        /// <summary>
        /// UI元素更新后运行
        /// </summary>
        public virtual void PostUIHanderElementUpdate(UIHandle handle) {

        }

        /// <summary>
        /// 在所有UI元素更新后执行
        /// </summary>
        public virtual void PostUpdataInUIEverything() {

        }
    }
}
