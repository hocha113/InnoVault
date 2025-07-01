using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using Terraria.ModLoader.IO;
using static InnoVault.GameSystem.ItemOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于物品重制节点的钩子均挂载于此处
    /// </summary>
    public class ItemRebuildLoader : GlobalItem, IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        #region On and IL
        public delegate bool On_Item_Dalegate(Item item);
        public delegate void On_Item_Void_Dalegate(Item item);
        public delegate bool On_AllowPrefix_Dalegate(Item item, int pre);
        public delegate void On_SetDefaults_Dalegate(Item item, bool createModItem = true);
        public delegate bool On_Shoot_Dalegate(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback, bool defaultResult = true);
        public delegate void On_ModifyShootStats_Delegate(Item item, Player player, ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback);
        public delegate void On_HitNPC_Delegate(Item item, Player player, NPC target, in NPC.HitInfo hit, int damageDone);
        public delegate void On_HitPvp_Delegate(Item item, Player player, Player target, Player.HurtInfo hurtInfo);
        public delegate void On_ModifyHitNPC_Delegate(Item item, Player player, NPC target, ref NPC.HitModifiers modifiers);
        public delegate bool On_CanUseItem_Delegate(Item item, Player player);
        public delegate bool On_PreDrawInInventory_Delegate(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale);
        public delegate bool? On_UseItem_Delegate(Item item, Player player);
        public delegate void On_UseAnimation_Delegate(Item item, Player player);
        public delegate void On_ModifyWeaponCrit_Delegate(Item item, Player player, ref float crit);
        public delegate void On_ModifyItemLoot_Delegate(Item item, ItemLoot itemLoot);
        public delegate bool On_CanConsumeAmmo_Delegate(Item weapon, Item ammo, Player player);
        public delegate void On_ModifyWeaponDamage_Delegate(Item item, Player player, ref StatModifier damage);
        public delegate void On_UpdateAccessory_Delegate(Item item, Player player, bool hideVisual);
        public delegate bool On_AltFunctionUse_Delegate(Item item, Player player);
        public delegate void On_ModifyTooltips_Dalegate(Item item, List<TooltipLine> tooltips);
        public delegate void On_ModItem_ModifyTooltips_Delegate(object obj, List<TooltipLine> list);
        public delegate List<TooltipLine> On_ModifyTooltips_Delegate(Item item, ref int numTooltips, string[] names, ref string[] text
            , ref bool[] modifier, ref bool[] badModifier, ref int oneDropLogo, out Color?[] overrideColor, int prefixlineIndex);
        public delegate string On_GetItemNameValue_Delegate(int id);
        public delegate string On_GetItemName_get_Delegate(Item item);
        public static event On_Shoot_Dalegate PreShootEvent;
        public static event On_Item_Void_Dalegate PreSetDefaultsEvent;
        public static event On_Item_Void_Dalegate PostSetDefaultsEvent;
        public static event On_ModifyTooltips_Dalegate PreModifyTooltipsEvent;
        public static event On_ModifyTooltips_Dalegate PostModifyTooltipsEvent;
        public static Type itemLoaderType;
        public static MethodBase onMeleePrefixMethod;
        public static MethodBase onRangedPrefixMethod;
        public static MethodBase onAllowPrefixMethod;
        public static MethodBase onSetDefaultsMethod;
        public static MethodBase onShootMethod;
        public static MethodBase onModifyShootStatsMethod;
        public static MethodBase onHitNPCMethod;
        public static MethodBase onHitPvpMethod;
        public static MethodBase onModifyHitNPCMethod;
        public static MethodBase onCanUseItemMethod;
        public static MethodBase onConsumeItemMethod;
        public static MethodBase onPreDrawInInventoryMethod;
        public static MethodBase onUseItemMethod;
        public static MethodBase onUseAnimationMethod;
        public static MethodBase onModifyWeaponCritMethod;
        public static MethodBase onModifyItemLootMethod;
        public static MethodBase onCanConsumeAmmoMethod;
        public static MethodBase onModifyWeaponDamageMethod;
        public static MethodBase onUpdateAccessoryMethod;
        public static MethodBase onAltFunctionUseMethod;
        public static MethodBase onModifyTooltipsMethod;
        public static MethodBase onGetItemNameValueMethod;
        public static MethodBase onItemNamePropertyGetMethod;
        public static MethodBase onAffixNameMethod;
        public static FieldInfo TooltipLine_ModName_Field { get; set; }
        public static FieldInfo TooltipLine_OneDropLogo_Field { get; set; }
        public static GlobalHookList<GlobalItem> ItemLoader_Shoot_Hook { get; private set; }
        public static GlobalHookList<GlobalItem> ItemLoader_CanUse_Hook { get; private set; }
        public static GlobalHookList<GlobalItem> ItemLoader_UseItem_Hook { get; private set; }
        public static GlobalHookList<GlobalItem> ItemLoader_ModifyTooltips_Hook { get; private set; }

        private static GlobalHookList<GlobalItem> GetItemLoaderHookTargetValue(string key)
            => (GlobalHookList<GlobalItem>)typeof(ItemLoader).GetField(key, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

        void IVaultLoader.LoadData() {
            Instances ??= [];
            TypeToInstance ??= [];
            ByID ??= [];

            TooltipLine_ModName_Field = typeof(TooltipLine).GetField("Mod", BindingFlags.Public | BindingFlags.Instance);
            TooltipLine_OneDropLogo_Field = typeof(TooltipLine).GetField("OneDropLogo", BindingFlags.NonPublic | BindingFlags.Instance);

            ItemLoader_Shoot_Hook = GetItemLoaderHookTargetValue("HookShoot");
            ItemLoader_CanUse_Hook = GetItemLoaderHookTargetValue("HookCanUseItem");
            ItemLoader_UseItem_Hook = GetItemLoaderHookTargetValue("HookUseItem");
            ItemLoader_ModifyTooltips_Hook = GetItemLoaderHookTargetValue("HookModifyTooltips");

            itemLoaderType = typeof(ItemLoader);
            onSetDefaultsMethod = itemLoaderType.GetMethod("SetDefaults", BindingFlags.NonPublic | BindingFlags.Static);
            onShootMethod = itemLoaderType.GetMethod("Shoot", BindingFlags.Public | BindingFlags.Static);
            onModifyShootStatsMethod = itemLoaderType.GetMethod("ModifyShootStats", BindingFlags.Public | BindingFlags.Static);
            onHitNPCMethod = itemLoaderType.GetMethod("OnHitNPC", BindingFlags.Public | BindingFlags.Static);
            onHitPvpMethod = itemLoaderType.GetMethod("OnHitPvp", BindingFlags.Public | BindingFlags.Static);
            onModifyHitNPCMethod = itemLoaderType.GetMethod("ModifyHitNPC", BindingFlags.Public | BindingFlags.Static);
            onCanUseItemMethod = itemLoaderType.GetMethod("CanUseItem", BindingFlags.Public | BindingFlags.Static);
            onConsumeItemMethod = itemLoaderType.GetMethod("ConsumeItem", BindingFlags.Public | BindingFlags.Static);
            onPreDrawInInventoryMethod = itemLoaderType.GetMethod("PreDrawInInventory", BindingFlags.Public | BindingFlags.Static);
            onUseItemMethod = itemLoaderType.GetMethod("UseItem", BindingFlags.Public | BindingFlags.Static);
            onUseAnimationMethod = itemLoaderType.GetMethod("UseAnimation", BindingFlags.Public | BindingFlags.Static);
            onModifyWeaponCritMethod = itemLoaderType.GetMethod("ModifyWeaponCrit", BindingFlags.Public | BindingFlags.Static);
            onModifyItemLootMethod = itemLoaderType.GetMethod("ModifyItemLoot", BindingFlags.Public | BindingFlags.Static);
            onCanConsumeAmmoMethod = itemLoaderType.GetMethod("CanConsumeAmmo", BindingFlags.Public | BindingFlags.Static);
            onModifyWeaponDamageMethod = itemLoaderType.GetMethod("ModifyWeaponDamage", BindingFlags.Public | BindingFlags.Static);
            onUpdateAccessoryMethod = itemLoaderType.GetMethod("UpdateAccessory", BindingFlags.Public | BindingFlags.Static);
            onAltFunctionUseMethod = itemLoaderType.GetMethod("AltFunctionUse", BindingFlags.Public | BindingFlags.Static);
            onAllowPrefixMethod = itemLoaderType.GetMethod("AllowPrefix", BindingFlags.Public | BindingFlags.Static);
            onMeleePrefixMethod = itemLoaderType.GetMethod("MeleePrefix", BindingFlags.NonPublic | BindingFlags.Static);
            onRangedPrefixMethod = itemLoaderType.GetMethod("RangedPrefix", BindingFlags.NonPublic | BindingFlags.Static);
            onModifyTooltipsMethod = itemLoaderType.GetMethod("ModifyTooltips", BindingFlags.Public | BindingFlags.Static);
            onGetItemNameValueMethod = typeof(Lang).GetMethod("GetItemNameValue", BindingFlags.Public | BindingFlags.Static);
            onItemNamePropertyGetMethod = typeof(Item).GetProperty("Name", BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
            onAffixNameMethod = typeof(Item).GetMethod("AffixName", BindingFlags.Instance | BindingFlags.Public);

            if (onShootMethod != null) {
                VaultHook.Add(onShootMethod, OnShootHook);
            }
            if (onModifyShootStatsMethod != null) {
                VaultHook.Add(onModifyShootStatsMethod, OnModifyShootStatsHook);
            }
            if (onHitNPCMethod != null) {
                VaultHook.Add(onHitNPCMethod, OnHitNPCHook);
            }
            if (onHitPvpMethod != null) {
                VaultHook.Add(onHitPvpMethod, OnHitPvpHook);
            }
            if (onModifyHitNPCMethod != null) {
                VaultHook.Add(onModifyHitNPCMethod, OnModifyHitNPCHook);
            }
            if (onCanUseItemMethod != null) {
                VaultHook.Add(onCanUseItemMethod, OnCanUseItemHook);
            }
            if (onConsumeItemMethod != null) {
                VaultHook.Add(onConsumeItemMethod, OnConsumeItemHook);
            }
            if (onPreDrawInInventoryMethod != null) {
                VaultHook.Add(onPreDrawInInventoryMethod, OnPreDrawInInventoryHook);
            }
            if (onUseItemMethod != null) {
                VaultHook.Add(onUseItemMethod, OnUseItemHook);
            }
            if (onUseAnimationMethod != null) {
                VaultHook.Add(onUseAnimationMethod, OnUseAnimationHook);
            }
            if (onModifyWeaponCritMethod != null) {
                VaultHook.Add(onModifyWeaponCritMethod, OnModifyWeaponCritHook);
            }
            if (onModifyItemLootMethod != null) {
                VaultHook.Add(onModifyItemLootMethod, OnModifyItemLootHook);
            }
            if (onCanConsumeAmmoMethod != null) {
                VaultHook.Add(onCanConsumeAmmoMethod, OnCanConsumeAmmoHook);
            }
            if (onModifyWeaponDamageMethod != null) {
                VaultHook.Add(onModifyWeaponDamageMethod, OnModifyWeaponDamageHook);
            }
            if (onUpdateAccessoryMethod != null) {
                VaultHook.Add(onUpdateAccessoryMethod, OnUpdateAccessoryHook);
            }
            if (onAltFunctionUseMethod != null) {
                VaultHook.Add(onAltFunctionUseMethod, OnAltFunctionUseHook);
            }
            if (onMeleePrefixMethod != null) {
                VaultHook.Add(onMeleePrefixMethod, OnMeleePrefixHook);
            }
            if (onRangedPrefixMethod != null) {
                VaultHook.Add(onRangedPrefixMethod, OnRangedPrefixHook);
            }
            if (onAllowPrefixMethod != null) {
                VaultHook.Add(onAllowPrefixMethod, OnAllowPrefixHook);
            }
            if (onModifyTooltipsMethod != null) {
                VaultHook.Add(onModifyTooltipsMethod, On_ModifyTooltips_Hook);
            }
            if (onItemNamePropertyGetMethod != null) {
                VaultHook.Add(onItemNamePropertyGetMethod, On_Name_Get_Hook);
            }
            if (onAffixNameMethod != null) {
                VaultHook.Add(onAffixNameMethod, OnAffixNameHook);
            }

            On_Player.UpdateArmorSets += UpdateArmorSetHook;
        }

        void IVaultLoader.UnLoadData() {
            Instances?.Clear();
            TypeToInstance?.Clear();
            ByID?.Clear();

            PreShootEvent = null;
            PreSetDefaultsEvent = null;
            PostSetDefaultsEvent = null;
            PreModifyTooltipsEvent = null;
            PostModifyTooltipsEvent = null;

            TooltipLine_ModName_Field = null;
            TooltipLine_OneDropLogo_Field = null;

            ItemLoader_Shoot_Hook = null;
            ItemLoader_CanUse_Hook = null;
            ItemLoader_UseItem_Hook = null;
            ItemLoader_ModifyTooltips_Hook = null;

            itemLoaderType = null;
            onSetDefaultsMethod = null;
            onShootMethod = null;
            onHitNPCMethod = null;
            onHitPvpMethod = null;
            onModifyHitNPCMethod = null;
            onCanUseItemMethod = null;
            onConsumeItemMethod = null;
            onPreDrawInInventoryMethod = null;
            onUseItemMethod = null;
            onUseAnimationMethod = null;
            onModifyWeaponCritMethod = null;
            onModifyItemLootMethod = null;
            onCanConsumeAmmoMethod = null;
            onModifyWeaponDamageMethod = null;
            onUpdateAccessoryMethod = null;
            onAltFunctionUseMethod = null;
            onMeleePrefixMethod = null;
            onRangedPrefixMethod = null;
            onModifyTooltipsMethod = null;
            onAllowPrefixMethod = null;
            onGetItemNameValueMethod = null;
            onItemNamePropertyGetMethod = null;
            onAffixNameMethod = null;
            On_Player.UpdateArmorSets -= UpdateArmorSetHook;
        }

        public static List<TooltipLine> On_ModifyTooltips_Hook(On_ModifyTooltips_Delegate orig, Item item, ref int numTooltips, string[] names, ref string[] text
            , ref bool[] modifier, ref bool[] badModifier, ref int oneDropLogo, out Color?[] overrideColor, int prefixlineIndex) {
            List<TooltipLine> tooltips = new List<TooltipLine>();
            for (int k = 0; k < numTooltips; k++) {
                TooltipLine tooltip = new TooltipLine(VaultMod.Instance, names[k], text[k]);
                TooltipLine_ModName_Field.SetValue(tooltip, "Terraria");
                tooltip.IsModifier = modifier[k];
                tooltip.IsModifierBad = badModifier[k];
                if (k == oneDropLogo) {
                    //tooltip.OneDropLogo = true;
                    TooltipLine_OneDropLogo_Field.SetValue(tooltip, true);
                }

                tooltips.Add(tooltip);
            }
            if (item.prefix >= PrefixID.Count && prefixlineIndex != -1) {
                IEnumerable<TooltipLine> tooltipLines = PrefixLoader.GetPrefix(item.prefix)?.GetTooltipLines(item);
                if (tooltipLines != null) {
                    foreach (TooltipLine line in tooltipLines) {
                        tooltips.Insert(prefixlineIndex, line);
                        prefixlineIndex++;
                    }
                }
            }

            bool? result = null;
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_ModifyTooltips(item, tooltips);
                }
            }

            if (!result.HasValue) {
                item.ModItem?.ModifyTooltips(tooltips);
                if (!item.IsAir) {
                    foreach (var modifyTooltip in ItemLoader_ModifyTooltips_Hook.Enumerate(item)) {
                        modifyTooltip.ModifyTooltips(item, tooltips);
                    }
                }
            }
            else if (result.Value) {
                item.ModItem?.ModifyTooltips(tooltips);
            }

            tooltips.RemoveAll((x) => !x.Visible);
            numTooltips = tooltips.Count;
            text = new string[numTooltips];
            modifier = new bool[numTooltips];
            badModifier = new bool[numTooltips];
            oneDropLogo = -1;
            overrideColor = new Color?[numTooltips];
            for (int k = 0; k < numTooltips; k++) {
                text[k] = tooltips[k].Text;
                modifier[k] = tooltips[k].IsModifier;
                badModifier[k] = tooltips[k].IsModifierBad;
                if ((bool)TooltipLine_OneDropLogo_Field.GetValue(tooltips[k])) {//tooltips[k].OneDropLogo
                    oneDropLogo = k;
                }
                overrideColor[k] = tooltips[k].OverrideColor;
            }
            return tooltips;
        }

        /// <summary>
        /// <br>这个钩子非常危险，未来很可能移除，因为它钩的是属性的get行为，这可能会带来较大的性能开销和适配性问题，同时，编写代码时也得非常小心，否则可能引起无限迭代让游戏闪退</br>
        /// <br>为什么修改物品名字不使用 Item.SetNameOverride() ？因为这会导致一个难以解决的问题，详情见<see href="https://github.com/tModLoader/tModLoader/issues/4467#issuecomment-2623220787"/> </br>
        /// <br>所以我使用了两个钩子来解决这个名称的覆盖显示，On_Name_Get_Hook改变了Item.Name返回值，因为Name被使用的地方非常多，所以这个钩子需要多加考察才能确认其安全性</br> 
        /// <br>OnAffixNameHook用于改变UI获取物品名字的方式，(不知道为何，明明AffixName的返回值是基于Item.Name的，但On_Name_Get_Hook的修改没能在这上面起作用)</br> 
        /// <br>直观来讲，一个负责改变UI上显示的名字(OnAffixNameHook)，一个负责改变逻辑数据，使其在搜索框之类的功能中能够被以新名字检索到(OnAffixNameHook)</br> 
        /// </summary>
        public static string On_Name_Get_Hook(On_GetItemName_get_Delegate orig, Item item) {
            if (Main.gameMenu) {
                return orig.Invoke(item);
            }

            if (!TryFetchByID(item.type, out Dictionary<Type, ItemOverride> values)) {
                return orig.Invoke(item);
            }

            string result = string.Empty;
            foreach (var value in values.Values) {
                if (!value.CanLoadLocalization) {
                    continue;
                }
                result = value.DisplayName.Value;
            }

            if (result != string.Empty) {
                return result;
            }

            return orig.Invoke(item);
        }

        public static string OnAffixNameHook(On_GetItemName_get_Delegate orig, Item item) {
            if (Main.gameMenu) {
                return orig.Invoke(item);
            }

            bool onOverd = false;
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> values)) {
                string result = string.Empty;
                foreach (var value in values.Values) {
                    if (!value.CanLoadLocalization) {
                        continue;
                    }
                    result = value.DisplayName.Value;
                }

                if (result != string.Empty) {
                    item.SetNameOverride(result);
                    onOverd = true;
                }
            }
            //这是个很取巧的办法，保证了兼容性
            //因为上面已经将名称重命名了，所以这里会以重命名的内容进入原版的处理
            string forgtName = orig.Invoke(item);
            //清除物品的重命名状态
            if (onOverd) {
                item.ClearNameOverride();
            }
            return forgtName;
        }

        public static bool OnAllowPrefixHook(On_AllowPrefix_Dalegate orig, Item item, int pre) {
            if (ItemAllowPrefixDic.TryGetValue(item.type, out bool? value)) {
                if (value.HasValue) {
                    return value.Value;
                }
            }
            else {
                ItemAllowPrefixDic[item.type] = null;
            }

            return orig.Invoke(item, pre);
        }

        public static bool OnMeleePrefixHook(On_Item_Dalegate orig, Item item) {
            if (ItemMeleePrefixDic.TryGetValue(item.type, out bool? value)) {
                if (value.HasValue) {
                    return value.Value;
                }
            }
            else {
                ItemMeleePrefixDic[item.type] = null;
            }

            return orig.Invoke(item);
        }

        public static bool OnRangedPrefixHook(On_Item_Dalegate orig, Item item) {
            if (ItemRangedPrefixDic.TryGetValue(item.type, out bool? value)) {
                if (value.HasValue) {
                    return value.Value;
                }
            }
            else {
                ItemRangedPrefixDic[item.type] = null;
            }

            return orig.Invoke(item);
        }

        /// <summary>
        /// 提前于 TML 的方法执行，这样继承重写 <see cref="ItemOverride.On_AltFunctionUse"/> 便拥有阻断后续逻辑的能力，用于进行一些高级修改。
        /// 若多个覆盖器返回了非空值，则优先返回最后一个非空值。
        /// </summary>
        public static bool OnAltFunctionUseHook(On_AltFunctionUse_Delegate orig, Item item, Player player) {
            if (item.IsAir) {
                return false;
            }

            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;

                foreach (var overrideInstance in itemOverrides.Values) {
                    var value = overrideInstance.On_AltFunctionUse(item, player);
                    if (value.HasValue) {
                        result = value;
                    }
                }

                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(item, player);
        }
        /// <summary>
        /// 提前于 TML 的方法执行，这样继承重写 <see cref="ItemOverride.On_UpdateAccessory"/> 便拥有阻断后续逻辑的能力，用于进行一些高级修改。
        /// 若任意一个覆盖器返回 <c>false</c>，则立即中止原方法执行。
        /// </summary>
        public static void OnUpdateAccessoryHook(On_UpdateAccessory_Delegate orig, Item item, Player player, bool hideVisual) {
            if (item.IsAir) {
                return;
            }

            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                foreach (var overrideInstance in itemOverrides.Values) {
                    if (!overrideInstance.On_UpdateAccessory(item, player, hideVisual)) {
                        return;//阻断执行
                    }
                }
            }

            orig.Invoke(item, player, hideVisual);
        }
        /// <summary>
        /// 提前于TML的方法执行，这样继承重写<br/><see cref="ItemOverride.On_CanConsumeAmmo"/><br/>便拥有可以阻断TML后续方法运行的能力，用于进行一些高级修改
        /// </summary>
        public static void OnModifyWeaponDamageHook(On_ModifyWeaponDamage_Delegate orig, Item item, Player player, ref StatModifier damage) {
            if (item.IsAir) {
                return;
            }

            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                foreach (var overrideInstance in itemOverrides.Values) {
                    if (!overrideInstance.On_ModifyWeaponDamage(item, player, ref damage)) {
                        return;//阻断执行
                    }
                }
            }

            orig.Invoke(item, player, ref damage);
        }

        /// <summary>
        /// 提前于TML的方法执行，这样继承重写<br/><see cref="ItemOverride.On_CanConsumeAmmo"/><br/>便拥有可以阻断TML后续方法运行的能力，用于进行一些高级修改
        /// </summary>
        public static bool OnCanConsumeAmmoHook(On_CanConsumeAmmo_Delegate orig, Item item, Item ammo, Player player) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_CanConsumeAmmo(item, ammo, player);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(item, ammo, player);
        }
        /// <summary>
        /// 这个钩子用于挂载一个提前于TML方法的<see cref="ItemLoader.ModifyItemLoot"/>，以此来进行一些高级的修改
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="item"></param>
        /// <param name="itemLoot"></param>
        public static void OnModifyItemLootHook(On_ModifyItemLoot_Delegate orig, Item item, ItemLoot itemLoot) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_ModifyItemLoot(item, itemLoot);
                }

                if (result.HasValue) {
                    if (result.Value) {
                        item.ModItem?.ModifyItemLoot(itemLoot);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(item, itemLoot);
        }
        /// <summary>
        /// 这个钩子用于挂载一个提前于TML方法的<see cref="ItemLoader.ModifyWeaponCrit"/>，以此来进行一些高级的修改
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="crit"></param>
        public static void OnModifyWeaponCritHook(On_ModifyWeaponCrit_Delegate orig, Item item, Player player, ref float crit) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_ModifyWeaponCrit(item, player, ref crit);
                }

                if (result.HasValue) {
                    if (result.Value) {
                        item.ModItem?.ModifyWeaponCrit(player, ref crit);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(item, player, ref crit);
        }
        /// <summary>
        /// 这个钩子用于挂载一个提前于TML方法的<see cref="ItemLoader.UseAnimation(Item, Player)"/>，以此来进行一些高级的修改
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        public static void OnUseAnimationHook(On_UseAnimation_Delegate orig, Item item, Player player) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_UseAnimation(item, player);
                }

                if (result.HasValue) {
                    if (result.Value) {
                        item.ModItem?.UseAnimation(player);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(item, player);
        }
        /// <summary>
        /// 这个钩子用于挂载一个提前于TML方法的<see cref="ItemLoader.UseItem(Item, Player)"/>，以此来进行一些高级的修改
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        public static bool? OnUseItemHook(On_UseItem_Delegate orig, Item item, Player player) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_UseItem(item, player);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig(item, player);
        }
        /// <summary>
        /// 提前于TML的方法执行，这样继承重写<br/><see cref="ItemOverride.On_Shoot"/><br/>便拥有可以阻断TML后续方法运行的能力，用于进行一些高级修改
        /// </summary>
        public static bool OnShootHook(On_Shoot_Dalegate orig, Item item, Player player, EntitySource_ItemUse_WithAmmo source
            , Vector2 position, Vector2 velocity, int type, int damage, float knockback, bool defaultResult) {

            if (item.type > ItemID.None) {
                bool? eventResult = PreShootEvent?.Invoke(item, player, source, position, velocity, type, damage, knockback, defaultResult);
                if (eventResult.HasValue) {
                    if (eventResult.Value) {
                        return orig.Invoke(item, player, source, position, velocity, type, damage, knockback);
                    }
                    else {
                        return false;
                    }
                }
            }

            bool? result = null;

            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_Shoot(item, player, source, position, velocity, type, damage, knockback);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            result = ProcessRemakeAction(item, (inds) => inds.Shoot(item, player, source, position, velocity, type, damage, knockback));

            if (result.HasValue) {
                return result.Value;
            }

            return orig.Invoke(item, player, source, position, velocity, type, damage, knockback);
        }
        /// <summary>
        /// 提前于TML的方法执行，这样继承重写<br/><see cref="ItemOverride.On_ModifyShootStats"/><br/>便拥有可以阻断TML后续方法运行的能力，用于进行一些高级修改
        /// </summary>
        public static void OnModifyShootStatsHook(On_ModifyShootStats_Delegate orig, Item item, Player player
            , ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_ModifyShootStats(item, player, ref position, ref velocity, ref type, ref damage, ref knockback);
                }
                if (result.HasValue) {
                    if (result.Value) {
                        item.ModItem?.ModifyShootStats(player, ref position, ref velocity, ref type, ref damage, ref knockback);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(item, player, ref position, ref velocity, ref type, ref damage, ref knockback);
        }
        /// <summary>
        /// 提前于TML的方法执行，这个钩子可以用来做到<see cref="GlobalItem.CanUseItem"/>无法做到的修改效果，比如让一些原本不可使用的物品可以使用，
        /// <br/>继承重写<see cref="ItemOverride.On_CanUseItem(Item, Player)"/>来达到这些目的，用于进行一些高级修改
        /// </summary>
        public static bool OnCanUseItemHook(On_CanUseItem_Delegate orig, Item item, Player player) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_CanUseItem(item, player);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(item, player);
        }
        /// <summary>
        /// 提前于TML的方法执行，这个钩子可以用来做到<see cref="GlobalItem.ConsumeItem"/>无法做到的修改效果，比如让一些原本不可使用的物品可以使用，
        /// <br/>继承重写<see cref="ItemOverride.On_ConsumeItem(Item, Player)"/>来达到这些目的，用于进行一些高级修改
        /// </summary>
        public static bool OnConsumeItemHook(On_CanUseItem_Delegate orig, Item item, Player player) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_ConsumeItem(item, player);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(item, player);
        }

        public static void OnHitNPCHook(On_HitNPC_Delegate orig, Item item, Player player, NPC target, in NPC.HitInfo hit, int damageDone) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool result = true;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_OnHitNPC(item, player, target, hit, damageDone);
                }
                if (!result) {
                    return;
                }
            }

            orig.Invoke(item, player, target, hit, damageDone);
        }

        public static void OnHitPvpHook(On_HitPvp_Delegate orig, Item item, Player player, Player target, Player.HurtInfo hurtInfo) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool result = true;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_OnHitPvp(item, player, target, hurtInfo);
                }
                if (!result) {
                    return;
                }
            }

            orig.Invoke(item, player, target, hurtInfo);
        }

        public static void OnModifyHitNPCHook(On_ModifyHitNPC_Delegate orig, Item item, Player player, NPC target, ref NPC.HitModifiers modifiers) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_ModifyHitNPC(item, player, target, ref modifiers);
                }
                if (result.HasValue) {
                    if (result.Value) {
                        item.ModItem?.ModifyHitNPC(player, target, ref modifiers);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(item, player, target, ref modifiers);
        }

        public static bool OnPreDrawInInventoryHook(On_PreDrawInInventory_Delegate orig, Item item, SpriteBatch spriteBatch
            , Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                bool? result = null;
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = overrideInstance.On_PreDrawInInventory(item, spriteBatch, position, frame, drawColor, itemColor, origin, scale);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(item, spriteBatch, position, frame, drawColor, itemColor, origin, scale);
        }
        #endregion

        #region Loader Item Hook
        public static void ProcessRemakeAction(Item item, Action<ItemOverride> action) {
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                foreach (var overrideInstance in itemOverrides.Values) {
                    action(overrideInstance);
                }
            }
        }

        public static bool? ProcessRemakeAction(Item item, Func<ItemOverride, bool?> action) {
            bool? result = null;
            if (TryFetchByID(item.type, out Dictionary<Type, ItemOverride> itemOverrides)) {
                foreach (var overrideInstance in itemOverrides.Values) {
                    result = action(overrideInstance);
                }
            }
            return result;
        }

        public override void SetDefaults(Item item) {
            if (item.type > ItemID.None) {
                PreSetDefaultsEvent?.Invoke(item);
            }
            ProcessRemakeAction(item, (inds) => inds.SetDefaults(item));
            if (item.type > ItemID.None) {
                PostSetDefaultsEvent?.Invoke(item);
            }
        }

        public override void PostDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
            ProcessRemakeAction(item, (inds) => inds.PostDrawInInventory(item, spriteBatch, position, frame, drawColor, itemColor, origin, scale));
        }

        public override bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.PreDrawInInventory(item, spriteBatch, position, frame, drawColor, itemColor, origin, scale));
            return rest ?? base.PreDrawInInventory(item, spriteBatch, position, frame, drawColor, itemColor, origin, scale);
        }

        public override bool AllowPrefix(Item item, int pre) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.AllowPrefix(item, pre));
            return rest ?? base.AllowPrefix(item, pre);
        }

        public override bool AltFunctionUse(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.AltFunctionUse(item, player));
            return rest ?? base.AltFunctionUse(item, player);
        }

        public override bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) {
            bool? rest = ProcessRemakeAction(equippedItem, (inds) => inds.CanAccessoryBeEquippedWith(equippedItem, incomingItem, player));
            return rest ?? base.CanAccessoryBeEquippedWith(equippedItem, incomingItem, player);
        }

        public override bool? CanBeChosenAsAmmo(Item ammo, Item weapon, Player player) {
            bool? rest = ProcessRemakeAction(ammo, (inds) => inds.CanBeChosenAsAmmo(ammo, weapon, player));
            return rest ?? base.CanBeChosenAsAmmo(ammo, weapon, player);
        }

        public override bool CanBeConsumedAsAmmo(Item ammo, Item weapon, Player player) {
            bool? rest = ProcessRemakeAction(ammo, (inds) => inds.CanBeConsumedAsAmmo(ammo, weapon, player));
            return rest ?? base.CanBeConsumedAsAmmo(ammo, weapon, player);
        }

        public override bool? CanCatchNPC(Item item, NPC target, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanCatchNPC(item, target, player));
            return rest ?? base.CanCatchNPC(item, target, player);
        }

        public override bool CanEquipAccessory(Item item, Player player, int slot, bool modded) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanEquipAccessory(item, player, slot, modded));
            return rest ?? base.CanEquipAccessory(item, player, slot, modded);
        }

        public override bool? CanHitNPC(Item item, Player player, NPC target) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanHitNPC(item, player, target));
            return rest ?? base.CanHitNPC(item, player, target);
        }

        public override bool CanHitPvp(Item item, Player player, Player target) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanHitPvp(item, player, target));
            return rest ?? base.CanHitPvp(item, player, target);
        }

        public override bool? CanMeleeAttackCollideWithNPC(Item item, Rectangle meleeAttackHitbox, Player player, NPC target) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanMeleeAttackCollideWithNPC(item, meleeAttackHitbox, player, target));
            return rest ?? base.CanMeleeAttackCollideWithNPC(item, meleeAttackHitbox, player, target);
        }

        public override bool CanPickup(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanPickup(item, player));
            return rest ?? base.CanPickup(item, player);
        }

        public override bool CanReforge(Item item) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanReforge(item));
            return rest ?? base.CanReforge(item);
        }

        public override bool CanResearch(Item item) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanResearch(item));
            return rest ?? base.CanResearch(item);
        }

        public override bool CanRightClick(Item item) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanRightClick(item));
            return rest ?? base.CanRightClick(item);
        }

        public override bool CanShoot(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanShoot(item, player));
            return rest ?? base.CanShoot(item, player);
        }

        public override bool CanStack(Item destination, Item source) {
            bool? rest = ProcessRemakeAction(destination, (inds) => inds.CanStack(destination, source));
            return rest ?? base.CanStack(destination, source);
        }

        public override bool CanStackInWorld(Item destination, Item source) {
            bool? rest = ProcessRemakeAction(destination, (inds) => inds.CanStackInWorld(destination, source));
            return rest ?? base.CanStackInWorld(destination, source);
        }

        public override bool CanUseItem(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.CanUseItem(item, player));
            return rest ?? base.CanUseItem(item, player);
        }

        public override bool ConsumeItem(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.ConsumeItem(item, player));
            return rest ?? base.ConsumeItem(item, player);
        }

        public override void HoldItem(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.HoldItem(item, player));
        }

        public override void HoldItemFrame(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.HoldItemFrame(item, player));
        }

        public override void LoadData(Item item, TagCompound tag) {
            ProcessRemakeAction(item, (inds) => inds.LoadData(item, tag));
        }

        public override void MeleeEffects(Item item, Player player, Rectangle hitbox) {
            ProcessRemakeAction(item, (inds) => inds.MeleeEffects(item, player, hitbox));
        }

        public override void ModifyHitNPC(Item item, Player player, NPC target, ref NPC.HitModifiers modifiers) {
            NPC.HitModifiers hitNPCModifier = modifiers;
            ProcessRemakeAction(item, (inds) => inds.ModifyHitNPC(item, player, target, ref hitNPCModifier));
            modifiers = hitNPCModifier;
        }

        public override void ModifyHitPvp(Item item, Player player, Player target, ref Player.HurtModifiers modifiers) {
            Player.HurtModifiers hitPlayerModifier = modifiers;
            ProcessRemakeAction(item, (inds) => inds.ModifyHitPvp(item, player, target, ref hitPlayerModifier));
            modifiers = hitPlayerModifier;
        }

        public override void ModifyItemLoot(Item item, ItemLoot itemLoot) {
            ProcessRemakeAction(item, (inds) => inds.ModifyItemLoot(item, itemLoot));
        }

        public override void ModifyItemScale(Item item, Player player, ref float scale) {
            float slp = scale;
            ProcessRemakeAction(item, (inds) => inds.ModifyItemScale(item, player, ref slp));
            scale = slp;
        }

        public override void ModifyManaCost(Item item, Player player, ref float reduce, ref float mult) {
            float newReduce = reduce;
            float newMult = mult;
            ProcessRemakeAction(item, (inds) => inds.ModifyManaCost(item, player, ref newReduce, ref newMult));
            reduce = newReduce;
            mult = newMult;
        }

        public override void ModifyShootStats(Item item, Player player, ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback) {
            ItemShootState stats = new() {
                Position = position,
                Velocity = velocity,
                Type = type,
                Damage = damage,
                Knockback = knockback
            };
            ProcessRemakeAction(item, (inds) => inds.ModifyShootStats(item, player, ref stats));
            position = stats.Position;
            velocity = stats.Velocity;
            type = stats.Type;
            damage = stats.Damage;
            knockback = stats.Knockback;
        }

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips) {
            if (item.type > ItemID.None) {
                PreModifyTooltipsEvent?.Invoke(item, tooltips);
            }
            ProcessRemakeAction(item, (inds) => inds.ModifyTooltips(item, tooltips));
            if (item.type > ItemID.None) {
                PostModifyTooltipsEvent?.Invoke(item, tooltips);
            }
        }

        public override void ModifyWeaponCrit(Item item, Player player, ref float crit) {
            float safeCrit = crit;
            ProcessRemakeAction(item, (inds) => inds.ModifyWeaponCrit(item, player, ref safeCrit));
            crit = safeCrit;
        }

        public override void ModifyWeaponDamage(Item item, Player player, ref StatModifier damage) {
            StatModifier safeDamage = damage;
            ProcessRemakeAction(item, (inds) => inds.ModifyWeaponDamage(item, player, ref safeDamage));
            damage = safeDamage;
        }

        public override void ModifyWeaponKnockback(Item item, Player player, ref StatModifier knockback) {
            StatModifier safeKnockback = knockback;
            ProcessRemakeAction(item, (inds) => inds.ModifyWeaponKnockback(item, player, ref safeKnockback));
            knockback = safeKnockback;
        }

        public override void OnConsumeAmmo(Item weapon, Item ammo, Player player) {
            ProcessRemakeAction(ammo, (inds) => inds.OnConsumeAmmo(weapon, ammo, player));
        }

        public override void OnConsumedAsAmmo(Item ammo, Item weapon, Player player) {
            ProcessRemakeAction(ammo, (inds) => inds.OnConsumedAsAmmo(ammo, weapon, player));
        }

        public override void OnConsumeItem(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.OnConsumeItem(item, player));
        }

        public override void OnConsumeMana(Item item, Player player, int manaConsumed) {
            ProcessRemakeAction(item, (inds) => inds.OnConsumeMana(item, player, manaConsumed));
        }

        public override void OnHitNPC(Item item, Player player, NPC target, NPC.HitInfo hit, int damageDone) {
            ProcessRemakeAction(item, (inds) => inds.OnHitNPC(item, player, target, hit, damageDone));
        }

        public override void OnHitPvp(Item item, Player player, Player target, Player.HurtInfo hurtInfo) {
            ProcessRemakeAction(item, (inds) => inds.OnHitPvp(item, player, target, hurtInfo));
        }

        public override void OnMissingMana(Item item, Player player, int neededMana) {
            ProcessRemakeAction(item, (inds) => inds.OnMissingMana(item, player, neededMana));
        }

        public override bool OnPickup(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.OnPickup(item, player));
            return rest ?? base.OnPickup(item, player);
        }

        public override void OnSpawn(Item item, IEntitySource source) {
            ProcessRemakeAction(item, (inds) => inds.OnSpawn(item, source));
        }

        public override void OnStack(Item destination, Item source, int numToTransfer) {
            ProcessRemakeAction(destination, (inds) => inds.OnStack(destination, source, numToTransfer));
        }

        public override void PickAmmo(Item weapon, Item ammo, Player player, ref int type, ref float speed, ref StatModifier damage, ref float knockback) {
            int safeType = type;
            float safeSpeed = speed;
            float safeKnockback = knockback;
            StatModifier safeDamage = damage;
            ProcessRemakeAction(weapon, (inds) => inds.PickAmmo(weapon, ammo, player, ref safeType, ref safeSpeed, ref safeDamage, ref safeKnockback));
            type = safeType;
            speed = safeSpeed;
            knockback = safeKnockback;
            safeDamage = damage;
        }

        public override void RightClick(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.RightClick(item, player));
        }

        public override void SaveData(Item item, TagCompound tag) {
            ProcessRemakeAction(item, (inds) => inds.SaveData(item, tag));
        }

        public override void SplitStack(Item destination, Item source, int numToTransfer) {
            ProcessRemakeAction(destination, (inds) => inds.SplitStack(destination, source, numToTransfer));
        }

        public override void Update(Item item, ref float gravity, ref float maxFallSpeed) {
            float safeGravity = gravity;
            float safeMaxFallSpeed = maxFallSpeed;
            ProcessRemakeAction(item, (inds) => inds.Update(item, ref safeGravity, ref safeMaxFallSpeed));
            gravity = safeGravity;
            maxFallSpeed = safeMaxFallSpeed;
        }

        public static void UpdateArmorSetHook(On_Player.orig_UpdateArmorSets orig, Player player, int i) {
            orig(player, i);
            ProcessRemakeAction(player.armor[0], (inds) => inds.UpdateArmorByHead(player, player.armor[1], player.armor[2]));
        }

        public override void UpdateAccessory(Item item, Player player, bool hideVisual) {
            ProcessRemakeAction(item, (inds) => inds.UpdateAccessory(item, player, hideVisual));
        }

        public override void UpdateEquip(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.UpdateEquip(item, player));
        }

        public override void UpdateInventory(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.UpdateInventory(item, player));
        }

        public override void UseAnimation(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.UseAnimation(item, player));
        }

        public override bool? UseItem(Item item, Player player) {
            bool? rest = ProcessRemakeAction(item, (inds) => inds.UseItem(item, player));
            return rest ?? base.UseItem(item, player);
        }

        public override void UseItemFrame(Item item, Player player) {
            ProcessRemakeAction(item, (inds) => inds.UseItemFrame(item, player));
        }

        public override void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox) {
            Rectangle safeHitbox = hitbox;
            bool safeNoHitbox = noHitbox;
            ProcessRemakeAction(item, (inds) => inds.UseItemFrame(item, player));
            hitbox = safeHitbox;
            noHitbox = safeNoHitbox;
        }

        public override void UseStyle(Item item, Player player, Rectangle heldItemFrame) {
            ProcessRemakeAction(item, (inds) => inds.UseStyle(item, player, heldItemFrame));
        }

        #endregion
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
