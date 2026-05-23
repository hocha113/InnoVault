using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示 MTL 文件中描述的一份材质
    /// <br/>本 MVP 仅消费 <c>Kd</c>、<c>map_Kd</c>、<c>d</c>/<c>Tr</c> 字段，其它字段被忽略
    /// </summary>
    public sealed class ObjMaterial
    {
        /// <summary>
        /// 材质名（对应 OBJ 中的 <c>usemtl</c>）
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 漫反射颜色（来自 MTL 的 <c>Kd</c>），未声明时为白色
        /// </summary>
        public Color DiffuseColor { get; set; } = Color.White;

        /// <summary>
        /// 不透明度（来自 MTL 的 <c>d</c> 或 <c>1 - Tr</c>），未声明时为 1
        /// </summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>
        /// 漫反射贴图相对路径（来自 MTL 的 <c>map_Kd</c>），可能为 <see langword="null"/>
        /// <br/>路径已经过相对解析处理，可直接配合 <see cref="Terraria.ModLoader.Mod.Assets"/> 使用
        /// </summary>
        public string DiffuseTexturePath { get; set; }

        /// <summary>
        /// 已加载的漫反射贴图 <see cref="Texture2D"/>
        /// <br/>由 <see cref="ObjModelLoadenHandle"/> 在加载阶段填充，未提供贴图时为 <see langword="null"/>
        /// </summary>
        public Texture2D DiffuseTexture { get; set; }

        /// <summary>
        /// 构造一个新的材质，名字一旦确定即不可更改
        /// </summary>
        public ObjMaterial(string name) {
            Name = name ?? string.Empty;
        }

        /// <summary>
        /// 是否拥有可绘制的漫反射贴图
        /// </summary>
        public bool HasTexture => DiffuseTexture != null;
    }
}
