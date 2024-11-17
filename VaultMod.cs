using System.Collections.Generic;
using System.IO;
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
                foreach (var loader in Loaders) {
                    loader.LoadAsset();
                }
            }
        }
        /// <inheritdoc/>
        public override void Unload() {
            foreach (var loader in Loaders) {
                loader.UnLoadData();
            }
            Loaders.Clear();
        }
        /// <inheritdoc/>
        public override void HandlePacket(BinaryReader reader, int whoAmI) => VaultNetWork.HandlePacket(this, reader, whoAmI);
    }
}
