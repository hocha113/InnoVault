using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 格式无关的 3D 材质描述
    /// <br/>保存默认 BasicEffect 参数，也承载材质级 shader 与状态覆盖
    /// </summary>
    public class Model3DMaterial
    {
        /// <summary>
        /// 材质名称
        /// <br/>来自源文件材质名，未命名材质由导入器生成稳定名称
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// 漫反射颜色
        /// <br/>默认 BasicEffect 路径下会与实例 <see cref="Model3DInstance.Tint"/> 相乘
        /// </summary>
        public Color DiffuseColor { get; set; } = Color.White;
        /// <summary>
        /// 材质不透明度
        /// <br/>小于 1 时实例会进入透明绘制桶
        /// </summary>
        public float Opacity { get; set; } = 1f;
        /// <summary>
        /// 漫反射贴图路径
        /// <br/>保留导入解析后的模组内相对路径，未声明贴图时为空
        /// </summary>
        public string DiffuseTexturePath { get; set; }
        /// <summary>
        /// 漫反射贴图
        /// <br/>默认 BasicEffect 路径下作为 0 号纹理使用
        /// </summary>
        public Texture2D DiffuseTexture { get; set; }
        /// <summary>
        /// 是否拥有漫反射贴图
        /// <br/>为 false 时默认路径只使用材质颜色与实例 tint
        /// </summary>
        public bool HasTexture => DiffuseTexture != null;

        /// <summary>
        /// 材质级 Effect 覆盖
        /// <br/>实例未指定 Effect 时生效，参数填写由调用方负责
        /// </summary>
        public Effect Effect { get; set; }
        /// <summary>
        /// 材质级 Effect 提供者
        /// <br/>适合多个模型共享同一套材质 shader 逻辑
        /// </summary>
        public IModel3DEffectProvider EffectProvider { get; set; }
        /// <summary>
        /// 材质级 Effect 参数配置
        /// <br/>在实例级配置之后调用，可覆盖同名 shader 参数
        /// </summary>
        public Model3DConfigureEffect ConfigureEffect { get; set; }
        /// <summary>
        /// 材质级渲染状态覆盖
        /// <br/>只影响使用该材质的分组，仍会被实例级覆盖进一步替换
        /// </summary>
        public Model3DRenderState RenderStateOverride { get; set; }
        /// <summary>
        /// 分组绘制前回调
        /// <br/>仅在使用该材质的分组绘制前触发
        /// </summary>
        public Model3DDrawCallback PreDrawGroup { get; set; }
        /// <summary>
        /// 分组绘制后回调
        /// <br/>仅在使用该材质的分组绘制后触发
        /// </summary>
        public Model3DDrawCallback PostDrawGroup { get; set; }

        /// <summary>
        /// 构造一个材质
        /// <br/>名称会被规范为空字符串而不是 null
        /// </summary>
        /// <param name="name">材质名称</param>
        public Model3DMaterial(string name) {
            Name = name ?? string.Empty;
        }
    }
}
