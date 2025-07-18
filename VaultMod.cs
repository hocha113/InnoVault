using InnoVault.GameSystem;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.Map;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 模组的主类
    /// </summary>
    public class VaultMod : Mod
    {
        /// <summary>
        /// 实时获取整个模组的实例
        /// </summary>
        public static VaultMod Instance => (VaultMod)ModLoader.GetMod("InnoVault");
        /// <summary>
        /// 所有继承了<see cref="IVaultLoader"/>接口的类的实例
        /// </summary>
        public static List<IVaultLoader> Loaders { get; private set; } = new List<IVaultLoader>();
        /// <summary>
        /// 用于模组源查找的性能优化，在加载完成后会立即释放
        /// </summary>
        internal static readonly Dictionary<Assembly, HashSet<Type>> ModTypeSetCache = new();
        /// <summary>
        /// 用于模组总程序集查找的性能优化，在加载完成后会立即释放
        /// </summary>
        internal static Type[] AnyModCodeType = null;
        /// <summary>
        /// 存储模组内报错时间戳
        /// </summary>
        internal static readonly Dictionary<string, DateTime> lastErrorByKey = [];
        /// <summary>
        /// 报错节流冷却间隔
        /// </summary>
        internal const int LogCooldownSeconds = 5;
        /// <inheritdoc/>
        public override void Load() {
            Loaders = VaultUtils.GetSubInterface<IVaultLoader>();
            foreach (var loader in Loaders) {
                loader.LoadData();
            }
            VaultLoad.LoadData();
        }

        /// <inheritdoc/>
        public override void PostSetupContent() {
            foreach (var loader in Loaders) {
                loader.SetupData();
            }
            if (!Main.dedServ) {
                VaultLoad.LoadAsset();
                foreach (var loader in Loaders) {
                    loader.LoadAsset();
                }
            }
            //完成加载后就释放，防止在游戏周期中占用不必要的内存
            ModTypeSetCache?.Clear();
            AnyModCodeType = null;
        }

        /// <inheritdoc/>
        public override void Unload() {
            foreach (var loader in Loaders) {
                loader.UnLoadData();
            }
            Loaders.Clear();
            VaultLoad.UnLoadData();
            VaultLoad.UnLoadAsset();
            TagCache.Clear();
            ModTypeSetCache?.Clear();
            AnyModCodeType = null;
            lastErrorByKey?.Clear();
        }

        internal static void LoggerError(string key, string msg) {
            if (lastErrorByKey.TryGetValue(key, out var time)
                && (DateTime.UtcNow - time).TotalSeconds < LogCooldownSeconds) {
                return; //同类错误短时间内忽略
            }
            Instance.Logger.Error(msg);
            lastErrorByKey[key] = DateTime.UtcNow;
        }

        /// <inheritdoc/>
        public override void HandlePacket(BinaryReader reader, int whoAmI) => VaultNetWork.HandlePacket(this, reader, whoAmI);
    }
}
