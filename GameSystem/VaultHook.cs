using MonoMod.RuntimeDetour;
using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于挂载和存储钩子实例
    /// </summary>
    public class VaultHook : IVaultLoader
    {
        private static ConcurrentDictionary<(MethodBase, Delegate), Hook> _hooks = new ConcurrentDictionary<(MethodBase, Delegate), Hook>();
        /// <summary>
        /// 缓存的被添加过的钩子实例，目前没有其他作用，仅仅是延长生命周期
        /// </summary>
        public static ConcurrentDictionary<(MethodBase, Delegate), Hook> Hooks => _hooks;

        /// <summary>
        /// InnoVault 钩子的默认排序优先级<br/>
        /// 带 <see cref="DetourConfig"/> 的钩子整体位于无配置钩子（例如其他模组经 <c>MonoModHooks.Add</c> 挂载的钩子）外层<br/>
        /// 配合较高的优先级值，可确保 InnoVault 的覆盖派发先于它们执行，避免被外层钩子跳过 <c>orig</c> 而失效
        /// </summary>
        public const int DefaultHookPriority = 1000000;

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
            return ApplyAndCache(method, hookDelegate, hook);
        }

        /// <summary>
        /// 添加带排序优先级的钩子到指定方法<br/>
        /// 通过附加 <see cref="DetourConfig"/> 让钩子进入 MonoMod 配置链（整体外层于无配置钩子），<paramref name="priority"/> 越大越靠外层、越先执行<br/>
        /// 需要稳定压过其他模组经 <c>MonoModHooks.Add</c> 挂载的同方法钩子时，可使用 <see cref="DefaultHookPriority"/>
        /// </summary>
        /// <param name="method">要钩住的方法</param>
        /// <param name="hookDelegate">钩子委托</param>
        /// <param name="priority">钩子排序优先级，数值越大越靠外层、越先执行</param>
        /// <param name="id">钩子配置标识，默认为 InnoVault</param>
        /// <returns>创建的Hook对象</returns>
        public static Hook Add(MethodBase method, Delegate hookDelegate, int priority, string id = null) {
            if (method == null) {
                throw new ArgumentException("The MethodBase passed in is Null");
            }
            if (hookDelegate == null) {
                throw new ArgumentException("The HookDelegate passed in is Null");
            }

            //附加 DetourConfig 让该钩子进入 MonoMod 配置链，整体位于无配置钩子外层，priority 再细化配置链内部顺序
            DetourConfig config = new DetourConfig(id ?? "InnoVault", priority);
            Hook hook = new Hook(method, hookDelegate, config);
            return ApplyAndCache(method, hookDelegate, hook);
        }

        //统一应用并缓存钩子实例，延长其生命周期
        private static Hook ApplyAndCache(MethodBase method, Delegate hookDelegate, Hook hook) {
            if (!hook.IsApplied) {
                hook.Apply();
            }
            if (!_hooks.TryAdd((method, hookDelegate), hook)) {
                VaultMod.Instance.Logger.Info("The target method is already mounted by the delegate");
            }
            return hook;
        }

        /// <summary>
        /// 检测钩子的挂载状态，如果没有任何异常将返回<see langword="true"/>，否则返回<see langword="false"/>
        /// </summary>
        /// <returns></returns>
        public static bool CheckHookStatus(out int hookDownNum) {
            hookDownNum = 0;

            foreach (var hook in _hooks.Values) {
                if (!hook.IsApplied) {
                    VaultMod.Instance.Logger.Info((hook + "Mount failure"));
                    hookDownNum++;
                }
            }

            return hookDownNum == 0;
        }

        void IVaultLoader.UnLoadData() {
            foreach (var hook in _hooks.Values) {
                if (hook == null) {
                    continue;
                }
                if (hook.IsApplied) {
                    hook.Undo();
                }
                hook.Dispose();
            }
            _hooks.Clear();
        }
    }
}
