using Microsoft.Xna.Framework;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 一盏定向光源的描述，与 <see cref="Microsoft.Xna.Framework.Graphics.DirectionalLight"/> 字段一一对应
    /// <br/><see cref="Direction"/> 表示光的传播方向（从光源射向被照面），调用方需保证其归一化
    /// </summary>
    public struct Model3DDirectionalLight
    {
        /// <summary>
        /// 是否启用这盏灯。关闭时下游 <c>ApplyLighting</c> 只会写 <see cref="Microsoft.Xna.Framework.Graphics.DirectionalLight.Enabled"/> = false
        /// </summary>
        public bool Enabled;
        /// <summary>
        /// 光的传播方向，需为归一化向量
        /// <br/>例如 <c>new Vector3(0, 1, 0)</c> 表示光由上往下，<c>new Vector3(0, 0, -1)</c> 表示从相机方向射入场景
        /// </summary>
        public Vector3 Direction;
        /// <summary>
        /// 漫反射颜色（0..1 线性空间），等价 <see cref="Color.ToVector3"/>
        /// </summary>
        public Vector3 DiffuseColor;
        /// <summary>
        /// 高光颜色（0..1 线性空间），通常与漫反射同向；若希望禁用高光可设为 <see cref="Vector3.Zero"/>
        /// </summary>
        public Vector3 SpecularColor;
    }

    /// <summary>
    /// Models3D 的光照配置，是 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect"/> 现有能力的简洁封装
    /// <br/>包含 3 盏定向光 + ambient + emissive + specular power
    /// <br/>注意：当前实现仅消费 BasicEffect 支持的字段，未来可能扩充点光数组、阴影贴图引用等
    /// <br/>使用模式：
    /// <list type="bullet">
    ///     <item>不设置任何东西：使用 <see cref="Model3DRenderer.GlobalLighting"/> 的默认值，效果与 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect.EnableDefaultLighting"/> 一致</item>
    ///     <item>实例覆盖：设置 <see cref="Model3DInstance.LightingOverride"/> 即可独立配光</item>
    ///     <item>动态/批量规则：订阅 <see cref="Model3DRenderer.ResolveLighting"/> 在每实例绘制前修改配置</item>
    /// </list>
    /// </summary>
    public sealed class Model3DLightingConfig
    {
        /// <summary>
        /// 环境光颜色（0..1）
        /// </summary>
        public Vector3 AmbientColor;
        /// <summary>
        /// 主光（Key Light）槽位，对应 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect.DirectionalLight0"/>
        /// </summary>
        public Model3DDirectionalLight Light0;
        /// <summary>
        /// 补光（Fill Light）槽位，对应 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect.DirectionalLight1"/>
        /// </summary>
        public Model3DDirectionalLight Light1;
        /// <summary>
        /// 背光（Back / Rim Light）槽位，对应 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect.DirectionalLight2"/>
        /// </summary>
        public Model3DDirectionalLight Light2;
        /// <summary>
        /// 自发光颜色（0..1），与材质本身的颜色叠加，不受灯光影响
        /// </summary>
        public Vector3 EmissiveColor;
        /// <summary>
        /// 高光指数，越大高光越锐利，BasicEffect 默认 16
        /// </summary>
        public float SpecularPower;

        /// <summary>
        /// 构造一个全零的空配置，所有灯都未启用
        /// </summary>
        public Model3DLightingConfig() {
            AmbientColor = Vector3.Zero;
            Light0 = default;
            Light1 = default;
            Light2 = default;
            EmissiveColor = Vector3.Zero;
            SpecularPower = 16f;
        }

        /// <summary>
        /// 创建一份与 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect.EnableDefaultLighting"/> 完全一致的预设
        /// <br/>方便开发者在已有"经典三点光"基础上做局部覆盖
        /// </summary>
        public static Model3DLightingConfig CreateDefault() {
            //数值取自 XNA/MonoGame BasicEffect.EnableDefaultLighting() 的实现
            return new Model3DLightingConfig {
                AmbientColor = new Vector3(0.05333332f, 0.09882354f, 0.1819608f),
                Light0 = new Model3DDirectionalLight {
                    Enabled = true,
                    Direction = new Vector3(-0.5265408f, -0.5735765f, -0.6275069f),
                    DiffuseColor = new Vector3(1f, 0.9607844f, 0.8078432f),
                    SpecularColor = new Vector3(1f, 0.9607844f, 0.8078432f),
                },
                Light1 = new Model3DDirectionalLight {
                    Enabled = true,
                    Direction = new Vector3(0.7198464f, 0.3420201f, 0.6040227f),
                    DiffuseColor = new Vector3(0.9647059f, 0.7607844f, 0.4078432f),
                    SpecularColor = Vector3.Zero,
                },
                Light2 = new Model3DDirectionalLight {
                    Enabled = true,
                    Direction = new Vector3(0.4545195f, -0.7660444f, 0.4545195f),
                    DiffuseColor = new Vector3(0.3231373f, 0.3607844f, 0.3937255f),
                    SpecularColor = new Vector3(0.3231373f, 0.3607844f, 0.3937255f),
                },
                EmissiveColor = Vector3.Zero,
                SpecularPower = 16f,
            };
        }

        /// <summary>
        /// 把当前配置浅拷贝到 <paramref name="dst"/>，主要用于把 <see cref="Model3DRenderer.GlobalLighting"/>
        /// 或实例 Override 拷到渲染器内部的 scratch，再让订阅者安全 mutate
        /// </summary>
        /// <param name="dst">目标配置，不能为空</param>
        public void CopyTo(Model3DLightingConfig dst) {
            if (dst == null) {
                return;
            }
            dst.AmbientColor = AmbientColor;
            dst.Light0 = Light0;
            dst.Light1 = Light1;
            dst.Light2 = Light2;
            dst.EmissiveColor = EmissiveColor;
            dst.SpecularPower = SpecularPower;
        }
    }

    /// <summary>
    /// 每实例光照解析回调，订阅 <see cref="Model3DRenderer.ResolveLighting"/> 时使用
    /// <br/>传入的 <paramref name="config"/> 已包含 <see cref="Model3DInstance.LightingOverride"/> 或
    /// <see cref="Model3DRenderer.GlobalLighting"/> 的拷贝，按需 mutate 即可生效
    /// <br/>该回调在每帧的每个可见实例绘制前都会被调用，请保持轻量
    /// </summary>
    /// <param name="instance">即将绘制的 3D 实例</param>
    /// <param name="config">渲染器内部的 scratch 配置，可安全修改</param>
    public delegate void Model3DLightingResolver(Model3DInstance instance, Model3DLightingConfig config);
}
