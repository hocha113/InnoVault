/// <summary>
/// 决定粒子在那里绘制
/// </summary>
public enum PRTLayersModeEnum
{
    /// <summary>
    /// 不参与自动更新，仅仅是装载进实例列表，这个模式很少使用，一般用于特殊的UI更新位置，以供开发者手动调用
    /// </summary>
    None,
    /// <summary>
    /// 在世界中进行绘制操作和逻辑更新，也是一个默认选项模式
    /// </summary>
    InWorld,
    /// <summary>
    /// 正常进行逻辑更新，但不进行绘制，供开发者在需要的地方手动调用绘制逻辑
    /// </summary>
    NoDraw,
}