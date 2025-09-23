using InnoVault.StateStruct;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.Graphics;
using Terraria.Graphics.Renderers;
using Terraria.ID;
using Terraria.ModLoader;
using static InnoVault.GameSystem.PlayerOverride;
using static Terraria.Player;

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
        public delegate bool On_CanHitNPC_Dalegate(Player player, NPC target);
        public delegate void On_OnHitNPC_Dalegate(Player player, NPC target, in NPC.HitInfo hit, int damageDone);
        public delegate void On_GiveImmuneTimeForCollisionAttack_Dalegate(Player player, int time);
        public delegate bool On_CanBeHitByProjectile_Dalegate(Player player, Projectile proj);
        public delegate double On_Hurt_Dalegate(Player player, PlayerDeathReason damageSource, int Damage, int hitDirection, out HurtInfo info, bool pvp = false, bool quiet = false
            , int cooldownCounter = -1, bool dodgeable = true, float armorPenetration = 0f, float scalingArmorPenetration = 0f, float knockback = 4.5f);
        public delegate Rectangle On_ItemCheck_EmitUseVisuals_Delegate(Player player, Item sItem, Rectangle itemRectangle);
        public static Type playerLoaderType;
        public static MethodBase onModifyHitNPCWithItemMethod;
        public static MethodBase onModifyHitNPCWithProjMethod;
        public static MethodBase onCanHitNPCMethod;
        public static MethodBase onOnHitNPCMethod;
        public static MethodBase onGiveImmuneTimeForCollisionAttackMethod;
        public static MethodBase onCanBeHitByProjectileMethod;
        public static MethodBase onHurtMethod;
        private static readonly List<VaultHookMethodCache<PlayerOverride>> hooks = [];
        internal static VaultHookMethodCache<PlayerOverride> HookPreIsSceneEffectActiveByPlayer;
        internal static VaultHookMethodCache<PlayerOverride> HookPostIsSceneEffectActiveByPlayer;
        void IVaultLoader.LoadData() {
            foreach (var playerOverride in VaultUtils.GetDerivedInstances<PlayerOverride>()) {
                VaultTypeRegistry<PlayerOverride>.Register(playerOverride);
                foreach (var name in playerOverride.GetActiveSceneEffectFullNames()) {
                    SceneRebuildLoader.ActiveSceneEffects.Add(name);
                }
            }
            VaultTypeRegistry<PlayerOverride>.CompleteLoading();

            HookPreIsSceneEffectActiveByPlayer = AddHook<Func<ModSceneEffect, bool?>>(player => player.PreIsSceneEffectActive);
            HookPostIsSceneEffectActiveByPlayer = AddHook<Action<ModSceneEffect>>(player => player.PostIsSceneEffectActive);

            IL_Player.Update += Player_Update_Hook;

            On_LegacyPlayerRenderer.DrawPlayers += On_DrawPlayersHook;

            playerLoaderType = typeof(PlayerLoader);

            static MethodBase getPublicStaticMethod(string key) => playerLoaderType.GetMethod(key, BindingFlags.Public | BindingFlags.Static);

            onModifyHitNPCWithItemMethod = getPublicStaticMethod("ModifyHitNPCWithItem");
            onModifyHitNPCWithProjMethod = getPublicStaticMethod("ModifyHitNPCWithProj");
            onCanHitNPCMethod = getPublicStaticMethod("CanHitNPC");
            onOnHitNPCMethod = getPublicStaticMethod("OnHitNPC");
            onGiveImmuneTimeForCollisionAttackMethod = typeof(Player).GetMethod("GiveImmuneTimeForCollisionAttack", BindingFlags.Public | BindingFlags.Instance);
            onCanBeHitByProjectileMethod = typeof(CombinedHooks).GetMethod("CanBeHitByProjectile", BindingFlags.Public | BindingFlags.Instance);
            onHurtMethod = typeof(Player).GetMethod(
                "Hurt",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                [
                    typeof(PlayerDeathReason),
                    typeof(int),
                    typeof(int),
                    typeof(HurtInfo).MakeByRefType(),
                    typeof(bool),
                    typeof(bool),
                    typeof(int),
                    typeof(bool),
                    typeof(float),
                    typeof(float),
                    typeof(float)
                ],
                null
            );

            if (onModifyHitNPCWithItemMethod != null) {
                VaultHook.Add(onModifyHitNPCWithItemMethod, On_ModifyHitNPCWithItemHook);
            }
            if (onModifyHitNPCWithProjMethod != null) {
                VaultHook.Add(onModifyHitNPCWithProjMethod, On_ModifyHitNPCWithProjHook);
            }
            if (onCanHitNPCMethod != null) {
                //VaultHook.Add(onCanHitNPCMethod, On_CanHitNPCHook);//此钩子因为性能问题被禁用
            }
            if (onOnHitNPCMethod != null) {
                VaultHook.Add(onOnHitNPCMethod, On_OnHitNPCHook);
            }
            if (onGiveImmuneTimeForCollisionAttackMethod != null) {
                VaultHook.Add(onGiveImmuneTimeForCollisionAttackMethod, On_GiveImmuneTimeForCollisionAttackHook);
            }
            if (onCanBeHitByProjectileMethod != null) {
                VaultHook.Add(onCanBeHitByProjectileMethod, On_CanBeHitByProjectileHook);
            }
            if (onHurtMethod != null) {
                VaultHook.Add(onHurtMethod, On_HurtHook);
            }

            VaultHook.Add(typeof(Player).GetMethod("ItemCheck_EmitUseVisuals", BindingFlags.Instance | BindingFlags.NonPublic), On_ItemCheck_EmitUseVisuals_Hook);
        }

        void IVaultLoader.UnLoadData() {
            Instances?.Clear();
            TypeToInstance?.Clear();

            IL_Player.Update -= Player_Update_Hook;
            On_LegacyPlayerRenderer.DrawPlayers -= On_DrawPlayersHook;
            playerLoaderType = null;
            onModifyHitNPCWithItemMethod = null;
            onModifyHitNPCWithProjMethod = null;
        }

        private static VaultHookMethodCache<PlayerOverride> AddHook<F>(Expression<Func<PlayerOverride, F>> func) where F : Delegate {
            VaultHookMethodCache<PlayerOverride> hook = VaultHookMethodCache<PlayerOverride>.Create(func);
            hooks.Add(hook);
            return hook;
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

        private static void On_ModifyHitNPCWithItemHook(On_ModifyHitNPCWithItem_Dalegate orig
            , Player player, Item item, NPC target, ref NPC.HitModifiers modifiers) {
            if (TryFetchByPlayer(player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    bool newResult = value.On_ModifyHitNPCWithItem(item, target, ref modifiers);
                    if (newResult == false) {
                        result = false;
                    }
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(player, item, target, ref modifiers);
        }

        private static void On_ModifyHitNPCWithProjHook(On_ModifyHitNPCWithProj_Dalegate orig
            , Player player, Projectile proj, NPC target, ref NPC.HitModifiers modifiers) {
            if (TryFetchByPlayer(player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    bool newResult = value.On_ModifyHitNPCWithProj(proj, target, ref modifiers);
                    if (newResult == false) {
                        result = false;
                    }
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(player, proj, target, ref modifiers);
        }

        //private static bool On_CanHitNPCHook(On_CanHitNPC_Dalegate orig, Player player, NPC target) {
        //    if (TryFetchByPlayer(player, out var values)) {
        //        bool? result = null;
        //        foreach (var value in values.Values) {
        //            bool? newResult = value.On_CanHitNPC(target);
        //            if (newResult.HasValue) {
        //                result = newResult.Value;
        //            }
        //        }
        //        if (result.HasValue) {
        //            return result.Value;
        //        }
        //    }

        //    return orig.Invoke(player, target);
        //}

        private static void On_OnHitNPCHook(On_OnHitNPC_Dalegate orig, Player player, NPC target, in NPC.HitInfo hit, int damageDone) {
            if (TryFetchByPlayer(player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    bool? newResult = value.On_OnHitNPC(target, hit, damageDone);
                    if (newResult == false) {
                        result = false;
                    }
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(player, target, hit, damageDone);
        }

        private static double On_HurtHook(On_Hurt_Dalegate orig, Player player, PlayerDeathReason damageSource, int Damage, int hitDirection
            , out HurtInfo info, bool pvp = false, bool quiet = false, int cooldownCounter = -1, bool dodgeable = true
            , float armorPenetration = 0f, float scalingArmorPenetration = 0f, float knockback = 4.5f) {
            var hurtState = new HurtState {
                DamageSource = damageSource,
                Damage = Damage,
                HitDirection = hitDirection,
                PvP = pvp,
                Quiet = quiet,
                CooldownCounter = cooldownCounter,
                Dodgeable = dodgeable,
                ArmorPenetration = armorPenetration,
                ScalingArmorPenetration = scalingArmorPenetration,
                Knockback = knockback,
                Info = default
            };

            if (TryFetchByPlayer(player, out var values)) {
                info = default;
                bool result = true;
                foreach (var value in values.Values) {
                    bool? newResult = value.On_Hurt(ref hurtState);
                    if (newResult == false) {
                        result = false;
                    }
                }
                if (!result) {
                    return 0.0;
                }
            }

            double num = orig.Invoke(player, hurtState.DamageSource, hurtState.Damage, hurtState.HitDirection, out hurtState.Info, hurtState.PvP, hurtState.Quiet, hurtState.CooldownCounter
                , hurtState.Dodgeable, hurtState.ArmorPenetration, hurtState.ScalingArmorPenetration, hurtState.Knockback);
            info = hurtState.Info;
            return num;
        }

        private static void On_GiveImmuneTimeForCollisionAttackHook(On_GiveImmuneTimeForCollisionAttack_Dalegate orig, Player player, int time) {
            if (TryFetchByPlayer(player, out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    bool? newResult = value.On_GiveImmuneTimeForCollisionAttack(time);
                    if (newResult == false) {
                        result = false;
                    }
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(player, time);
        }

        private static bool On_CanBeHitByProjectileHook(On_CanBeHitByProjectile_Dalegate orig, Player player, Projectile proj) {
            if (TryFetchByPlayer(player, out var values)) {
                bool? result = null;
                foreach (var value in values.Values) {
                    bool? newResult = value.On_CanBeHitByProjectile(proj);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(player, proj);
        }

        private static Rectangle On_ItemCheck_EmitUseVisuals_Hook(On_ItemCheck_EmitUseVisuals_Delegate orig, Player player, Item sItem, Rectangle itemRectangle) {
            bool origResult = true;

            bool hasPlayer = TryFetchByPlayer(player, out var values);
            if (hasPlayer) {
                bool result = true;
                foreach (var value in values.Values) {
                    if (value.TargetItemID != ItemID.None && value.TargetItemID != sItem.type) {
                        continue;
                    }
                    bool newResult = value.On_PreEmitUseVisuals(sItem, ref itemRectangle);
                    if (newResult == false) {
                        result = false;
                    }
                }
                if (!result) {
                    origResult = false;
                }
            }

            bool hasOverride = sItem.TryGetOverride(out var itemOverrides);

            if (origResult) {//玩家钩子优先级别高于物品钩子
                if (!ItemRebuildLoader.UniversalForEach(inds => inds.On_PreEmitUseVisuals(sItem, player, ref itemRectangle))) {
                    origResult = false;
                }

                if (origResult && hasOverride) {//物品全局钩子有限级高于物品指向钩子
                    bool result = true;
                    foreach (var value in itemOverrides.Values) {
                        bool newResult = value.On_PreEmitUseVisuals(sItem, player, ref itemRectangle);
                        if (newResult == false) {
                            result = false;
                        }
                    }
                    if (!result) {
                        origResult = false;
                    }
                }
            }

            if (origResult) {//全部通过才执行原函数
                itemRectangle = orig.Invoke(player, sItem, itemRectangle);
            }

            //下面执行Post操作，按优先级倒序执行

            if (hasOverride) {
                foreach (var value in itemOverrides.Values) {
                    value.On_PostEmitUseVisuals(sItem, player, ref itemRectangle);
                }
            }

            ItemRebuildLoader.UniversalForEach(inds => inds.On_PostEmitUseVisuals(sItem, player, ref itemRectangle));

            if (hasPlayer) {
                foreach (var value in values.Values) {
                    if (value.TargetItemID != ItemID.None && value.TargetItemID != sItem.type) {
                        continue;
                    }
                    value.On_PostEmitUseVisuals(sItem, ref itemRectangle);
                }
            }

            return itemRectangle;
        }

        private static void On_DrawPlayersHook(On_LegacyPlayerRenderer.orig_DrawPlayers orig
            , LegacyPlayerRenderer self, Camera camera, IEnumerable<Player> players) {
            if (TryFetchByPlayer(Main.LocalPlayer, out var values)) {
                bool reset = true;
                foreach (var value in values.Values) {
                    if (!value.PreDrawPlayers(camera, players)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }
            orig.Invoke(self, camera, players);
        }

        public override void ResetEffects() {
            if (TryFetchByPlayer(Player, out var values)) {
                foreach (var value in values.Values) {
                    value.ResetEffects();
                }
            }
        }

        public override void PostUpdate() {
            if (TryFetchByPlayer(Player, out var values)) {
                foreach (var value in values.Values) {
                    value.PostUpdate();
                }
            }
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
                    if (value.TargetItemID != ItemID.None && value.TargetItemID != item.type) {
                        continue;
                    }
                    result = value.CanUseItem(item);
                }
                return result;
            }

            return true;
        }
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
