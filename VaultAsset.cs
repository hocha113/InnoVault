using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace InnoVault
{
    /// <summary>
    /// 存储一些资源
    /// </summary>
    [VaultLoaden("Assets/")]
    public class VaultAsset : IVaultLoader
    {
        /// <summary>
        /// 透明素材无填充
        /// </summary>
        public static Asset<Texture2D> placeholder { get; set; }
        /// <summary>
        /// 透明素材白色填充
        /// </summary>
        public static Asset<Texture2D> placeholder2 { get; set; }
        /// <summary>
        /// 错误素材占位符
        /// </summary>
        public static Asset<Texture2D> placeholder3 { get; set; }
        /// <summary>
        /// 一个近十字光纹理
        /// </summary>
        public static Asset<Texture2D> Light { get; set; }
        /// <summary>
        /// 齿轮纹理
        /// </summary>
        public static Asset<Texture2D> GearWheel { get; set; }
        /// <summary>
        /// 空心齿轮纹理
        /// </summary>
        public static Asset<Texture2D> EmptyGearWheel { get; set; }
        /// <summary>
        /// 扳手纹理
        /// </summary>
        public static Asset<Texture2D> Spanner { get; set; }
    }
}
