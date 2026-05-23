using Microsoft.Xna.Framework;

namespace InnoVault.Models3D.Gltf
{
    /// <summary>
    /// glTF 静态模型导入选项
    /// <br/>只作用于导入阶段，不会在运行时逐帧参与计算
    /// </summary>
    public sealed class GltfImportOptions
    {
        /// <summary>
        /// 默认导入选项
        /// <br/>适配 Terraria 屏幕坐标并使用模型中心作为 pivot
        /// </summary>
        public static GltfImportOptions Default => new GltfImportOptions();

        /// <summary>
        /// 导入缩放
        /// <br/>在节点变换和轴向转换后应用，适合统一调整模型尺寸
        /// </summary>
        public Vector3 ImportScale { get; set; } = Vector3.One;
        /// <summary>
        /// 是否应用节点变换
        /// <br/>关闭后只读取 mesh 本身的顶点数据，通常仅用于排查导出问题
        /// </summary>
        public bool ApplyNodeTransforms { get; set; } = true;
        /// <summary>
        /// 是否生成缺失法线
        /// <br/>模型未提供 NORMAL 时使用面法线补齐，避免开启光照后全黑
        /// </summary>
        public bool GenerateMissingNormals { get; set; } = true;
        /// <summary>
        /// 是否翻转纹理 V 方向
        /// <br/>当前静态 glTF 贴图支持较轻量，只有 UV 原点不匹配时才需要开启
        /// </summary>
        public bool FlipTextureV { get; set; } = false;
        /// <summary>
        /// 是否翻转 Y 轴以适配 Terraria 屏幕坐标
        /// <br/>默认开启，使 glTF 的空间方向进入屏幕 Y 向下的绘制约定
        /// </summary>
        public bool FlipYForTerraria { get; set; } = true;
        /// <summary>
        /// 是否使用包围盒中心作为旋转中心
        /// <br/>默认开启，避免模型顶点远离原点时实例旋转变成绕远处公转
        /// </summary>
        public bool CenterPivot { get; set; } = true;

        /// <summary>
        /// 应用导入缩放
        /// <br/>供导入器集中处理缩放逻辑
        /// </summary>
        /// <param name="value">输入向量</param>
        /// <returns>缩放后的向量</returns>
        public Vector3 ApplyImportScale(Vector3 value) {
            return value * ImportScale;
        }

        /// <summary>
        /// 应用坐标轴转换
        /// <br/>供导入器集中处理位置坐标约定
        /// </summary>
        /// <param name="value">输入位置</param>
        /// <returns>转换后的位置</returns>
        public Vector3 ApplyAxis(Vector3 value) {
            return FlipYForTerraria ? new Vector3(value.X, -value.Y, value.Z) : value;
        }

        /// <summary>
        /// 应用法线坐标轴转换
        /// <br/>供导入器集中处理法线坐标约定
        /// </summary>
        /// <param name="value">输入法线</param>
        /// <returns>转换后的法线</returns>
        public Vector3 ApplyAxisNormal(Vector3 value) {
            return FlipYForTerraria ? new Vector3(value.X, -value.Y, value.Z) : value;
        }
    }
}
