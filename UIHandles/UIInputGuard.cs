namespace InnoVault.UIHandles
{
    /// <summary>
    /// UI 输入占用守卫。滚轮滚动列表等 UI 交互时，Player.mouseInterface 不足以阻止原版滚轮换武器，<br/>
    /// 需要额外通过 <see cref="GameSystem.PlayerOverride.CanSwitchWeapon"/> 拦截。本类提供与 CWR
    /// <c>DontSwitchWeaponTime</c> 等价的帧计数抑制，由 <see cref="UIInputPlayer"/> 消费
    /// </summary>
    public static class UIInputGuard
    {
        /// <summary>剩余抑制滚轮换武器的 tick 数（每逻辑帧递减）</summary>
        public static int WeaponSwitchSuppressTicks { get; private set; }

        /// <summary>
        /// 请求在接下来的若干 tick 内禁止滚轮换武器。多次调用取较大剩余值，<br/>
        /// UI 打开期间应每帧调用一次（典型值 2），与滚轮是否刚好产生增量无关
        /// </summary>
        public static void SuppressWeaponSwitch(int ticks = 2) {
            if (ticks > WeaponSwitchSuppressTicks) {
                WeaponSwitchSuppressTicks = ticks;
            }
        }

        /// <summary>每逻辑帧递减一次（由 <see cref="VaultPlayer"/> 驱动）</summary>
        internal static void Tick() {
            if (WeaponSwitchSuppressTicks > 0) {
                WeaponSwitchSuppressTicks--;
            }
        }

        /// <summary>模组卸载时复位</summary>
        internal static void Reset() => WeaponSwitchSuppressTicks = 0;
    }
}
