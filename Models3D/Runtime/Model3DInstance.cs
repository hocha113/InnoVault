using InnoVault.Models3D.Wavefront;
using Microsoft.Xna.Framework;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 一个用于绘制的 3D 模型实例
    /// <br/>仅描述"这一帧/这个对象怎么画"，不包含游戏逻辑
    /// </summary>
    /// <remarks>
    /// 使用方式：
    /// <list type="bullet">
    /// <item>临时绘制：每帧构造好实例后调用 <see cref="Model3DRenderer.Submit"/> 提交一次</item>
    /// <item>持久绘制：构造好实例后调用 <see cref="Model3DRenderer.RegisterPersistent"/>，不再使用时调用 <see cref="Model3DRenderer.UnregisterPersistent"/></item>
    /// </list>
    /// </remarks>
    public sealed class Model3DInstance
    {
        /// <summary>
        /// 关联的模型资源
        /// </summary>
        public VaultObjModel Model { get; set; }

        /// <summary>
        /// 世界空间位置（按 Terraria 屏幕坐标系，单位为像素）
        /// <br/>对应屏幕渲染时会自动减去 <see cref="Terraria.Main.screenPosition"/>
        /// </summary>
        public Vector2 Position { get; set; }

        /// <summary>
        /// 沿 Z 轴的附加深度偏移（屏幕坐标系，向"屏幕外"为负）
        /// <br/>用于让模型相对于地面/玩家平面错位
        /// </summary>
        public float Depth { get; set; }

        /// <summary>
        /// 三轴欧拉旋转（弧度），按 X、Y、Z 顺序应用
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// 三轴缩放
        /// </summary>
        public Vector3 Scale { get; set; } = Vector3.One;

        /// <summary>
        /// 整体颜色 tint，会与材质的漫反射颜色相乘
        /// </summary>
        public Color Tint { get; set; } = Color.White;

        /// <summary>
        /// 整体不透明度，会与 <see cref="Tint"/> 的 alpha 相乘
        /// </summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>
        /// 绘制层级，决定模型与 Terraria 各阶段的遮挡关系
        /// </summary>
        public Model3DLayer Layer { get; set; } = Model3DLayer.AfterTiles;

        /// <summary>
        /// 是否启用 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect"/> 的内置定向光照
        /// <br/>关闭时使用纯色/贴图渲染
        /// </summary>
        public bool LightingEnabled { get; set; } = false;

        /// <summary>
        /// 实例级光照覆盖配置；<see langword="null"/> 时使用 <see cref="Model3DRenderer.GlobalLighting"/>
        /// <br/>仅在 <see cref="LightingEnabled"/> 为 <see langword="true"/> 时生效
        /// <br/>注意：传入的对象会被渲染器拷贝到内部 scratch 后再交给 <see cref="Model3DRenderer.ResolveLighting"/>，
        /// 因此订阅者的修改不会污染该对象，可放心在多实例间共享同一份配置
        /// </summary>
        public Model3DLightingConfig LightingOverride { get; set; }

        /// <summary>
        /// 是否在未来的阴影系统中作为投影者参与
        /// <br/><b>当前渲染管线尚未实现阴影，故此字段不被消费</b>，仅作为前向兼容预留
        /// </summary>
        public bool CastShadow { get; set; } = false;

        /// <summary>
        /// 是否在未来的阴影系统中接收阴影
        /// <br/><b>当前渲染管线尚未实现阴影，故此字段不被消费</b>，仅作为前向兼容预留
        /// </summary>
        public bool ReceiveShadow { get; set; } = true;

        /// <summary>
        /// 是否启用深度测试（多个模型相互重叠时可能需要打开）
        /// </summary>
        public bool DepthEnabled { get; set; } = true;

        /// <summary>
        /// 是否进行背面剔除，OBJ 来源不一致时建议保持 <see langword="false"/>
        /// </summary>
        public bool CullBackface { get; set; } = false;

        /// <summary>
        /// 是否强制将该实例视为半透明，即使材质 / <see cref="Tint"/> / <see cref="Opacity"/> 都满 1
        /// <br/>开启后该实例会进入透明桶：使用 alpha blend、只读深度、按距离 back-to-front 排序
        /// <br/>适合发光罩、玻璃外壳等需要从外往里看的"装饰外层"场景
        /// </summary>
        public bool ForceTransparent { get; set; } = false;

        /// <summary>
        /// 自定义排序权重（越大越后绘制），同 <see cref="Layer"/> 的实例间排序使用
        /// </summary>
        public float SortKey { get; set; }

        /// <summary>
        /// 是否参与绘制
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// 构造一个未绑定模型的空实例
        /// </summary>
        public Model3DInstance() { }

        /// <summary>
        /// 用指定模型构造实例
        /// </summary>
        public Model3DInstance(VaultObjModel model) {
            Model = model;
        }
    }
}
