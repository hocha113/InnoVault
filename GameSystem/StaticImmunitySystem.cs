using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using static InnoVault.GameSystem.StaticImmunitySystem;
using static InnoVault.VaultNetWork;

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

    /// <summary>
    /// 用于唯一标识一次击中事件的结构体<br/>
    /// 主要应用于静态伤害免疫机制中的击中冷却追踪<br/>
    /// 可用作字典键值以支持查找和更新击中状态
    /// </summary>
    public struct HitByHitData
    {
        /// <summary>
        /// 被击中的 NPC 类型 ID
        /// 用于识别具体的 NPC 类别
        /// </summary>
        public int npcID;

        /// <summary>
        /// 造成伤害的道具或弹幕的类型 ID
        /// 表示击中来源
        /// </summary>
        public int hitID;

        /// <summary>
        /// 造成击中的玩家编号
        /// 范围为 0 到 Main.maxPlayers - 1
        /// </summary>
        public byte whoAmI;

        /// <summary>
        /// 用于弹幕击中构造唯一击中标识
        /// </summary>
        public HitByHitData(NPC npc, Projectile projectile, Player player) {
            npcID = npc.type;
            hitID = projectile.type;
            whoAmI = (byte)player.whoAmI;
        }

        /// <summary>
        /// 用于物品击中构造唯一击中标识
        /// </summary>
        public HitByHitData(NPC npc, Item item, Player player) {
            npcID = npc.type;
            hitID = item.type;
            whoAmI = (byte)player.whoAmI;
        }

        /// <summary>
        /// 通用构造函数 传入三个基础字段
        /// 一般用于网络同步或手动构建结构
        /// </summary>
        public HitByHitData(int npcID, int hitID, byte whoAmI) {
            this.npcID = npcID;
            this.hitID = hitID;
            this.whoAmI = whoAmI;
        }

        /// <summary>
        /// 将结构体写入二进制流 用于网络传输
        /// 顺序为 npcID → hitID → whoAmI
        /// </summary>
        public void Write(BinaryWriter writer) {
            writer.Write(npcID);
            writer.Write(hitID);
            writer.Write(whoAmI);
        }

        /// <summary>
        /// 从二进制流读取数据并构造结构体
        /// 需保证读取顺序与写入一致
        /// </summary>
        public static HitByHitData Read(BinaryReader reader)
            => new HitByHitData(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());

        /// <summary>
        /// 判断两个 HitByHitData 是否完全相等
        /// 逐字段比较
        /// </summary>
        public readonly bool Equals(HitByHitData other)
            => npcID == other.npcID && hitID == other.hitID && whoAmI == other.whoAmI;

        /// <summary>
        /// 重写 object.Equals 支持字典键比较
        /// </summary>
        public readonly override bool Equals(object obj)
            => obj is HitByHitData other && Equals(other);

        /// <summary>
        /// 生成哈希值用于哈希容器支持
        /// 基于三个字段组合计算
        /// </summary>
        public readonly override int GetHashCode()
            => HashCode.Combine(npcID, hitID, whoAmI);

        /// <summary>
        /// 判断是否相等，比较<see cref="npcID"/>与<see cref="hitID"/>与<see cref="whoAmI"/>
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator == (HitByHitData left, HitByHitData right)
            => left.Equals(right);

        /// <summary>
        /// 判断是否不相等，比较<see cref="npcID"/>与<see cref="hitID"/>与<see cref="whoAmI"/>
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator != (HitByHitData left, HitByHitData right)
            => !(left == right);
    }

    internal sealed class StaticImmunitySystem : ModSystem
    {
        internal enum HitType : byte
        {
            Player,
            Item,
            Projectile,
        }

        internal static readonly int[] normalWhoAmIs = [.. Enumerable.Range(0, Main.maxPlayers)];
        //这里的数据不应该对外暴露，而是使用封装好的接口进行访问操纵，直接修改这里的字典可能造成系统不稳定，写在这里提醒自己
        internal static readonly Dictionary<int, bool> NPCID_To_UseStaticImmunity = [];
        internal static readonly Dictionary<int, int> NPCID_To_SourceID = [];
        internal static readonly Dictionary<int, HitType> NPCID_To_HitType = [];
        internal static readonly Dictionary<int, int> NPCID_To_StaticImmunityCooldown = [];
        internal static readonly Dictionary<int, int[]> NPCSourceID_To_PlayerCooldowns = [];
        //(NPC.type, Item.type, Player.whoAmI)
        internal static readonly Dictionary<HitByHitData, int[]> NPCSourceID_To_ByItemCooldowns = [];
        //(NPC.type, Projectile.type, Player.whoAmI)
        internal static readonly Dictionary<HitByHitData, int[]> NPCSourceID_To_ByProjectileCooldowns = [];

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
            if (type == MessageType.AddStaticImmunityByProj) {
                HitByHitData hitByHitData = HitByHitData.Read(reader);
                int customCooldown = reader.ReadInt32();
                VaultUtils.AddStaticImmunity(NPCSourceID_To_ByProjectileCooldowns, hitByHitData.npcID, hitByHitData.whoAmI, hitByHitData.hitID, customCooldown, false);
                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.AddStaticImmunityByProj);
                    hitByHitData.Write(modPacket);
                    modPacket.Send(-1, whoAmI);
                }
            }
            if (type == MessageType.AddStaticImmunityByItem) {
                HitByHitData hitByHitData = HitByHitData.Read(reader);
                int customCooldown = reader.ReadInt32();
                VaultUtils.AddStaticImmunity(NPCSourceID_To_ByItemCooldowns, hitByHitData.npcID, hitByHitData.whoAmI, hitByHitData.hitID, customCooldown, false);
                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.AddStaticImmunityByItem);
                    hitByHitData.Write(modPacket);
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
            NPCSourceID_To_ByItemCooldowns?.Clear();
            NPCSourceID_To_ByProjectileCooldowns?.Clear();
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

            foreach (var key in NPCSourceID_To_ByItemCooldowns.Keys) {
                int has = 0;
                for (int whoAmI = 0; whoAmI < Main.maxPlayers; whoAmI++) {
                    if (NPCSourceID_To_ByItemCooldowns[key][whoAmI] <= 0) {
                        continue;
                    }

                    has++;
                    NPCSourceID_To_ByItemCooldowns[key][whoAmI]--;
                }
                if (has == 0) {
                    NPCSourceID_To_ByItemCooldowns.Remove(key);
                }
            }

            foreach (var key in NPCSourceID_To_ByProjectileCooldowns.Keys) {
                int has = 0;
                for (int whoAmI = 0; whoAmI < Main.maxPlayers; whoAmI++) {
                    if (NPCSourceID_To_ByProjectileCooldowns[key][whoAmI] <= 0) {
                        continue;
                    }
                    has++;
                    NPCSourceID_To_ByProjectileCooldowns[key][whoAmI]--;
                }
                if (has == 0) {
                    NPCSourceID_To_ByProjectileCooldowns.Remove(key);
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
            if (VaultUtils.AddStaticImmunity(npc, player, item)) {
                npc.immune[player.whoAmI] = 0;
                NPCID_To_HitType[npc.type] = HitType.Item;
            }
        }

        public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone) {
            if (projectile.owner == -1 && projectile.owner >= Main.maxPlayers) {
                return;
            }
            if (VaultUtils.AddStaticImmunity(npc, Main.player[projectile.owner], projectile)) {
                npc.immune[projectile.owner] = 0;
                NPCID_To_HitType[npc.type] = HitType.Projectile;
            }
        }

        public override bool? CanBeHitByItem(NPC npc, Player player, Item item) {
            return VaultUtils.HasStaticImmunity(npc, player, item) ? false : null;
        }

        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) {
            if (projectile.owner == -1 && projectile.owner >= Main.maxPlayers) {
                return null;
            }
            return VaultUtils.HasStaticImmunity(npc, Main.player[projectile.owner], projectile) ? false : null;
        }
    }
}
