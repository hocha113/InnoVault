using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 存储一些资源
    /// </summary>
    public class VaultAsset : IVaultLoader
    {
        /// <summary>
        /// 透明素材无填充
        /// </summary>
        public static Asset<Texture2D> placeholder { get; internal set; }
        /// <summary>
        /// 透明素材白色填充
        /// </summary>
        public static Asset<Texture2D> placeholder2 { get; internal set; }
        /// <summary>
        /// 错误素材占位符
        /// </summary>
        public static Asset<Texture2D> placeholder3 { get; internal set; }
        void IVaultLoader.LoadAsset() {
            placeholder = ModContent.Request<Texture2D>("InnoVault/Assets/placeholder");
            placeholder2 = ModContent.Request<Texture2D>("InnoVault/Assets/placeholder2");
            placeholder3 = ModContent.Request<Texture2D>("InnoVault/Assets/placeholder3");
        }
        void IVaultLoader.UnLoadData() {
            placeholder = null;
            placeholder2 = null;
            placeholder3 = null;
        }
    }
}
