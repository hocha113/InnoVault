using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示 MTL 文件中描述的一份材质
    /// <br/>本 MVP 仅消费 <c>Kd</c>、<c>map_Kd</c>、<c>d</c>/<c>Tr</c> 字段，其它字段被忽略
    /// <br/>除导入数据外，材质还可挂载若干"材质级覆盖项"：<see cref="Effect"/> / <see cref="EffectProvider"/>
    /// / <see cref="ConfigureEffect"/> / <see cref="RenderStateOverride"/> / <see cref="PreDrawGroup"/> / <see cref="PostDrawGroup"/>，
    /// 用于"所有使用该材质的 mesh group 都套上同一份 shader 或状态覆盖"的场景
    /// <br/>这些覆盖项的优先级<b>低于</b> <see cref="Runtime.Model3DInstance"/> 上的同名字段
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

        //========================================================================
        // 高级扩展：材质级 Effect / RenderState / 生命周期回调
        // 优先级低于 Model3DInstance 上的同名字段
        //========================================================================

        /// <summary>
        /// 材质级自定义 <see cref="Microsoft.Xna.Framework.Graphics.Effect"/>
        /// <br/>当 <see cref="Model3DInstance.Effect"/> 与 <see cref="Model3DInstance.EffectProvider"/> 都为 <see langword="null"/> 时生效
        /// <br/>典型用途：让所有使用同一材质（例如 "<c>glass</c>"）的 mesh group 自动套同一 shader
        /// <br/><b>注意</b>：自定义 Effect 时渲染器不会自动写光照 / Tint / Texture，需在 <see cref="ConfigureEffect"/> 中处理；
        /// World / View / Projection 矩阵会自动写入（当 Effect 实现 <see cref="IEffectMatrices"/>）
        /// </summary>
        public Effect Effect { get; set; }

        /// <summary>
        /// 材质级 Effect 提供者；与 <see cref="Effect"/> 互斥，前者非空时优先使用前者
        /// </summary>
        public IModel3DEffectProvider EffectProvider { get; set; }

        /// <summary>
        /// 材质级 Effect 参数配置回调；在实例级 <see cref="Model3DInstance.ConfigureEffect"/> 之后调用
        /// </summary>
        public Model3DConfigureEffect ConfigureEffect { get; set; }

        /// <summary>
        /// 材质级渲染状态覆盖；不为 <see langword="null"/> 的字段覆盖桶级默认，
        /// 但仍然会被 <see cref="Model3DInstance.RenderStateOverride"/> 进一步覆盖
        /// </summary>
        public Model3DRenderState RenderStateOverride { get; set; }

        /// <summary>
        /// 使用本材质的每个 mesh group 绘制前的回调
        /// </summary>
        public Model3DDrawCallback PreDrawGroup { get; set; }

        /// <summary>
        /// 使用本材质的每个 mesh group 绘制后的回调
        /// </summary>
        public Model3DDrawCallback PostDrawGroup { get; set; }
    }
}
