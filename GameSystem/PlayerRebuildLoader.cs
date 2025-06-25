using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using static InnoVault.GameSystem.PlayerOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于玩家重制节点的钩子均挂载于此处
    /// </summary>
    public class PlayerRebuildLoader : ModPlayer, IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_ModifyHitNPCWithItem_Dalegate(Player player, Item item, NPC target, ref NPC.HitModifiers modifiers);
        public delegate void On_ModifyHitNPCWithProj_Dalegate(Player player, Projectile proj, NPC target, ref NPC.HitModifiers modifiers);
        public static Type playerLoaderType;
        public static MethodBase onModifyHitNPCWithItemMethod;
        public static MethodBase onModifyHitNPCWithProjMethod;
        void IVaultLoader.LoadData() {
            Instances ??= [];
            TypeToInstance ??= [];

            IL_Player.Update += Player_Update_Hook;

            playerLoaderType = typeof(PlayerLoader);

            MethodBase getPublicStaticMethod(string key) => playerLoaderType.GetMethod(key, BindingFlags.Public | BindingFlags.Static);

            onModifyHitNPCWithItemMethod = getPublicStaticMethod("ModifyHitNPCWithItem");
            onModifyHitNPCWithProjMethod = getPublicStaticMethod("ModifyHitNPCWithProj");

            if (onModifyHitNPCWithItemMethod != null) {
                VaultHook.Add(onModifyHitNPCWithItemMethod, OnModifyHitNPCWithItemHook);
            }
            if (onModifyHitNPCWithProjMethod != null) {
                VaultHook.Add(onModifyHitNPCWithProjMethod, OnModifyHitNPCWithProjHook);
            }
        }

        void IVaultLoader.UnLoadData() {
            Instances?.Clear();
            TypeToInstance?.Clear();

            IL_Player.Update -= Player_Update_Hook;
            playerLoaderType = null;
            onModifyHitNPCWithItemMethod = null;
            onModifyHitNPCWithProjMethod = null;
        }

        private void Player_Update_Hook(ILContext il) {
            ILCursor c = new ILCursor(il);
            ILLabel LabelKey = null;
            Type playerType = typeof(Player);
            FieldInfo itemAnimation = playerType.GetField("itemAnimation", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo ItemTimeIsZero = playerType.GetProperty("ItemTimeIsZero", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo reuseDelay = playerType.GetField("reuseDelay", BindingFlags.Instance | BindingFlags.Public);
            Type mainType = typeof(Main);
            FieldInfo drawingPlayerChat = mainType.GetField("drawingPlayerChat", BindingFlags.Static | BindingFlags.Public);
            FieldInfo selectedItem = playerType.GetField("selectedItem", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo editSign = mainType.GetField("editSign", BindingFlags.Static | BindingFlags.Public);
            FieldInfo editChest = mainType.GetField("editChest", BindingFlags.Static | BindingFlags.Public);

            if (!c.TryGotoNext(
                    MoveType.After,
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld(itemAnimation),
                    x => x.MatchBrtrue(out LabelKey),
                    x => x.MatchLdarg(0),
                    x => x.MatchCall(ItemTimeIsZero.GetMethod),
                    x => x.MatchBrfalse(out LabelKey),
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld(reuseDelay),
                    x => x.MatchBrtrue(out LabelKey)
                    )) {
                return;
            }

            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate(static (Player self) => CanSwitchWeaponHook(self));
            c.Emit(OpCodes.Brfalse, LabelKey);

            if (!c.TryGotoNext(
                MoveType.After,
                x => x.MatchLdcI4(0),
                x => x.MatchStloc(49),
                x => x.MatchLdsfld(drawingPlayerChat),
                x => x.MatchBrtrue(out LabelKey),
                x => x.MatchLdarg(0),
                x => x.MatchLdfld(selectedItem),
                x => x.MatchLdcI4(58),
                x => x.MatchBeq(out LabelKey),
                x => x.MatchLdsfld(editSign),
                x => x.MatchBrtrue(out LabelKey),
                x => x.MatchLdsfld(editChest),
                x => x.MatchBrtrue(out LabelKey)
                )) {
                return;
            }

            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate(static (Player self) => CanSwitchWeaponHook(self));
            c.Emit(OpCodes.Brfalse, LabelKey);
        }

        public static bool CanSwitchWeaponHook(Player player) {
            bool? result = null;
            if (TryFetchByPlayer(player, out var values)) {
                foreach (var value in values.Values) {
                    bool? result2 = value.CanSwitchWeapon();
                    if (result2.HasValue) {
                        result = result2;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            Item item = player.GetItem();
            if (item.type == ItemID.None || item.IsAir) {
                return true;
            }

            if (ItemOverride.TryFetchByID(item.type, out Dictionary<Type, ItemOverride> values2)) {
                foreach (var value in values2.Values) {
                    bool? result2 = value.CanSwitchWeapon(item, player);
                    if (result2.HasValue) {
                        result = result2;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return true;
        }

        private static void OnModifyHitNPCWithItemHook(On_ModifyHitNPCWithItem_Dalegate orig
            , Player player, Item item, NPC target, ref NPC.HitModifiers modifiers) {
            if (TryFetchByPlayer(player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    value.On_ModifyHitNPCWithItem(item, target, ref modifiers);
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(player, item, target, ref modifiers);
        }

        private static void OnModifyHitNPCWithProjHook(On_ModifyHitNPCWithProj_Dalegate orig
            , Player player, Projectile proj, NPC target, ref NPC.HitModifiers modifiers) {
            if (TryFetchByPlayer(player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    value.On_ModifyHitNPCWithProj(proj, target, ref modifiers);
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(player, proj, target, ref modifiers);
        }

        public override bool Shoot(Item item, EntitySource_ItemUse_WithAmmo source
            , Vector2 position, Vector2 velocity, int type, int damage, float knockback) {
            if (item.type == ItemID.None || item.IsAir) {
                return false;
            }

            if (TryFetchByPlayer(Player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    if (value.TargetItemID == ItemID.None || value.TargetItemID == item.type) {
                        result = value.ItemShoot(item, source, position, velocity, type, damage, knockback);
                    }
                }
                return result;
            }

            return true;
        }

        public override bool CanUseItem(Item item) {
            if (item.type == ItemID.None || item.IsAir) {
                return false;
            }

            if (TryFetchByPlayer(Player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    if (value.TargetItemID == ItemID.None || value.TargetItemID == item.type) {
                        result = value.CanUseItem(item);
                    }
                }
                return result;
            }

            return true;
        }
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
