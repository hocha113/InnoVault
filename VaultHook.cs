using MonoMod.RuntimeDetour;
using System.Collections.Concurrent;
using System.Reflection;
using System;

namespace InnoVault
{
    internal class VaultHook : IVaultLoader
    {
        private static ConcurrentDictionary<(MethodBase, Delegate), Hook> _hooks = new ConcurrentDictionary<(MethodBase, Delegate), Hook>();
        /// <summary>
        /// 缓存的被添加过的钩子实例，目前没有其他作用，仅仅是延长生命周期
        /// </summary>
        public static ConcurrentDictionary<(MethodBase, Delegate), Hook> Hooks => _hooks;

        /// <summary>
        /// 添加钩子到指定方法
        /// </summary>
        /// <param name="method">要钩住的方法</param>
        /// <param name="hookDelegate">钩子委托</param>
        /// <returns>创建的Hook对象</returns>
        public static Hook Add(MethodBase method, Delegate hookDelegate) {
            if (method == null) {
                throw new ArgumentException("The MethodBase passed in is Null");
            }
            if (hookDelegate == null) {
                throw new ArgumentException("The HookDelegate passed in is Null");
            }

            Hook hook = new Hook(method, hookDelegate);

            if (!hook.IsApplied) {
                hook.Apply();
            }
            if (!_hooks.TryAdd((method, hookDelegate), hook)) {
                string ctext = "目标方法已经被该委托挂载";
                string egtext = "The target method is already mounted by the delegate";
                VaultMod.Instance.Logger.Info(VaultUtils.Translation(ctext, egtext));
            }
            return hook;
        }

        void IVaultLoader.UnLoadData() {
            foreach (var hook in _hooks.Values) {
                if (hook.IsApplied) {
                    hook.Undo();
                }
                hook.Dispose();
            }
            _hooks.Clear();
        }
    }
}
