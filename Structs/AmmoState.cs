using Terraria;

/// <summary>
/// 表示弹药的状态，包括相关物品、投射物以及数量信息
/// </summary>
public struct AmmoState
{
    /// <summary>
    /// 当前使用中的物品列表
    /// </summary>
    public Item[] CurrentItems;
    /// <summary>
    /// 用于表示最小数量的物品实例
    /// </summary>
    public Item MinAmountItem;
    /// <summary>
    /// 用于表示最大数量的物品实例
    /// </summary>
    public Item MaxAmountItem;
    /// <summary>
    /// 允许作为弹药的物品 ID 列表
    /// </summary>
    public int[] ValidItemIDs;
    /// <summary>
    /// 允许作为弹药的投射物 ID 列表
    /// </summary>
    public int[] ValidProjectileIDs;
    /// <summary>
    /// 当前弹药数量
    /// </summary>
    public int CurrentAmount;
}