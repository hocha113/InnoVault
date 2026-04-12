using System;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
#pragma warning disable CS1591
    /// <summary>
    /// [已弃用] 该系统已移除，请移除对此属性的使用
    /// </summary>
    [Obsolete("StaticImmunityAttribute已弃用，该系统存在性能问题已被移除，请移除对此属性的使用")]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class StaticImmunityAttribute(Type sourceNPC = null, int staticImmunityCooldown = 0) : Attribute
    {
        public Type SourceNPC { get; set; } = sourceNPC;
        public int StaticImmunityCooldown { get; set; } = staticImmunityCooldown;
    }

    /// <summary>
    /// [已弃用] 该系统已移除，请移除对此结构的使用
    /// </summary>
    [Obsolete("HitByHitData已弃用，该系统存在性能问题已被移除，请移除对此结构的使用")]
    public struct HitByHitData
    {
        public int npcID;
        public int hitID;
        public byte whoAmI;

        public HitByHitData(NPC npc, Projectile projectile, Player player) {
            npcID = npc.type;
            hitID = projectile.type;
            whoAmI = (byte)player.whoAmI;
        }

        public HitByHitData(NPC npc, Item item, Player player) {
            npcID = npc.type;
            hitID = item.type;
            whoAmI = (byte)player.whoAmI;
        }

        public HitByHitData(int npcID, int hitID, byte whoAmI) {
            this.npcID = npcID;
            this.hitID = hitID;
            this.whoAmI = whoAmI;
        }

        public void Write(BinaryWriter writer) { }
        public static HitByHitData Read(BinaryReader reader) => default;
        public readonly bool Equals(HitByHitData other)
            => npcID == other.npcID && hitID == other.hitID && whoAmI == other.whoAmI;
        public readonly override bool Equals(object obj)
            => obj is HitByHitData other && Equals(other);
        public readonly override int GetHashCode()
            => HashCode.Combine(npcID, hitID, whoAmI);
        public static bool operator ==(HitByHitData left, HitByHitData right)
            => left.Equals(right);
        public static bool operator !=(HitByHitData left, HitByHitData right)
            => !(left == right);
    }
#pragma warning restore CS1591

    internal sealed class StaticImmunitySystem : ModSystem
    {
        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) { }
    }
}
