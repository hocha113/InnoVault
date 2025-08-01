using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using static InnoVault.VaultNetWork;
using static InnoVault.GameSystem.StaticImmunitySystem;
using Terraria.ID;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 为 NPC 类型声明其静态免疫行为的属性标签<br/>
    /// 如果让sourceNPC保持默认值，则会再加载时自动匹配为当前 NPC 类型，即源 NPC，<br/>
    /// 只有源 NPC 所设置的 staticImmunityCooldown 才会被载入
    /// </summary>
    /// <param name="sourceNPC">
    /// 该 NPC 的免疫逻辑来源 NPC 类型<br/>
    /// 用于将当前 NPC 的静态免疫逻辑映射到另一个 NPC，共享无敌帧<br/>
    /// 若为 <see langword="null"/>，则默认为自身（即使用当前类本身作为来源）
    /// </param>
    /// <param name="staticImmunityCooldown">
    /// 静态免疫冷却时间，单位为帧（ticks）<br/>
    /// 冷却期间该 NPC 将不会再次受到来自同一玩家的伤害<br/>
    /// 默认值为 0
    /// </param>
    /// <remarks>
    /// 应用于<see cref="ModNPC"/>派生类，通常用于定义 boss 多阶段或蠕虫体节之间共享的免疫状态
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class StaticImmunityAttribute(Type sourceNPC = null, int staticImmunityCooldown = 0) : Attribute
    {
        /// <summary>
        /// 该类 NPC 的免疫逻辑来源 NPC 类型<br/>
        /// 用于将当前 NPC 的静态免疫逻辑映射到另一个 NPC，共享无敌帧<br/>
        /// 若为 <see langword="null"/>，则默认为自身（即使用当前类本身作为来源）
        /// </summary>
        public Type SourceNPC { get; set; } = sourceNPC;

        /// <summary>
        /// 静态免疫冷却时间，单位为帧（ticks）<br/>
        /// 冷却期间该类 NPC 将不会再次受到来自同一玩家的伤害
        /// 默认值为 0
        /// </summary>
        public int StaticImmunityCooldown { get; set; } = staticImmunityCooldown;
    }

    internal sealed class StaticImmunitySystem : ModSystem
    {
        internal enum HitType : byte
        {
            Player,
            Item,
            Projectile,
        }
        //这里的数据不应该对外暴露，而是使用封装好的接口进行访问操纵，直接修改这里的字典可能造成系统不稳定，写在这里提醒自己
        internal static readonly Dictionary<int, bool> NPCID_To_UseStaticImmunity = [];
        internal static readonly Dictionary<int, int> NPCID_To_SourceID = [];
        internal static readonly Dictionary<int, HitType> NPCID_To_HitType = [];
        internal static readonly Dictionary<int, int> NPCID_To_StaticImmunityCooldown = [];
        internal static readonly Dictionary<int, int> NPCID_To_StaticImmunityCooldown_ByPlayer = [];
        internal static readonly Dictionary<int, int[]> NPCSourceID_To_PlayerCooldowns = [];

        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            if (type == MessageType.AddStaticImmunity) {
                int npcID = reader.ReadInt32();
                int playerWhoAmI = reader.ReadInt32();
                short localNPCHitCooldown = reader.ReadInt16();
                VaultUtils.AddStaticImmunity(npcID, playerWhoAmI, localNPCHitCooldown, false);

                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.AddStaticImmunity);
                    modPacket.Write(npcID);
                    modPacket.Write(playerWhoAmI);
                    modPacket.Write(localNPCHitCooldown);
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
            else if (type == MessageType.UseStaticImmunity) {
                int npcID = reader.ReadInt32();
                bool enabled = reader.ReadBoolean();
                VaultUtils.ConfigureStaticImmunityUsage(npcID, enabled, false);

                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.UseStaticImmunity);
                    modPacket.Write(npcID);
                    modPacket.Write(enabled);
                    modPacket.Send(-1, whoAmI);
                }
            }
        }

        public override void Unload() {
            NPCID_To_UseStaticImmunity?.Clear();
            NPCID_To_SourceID?.Clear();
            NPCID_To_HitType?.Clear();
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
                NPCID_To_UseStaticImmunity.TryAdd(i, true);
                NPCID_To_SourceID.TryAdd(i, -1);//确保不会覆盖前面已经设置好的项
                NPCID_To_HitType.TryAdd(i, HitType.Player);
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
                    VaultUtils.LoadenNPCStaticImmunityData(npcSourceID, npcID, attribute.StaticImmunityCooldown);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Failed to load immunity data for {type.FullName}: {ex.Message}");
                }
            }

            VaultUtils.NormalizeStaticImmunityCooldowns();
        }

        public override void PostUpdateEverything() {
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

    internal sealed class StaticImmunityPlayer : PlayerOverride
    {
        public override bool? On_CanHitNPC(NPC target) {
            if (target.HasStaticImmunity(Player.whoAmI)) {
                return false;
            }
            return null;
        }

        public override bool On_OnHitNPC(NPC npc, in NPC.HitInfo hit, int damageDone) {
            int sourceID = NPCID_To_SourceID[npc.type];
            if (sourceID == -1 || !NPCID_To_UseStaticImmunity[npc.type]) {
                return true;
            }

            if (NPCID_To_HitType[npc.type] != HitType.Player) {
                NPCID_To_HitType[npc.type] = HitType.Player;
                return true;
            }

            if (npc.AddStaticImmunity(Player.whoAmI)) {
                npc.immune[Player.whoAmI] = 0;
            }
            return true;
        }
    }

    internal sealed class StaticImmunityGlobalNPC : GlobalNPC
    {
        public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone) {
            if (npc.AddStaticImmunity(player.whoAmI)) {
                npc.immune[player.whoAmI] = 0;
                NPCID_To_HitType[npc.type] = HitType.Item;
            }
        }

        public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone) {
            short localNPCHitCooldown = -2;

            if (projectile.usesLocalNPCImmunity) {
                localNPCHitCooldown = (short)Math.Clamp(projectile.localNPCHitCooldown, short.MinValue, short.MaxValue);
            }
            else if (projectile.usesIDStaticNPCImmunity) {
                localNPCHitCooldown = (short)Math.Clamp(projectile.idStaticNPCHitCooldown, short.MinValue, short.MaxValue);
            }

            if (npc.AddStaticImmunity(projectile.owner, localNPCHitCooldown)) {
                int whoAmI = projectile.owner;
                if (whoAmI == -1 || whoAmI >= Main.maxPlayers) {
                    whoAmI = 0;
                }
                npc.immune[whoAmI] = 0;
                NPCID_To_HitType[npc.type] = HitType.Projectile;
            }
        }

        public override bool? CanBeHitByItem(NPC npc, Player player, Item item)
            => npc.HasStaticImmunity(player.whoAmI) ? false : null;

        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile)
            => npc.HasStaticImmunity(projectile.owner) ? false : null;
    }
}
