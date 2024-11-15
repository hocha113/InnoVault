using Terraria.DataStructures;

/// <summary>
/// 表示射击行为的状态，包括弹药类型、武器属性和使用条件等信息
/// </summary>
public struct ShootState
{
    /// <summary>
    /// 弹药类型标识，指示此次射击所使用的弹药种类
    /// </summary>
    public int AmmoTypes;
    /// <summary>
    /// 射弹速度
    /// </summary>
    public float ShootSpeed;
    /// <summary>
    /// 武器造成的伤害值，决定每次射击时目标所受到的伤害
    /// </summary>
    public int WeaponDamage;
    /// <summary>
    /// 武器的击退力，表示射击后目标受到的击退效果强度
    /// </summary>
    public float WeaponKnockback;
    /// <summary>
    /// 用作弹药的物品类型，标识该武器所需的弹药物品类型
    /// </summary>
    public int UseAmmoItemType;
    /// <summary>
    /// 是否有足够的弹药来执行射击，若为 `false` 则表示弹药不足，无法进行射击
    /// </summary>
    public bool HasAmmo;
    /// <summary>
    /// 描述物品使用事件的来源，包含了弹药使用的详细信息
    /// </summary>
    public EntitySource_ItemUse_WithAmmo Source;
}
