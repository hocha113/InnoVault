using Terraria.DataStructures;
using static Terraria.Player;

namespace InnoVault.StateStruct
{
    /// <summary>
    /// 封装玩家受击处理所需的所有参数
    /// </summary>
    public struct HurtState
    {
        /// <summary>造成伤害的来源信息</summary>
        public PlayerDeathReason DamageSource;
        /// <summary>伤害值</summary>
        public int Damage;
        /// <summary>击退方向，通常为 ±1</summary>
        public int HitDirection;
        /// <summary>
        /// 输出的 <see cref="HurtInfo"/> 信息
        /// </summary>
        public HurtInfo Info;
        /// <summary>是否为 PvP 攻击</summary>
        public bool PvP;
        /// <summary>是否静默受击（不触发音效/特效）</summary>
        public bool Quiet;
        /// <summary>冷却计数器</summary>
        public int CooldownCounter;
        /// <summary>是否可以被闪避</summary>
        public bool Dodgeable;
        /// <summary>护甲穿透值</summary>
        public float ArmorPenetration;
        /// <summary>护甲穿透缩放值</summary>
        public float ScalingArmorPenetration;
        /// <summary>击退力度</summary>
        public float Knockback;
    }
}
