using InnoVault.GameSystem;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// 拦截 UI 占用期间的滚轮换武器。与 <see cref="UIInputGuard"/> 配合，供所有 InnoVault UI 共用
    /// </summary>
    internal sealed class UIInputPlayer : PlayerOverride
    {
        /// <inheritdoc/>
        public override bool? CanSwitchWeapon()
            => UIInputGuard.WeaponSwitchSuppressTicks > 0 ? false : null;
    }
}
