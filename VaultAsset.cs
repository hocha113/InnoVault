using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

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
        [VaultLoaden("Assets/placeholder")]
        public static Asset<Texture2D> placeholder {  get; set; }
        /// <summary>
        /// 透明素材白色填充
        /// </summary>
        [VaultLoaden("Assets/placeholder2")]
        public static Asset<Texture2D> placeholder2 { get; set; }
        /// <summary>
        /// 错误素材占位符
        /// </summary>
        [VaultLoaden("Assets/placeholder3")]
        public static Asset<Texture2D> placeholder3 { get; set; }
        /// <summary>
        /// 一个近十字光纹理
        /// </summary>
        [VaultLoaden("Assets/Light")]
        public static Asset<Texture2D> Light { get; set; }
    }
}
