using Microsoft.Xna.Framework;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示 OBJ 顶点位置在导入时的轴重映射方式
    /// <br/>OBJ 常见的导出习惯是 Y 朝上，而 Terraria 屏幕坐标 Y 朝下
    /// </summary>
    public enum ObjAxisConvention
    {
        /// <summary>
        /// 不做任何坐标转换，OBJ 顶点保持原始数据
        /// </summary>
        Raw,
        /// <summary>
        /// 假设 OBJ 是 Y 朝上的右手坐标系，转换为 Terraria 屏幕坐标（Y 朝下）
        /// <br/>等价于将原始 (x, y, z) 写入为 (x, -y, z)
        /// </summary>
        YUpToYDown,
    }

    /// <summary>
    /// OBJ 模型在导入阶段可配置的解析与转换选项
    /// </summary>
    public sealed class ObjImportOptions
    {
        /// <summary>
        /// 默认导入设置：执行 Y 轴翻转，统一缩放为 1，不重算法线
        /// </summary>
        public static ObjImportOptions Default => new ObjImportOptions();

        /// <summary>
        /// 顶点轴向转换约定，默认 <see cref="ObjAxisConvention.YUpToYDown"/>
        /// </summary>
        public ObjAxisConvention AxisConvention { get; set; } = ObjAxisConvention.YUpToYDown;

        /// <summary>
        /// 导入时统一应用的缩放系数，默认 1
        /// </summary>
        public Vector3 ImportScale { get; set; } = Vector3.One;

        /// <summary>
        /// 是否在 OBJ 没有提供 <c>vn</c> 法线数据时使用面法线作为顶点法线
        /// </summary>
        public bool GenerateMissingNormals { get; set; } = true;

        /// <summary>
        /// 是否将 V 方向的纹理坐标翻转，OBJ 与许多 DCC 工具的 UV 原点位于左下，
        /// 默认 <see langword="true"/>，使其与 XNA/MonoGame 的左上原点对齐
        /// </summary>
        public bool FlipTextureV { get; set; } = true;

        /// <summary>
        /// 当 OBJ 中出现多于 4 边的多边形时，是否使用扇形三角化
        /// <br/>关闭时直接跳过该面并记录警告
        /// </summary>
        public bool TriangulateNGons { get; set; } = true;

        /// <summary>
        /// 应用轴向转换到一个原始 OBJ 位置向量
        /// </summary>
        /// <param name="raw">从 OBJ 读取的原始 <c>v</c> 向量</param>
        /// <returns>转换为渲染坐标系后的向量</returns>
        public Vector3 ApplyAxis(Vector3 raw) {
            Vector3 result = raw;
            if (AxisConvention == ObjAxisConvention.YUpToYDown) {
                result = new Vector3(raw.X, -raw.Y, raw.Z);
            }
            return result * ImportScale;
        }

        /// <summary>
        /// 应用轴向转换到一个原始 OBJ 法线向量（不应用缩放）
        /// </summary>
        /// <param name="raw">从 OBJ 读取的原始 <c>vn</c> 向量</param>
        /// <returns>转换为渲染坐标系后的法线</returns>
        public Vector3 ApplyAxisNormal(Vector3 raw) {
            return AxisConvention switch {
                ObjAxisConvention.YUpToYDown => new Vector3(raw.X, -raw.Y, raw.Z),
                _ => raw,
            };
        }
    }
}
