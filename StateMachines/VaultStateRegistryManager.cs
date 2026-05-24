using System;
using System.Collections.Generic;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// "注册表的注册表"：托管所有<see cref="VaultStateRegistry{TContext}"/>的清理委托<br/>
    /// 由于 C# 的泛型静态无法被外部枚举（每个 <c>TContext</c> 是独立类型），<br/>
    /// 必须由各<see cref="VaultStateRegistry{TContext}"/>在首次注册时把<c>Clear()</c>登记进来，<br/>
    /// 卸载阶段（<see cref="VaultStateMachineLoader"/>的<c>UnLoadData</c>）才能"一次性清空所有泛型注册表"，<br/>
    /// 否则二次<c>Load</c>会与上次的工厂叠加导致 ID 冲突
    /// </summary>
    internal static class VaultStateRegistryManager
    {
        //使用 HashSet 而非 List：同一个 Clear 委托不应被重复登记
        private static readonly HashSet<Action> _clearActions = [];

        /// <summary>
        /// 由<see cref="VaultStateRegistry{TContext}"/>在首次<c>Register</c>时调用，登记自己的<c>Clear()</c>
        /// </summary>
        public static void RegisterClearAction(Action clear) {
            if (clear != null) {
                _clearActions.Add(clear);
            }
        }

        /// <summary>
        /// 调用所有已登记的清理委托，并清空 Manager 自身<br/>
        /// 仅供<see cref="VaultStateMachineLoader"/>在<c>UnLoadData</c>阶段使用
        /// </summary>
        public static void ClearAll() {
            foreach (Action action in _clearActions) {
                try {
                    action?.Invoke();
                } catch (Exception ex) {
                    VaultMod.LoggerError("VaultStateRegistryManager.ClearAll", $"Clear action threw: {ex}");
                }
            }
            _clearActions.Clear();
        }
    }
}
