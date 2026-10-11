using System;
using System.Reflection;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //与 Subworld Library 共存：只经它的公开属性 SubworldSystem.Current 探测是否身处子世界，不依赖它的实现。
    //它的子世界里发起换舞台会让两边各自以为掌握着当前世界文件，所以那时拒绝切换
    internal static class SubworldLibraryInterop
    {
        private static bool _resolved;
        private static PropertyInfo _current;

        internal static bool AnySubworldActive() {
            if (!_resolved) {
                _resolved = true;
                try {
                    if (ModLoader.TryGetMod("SubworldLibrary", out Mod mod)) {
                        _current = mod.Code?.GetType("SubworldLibrary.SubworldSystem")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    }
                } catch (Exception ex) {
                    VaultMod.Instance?.Logger.Warn($"[Scenarios] Subworld Library detection failed: {ex.Message}");
                }
            }
            try {
                return _current?.GetValue(null) != null;
            } catch {
                return false;
            }
        }

        internal static void Reset() {
            _resolved = false;
            _current = null;
        }
    }
}
