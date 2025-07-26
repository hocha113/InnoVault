using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using static InnoVault.VaultNetWork;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 为 NPC 类型声明其静态免疫行为的属性标签
    /// 如果让sourceNPC保持默认值，则会再加载时自动匹配为当前 NPC 类型，即源 NPC，只有源 NPC 所设置的 staticImmunityCooldown 才会被载入
    /// </summary>
    /// <param name="sourceNPC">
    /// 该 NPC 的免疫逻辑来源 NPC 类型
    /// 用于将当前 NPC 的静态免疫逻辑映射到另一个 NPC，共享无敌帧
    /// 若为 <see langword="null"/>，则默认为自身（即使用当前类本身作为来源）
    /// </param>
    /// <param name="staticImmunityCooldown">
    /// 静态免疫冷却时间，单位为帧（ticks）
    /// 冷却期间该 NPC 将不会再次受到来自同一玩家的伤害
    /// 默认值为 0
    /// </param>
    /// <remarks>
    /// 应用于<see cref="ModNPC"/>派生类，通常用于定义 boss 多阶段或蠕虫体节之间共享的免疫状态
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class StaticImmunityAttribute(Type sourceNPC = null, int staticImmunityCooldown = 0) : Attribute
    {
        /// <summary>
        /// 该 NPC 的免疫逻辑来源 NPC 类型
        /// 用于将当前 NPC 的静态免疫逻辑映射到另一个 NPC，共享无敌帧
        /// 若为 <see langword="null"/>，则默认为自身（即使用当前类本身作为来源）
        /// </summary>
        public Type SourceNPC { get; set; } = sourceNPC;

        /// <summary>
        /// 静态免疫冷却时间，单位为帧（ticks）
        /// 冷却期间该 NPC 将不会再次受到来自同一玩家的伤害
        /// 默认值为 0
        /// </summary>
        public int StaticImmunityCooldown { get; set; } = staticImmunityCooldown;
    }

    internal class StaticImmunitySystem : ModSystem
    {
        internal static readonly Dictionary<int, int> NPCID_To_SourceID = [];
        internal static readonly Dictionary<int, int> NPCID_To_StaticImmunityCooldown = [];
        internal static readonly Dictionary<int, int[]> NPCSourceID_To_PlayerCooldowns = [];

        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            if (type == MessageType.AddStaticImmunity) {
                int npcID = reader.ReadInt32();
                int playerWhoAmI = reader.ReadInt32();
                VaultUtils.AddStaticImmunity(npcID, playerWhoAmI, false);

                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.AddStaticImmunity);
                    modPacket.Write(npcID);
                    modPacket.Write(playerWhoAmI);//TODO:有必要再考量一下，这里的玩家索引是否真的有必要发送
                    modPacket.Send(-1, whoAmI);
                }
            }
            else if (type == MessageType.SetStaticImmunity) {
                int npcID = reader.ReadInt32();
                int playerWhoAmI = reader.ReadInt32();
                int immunity = reader.ReadInt32();
                VaultUtils.SetStaticImmunity(npcID, whoAmI, immunity, false);

                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.SetStaticImmunity);
                    modPacket.Write(npcID);
                    modPacket.Write(playerWhoAmI);
                    modPacket.Write(immunity);
                    modPacket.Send(-1, whoAmI);
                }
            }
        }

        public override void Unload() {
            NPCID_To_SourceID?.Clear();
            NPCID_To_StaticImmunityCooldown?.Clear();
            NPCSourceID_To_PlayerCooldowns?.Clear();
        }

        public override void PostSetupContent() {
            try {
                LoadImmunityData();
            } catch {
                VaultMod.Instance.Logger.Error("[LoadImmunityData] an error has occurred");
            }
        }

        private static void LoadImmunityData() {
            for (int i = 0; i < NPCLoader.NPCCount; i++) {
                NPCID_To_SourceID.TryAdd(i, -1);//确保不会覆盖前面已经设置好的项
            }

            Type[] types = VaultUtils.GetAnyModCodeType();
            MethodInfo npcTypeMethod = typeof(ModContent).GetMethod(nameof(ModContent.NPCType));

            foreach (var type in types) {
                StaticImmunityAttribute attribute;

                try {
                    attribute = type.GetCustomAttribute<StaticImmunityAttribute>();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Skipped {type.ToString().ToLower()} " +
                        $"{type.Name} in class {type.FullName} due to attribute load error: {ex.Message}");
                    return;
                }

                if (attribute == null) {
                    continue;
                }

                if (attribute.SourceNPC == null) {
                    attribute.SourceNPC = type;
                }

                if (!type.IsSubclassOf(typeof(ModNPC))) {
                    VaultMod.Instance.Logger.Warn($"Skipped {type.FullName}: Not a ModNPC type.");
                    continue;
                }

                try {
                    int npcID = (int)npcTypeMethod.MakeGenericMethod(type).Invoke(null, null);
                    int npcSourceID = (int)npcTypeMethod.MakeGenericMethod(attribute.SourceNPC).Invoke(null, null);
                    VaultUtils.LoadenNPCStaticImmunityData(npcID, npcSourceID, attribute.StaticImmunityCooldown);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Failed to load immunity data for {type.FullName}: {ex.Message}");
                }
            }

            VaultUtils.NormalizeStaticImmunityCooldowns();
        }

        public override void PostUpdateNPCs() {
            foreach (var key in NPCSourceID_To_PlayerCooldowns.Keys) {
                for (int whoAmI = 0; whoAmI < Main.maxPlayers; whoAmI++) {
                    if (NPCSourceID_To_PlayerCooldowns[key][whoAmI] <= 0) {
                        continue;
                    }

                    NPCSourceID_To_PlayerCooldowns[key][whoAmI]--;
                }
            }
        }
    }

    internal class StaticImmunityGlobalNPC : GlobalNPC
    {
        public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone) 
            => npc.AddStaticImmunity(player.whoAmI);

        public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone) 
            => npc.AddStaticImmunity(projectile.owner);

        public override bool? CanBeHitByItem(NPC npc, Player player, Item item) 
            => npc.HasStaticImmunity(player.whoAmI) ? false : null;

        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) 
            => npc.HasStaticImmunity(projectile.owner) ? false : null;
    }
}
