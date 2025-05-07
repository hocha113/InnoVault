using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
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
        /// <inheritdoc/>
        public override void Load() {
            Loaders = VaultUtils.GetSubInterface<IVaultLoader>();
            foreach (var loader in Loaders) {
                loader.LoadData();
            }
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
        }
        /// <inheritdoc/>
        public override void Unload() {
            foreach (var loader in Loaders) {
                loader.UnLoadData();
            }
            Loaders.Clear();
            VaultLoad.UnLoadAsset();
            ModTypeSetCache?.Clear();
        }
        /// <inheritdoc/>
        public override void HandlePacket(BinaryReader reader, int whoAmI) => VaultNetWork.HandlePacket(this, reader, whoAmI);
    }
}
