/// <summary>
/// PRTDrawModeEnum 表示不同的渲染混合模式，每种模式决定像素与背景如何混合
/// 该枚举支持三种混合模式：透明混合（AlphaBlend）、加法混合（AdditiveBlend）和非预乘混合（NonPremultiplied）
/// </summary>
public enum PRTDrawModeEnum
{
    /// <summary>
    /// 透明混合模式，基于alpha值（透明度）将前景和背景进行混合
    /// 常用于渲染半透明的物体
    /// </summary>
    AlphaBlend,
    /// <summary>
    /// 非预乘混合模式，使用独立的alpha值和颜色值进行混合，适用于没有预乘alpha的纹理或图像
    /// </summary>
    NonPremultiplied,
    /// <summary>
    /// 加法混合模式，将前景和背景的颜色值相加，通常用于发光或光效
    /// 产生明亮、强烈的视觉效果
    /// </summary>
    AdditiveBlend,
}
