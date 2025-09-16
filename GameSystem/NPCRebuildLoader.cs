using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using static InnoVault.GameSystem.NPCOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 所有关于NPC行为覆盖和性质加载的钩子在此处挂载
    /// </summary>
    public class NPCRebuildLoader : GlobalNPC, IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        #region Data
        public delegate void On_NPCDelegate(NPC npc);
        public delegate bool On_NPCDelegate2(NPC npc);
        public delegate bool On_DrawDelegate(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);
        public delegate void On_DrawDelegate2(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);
        public delegate void On_OnHitByProjectileDelegate(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone);
        public delegate void On_ModifyIncomingHitDelegate(NPC npc, ref NPC.HitModifiers modifiers);
        public delegate void On_FindFrameDelegate(NPC npc, int frameHeight);
        public delegate void On_NPCSetDefaultDelegate();
        public static event On_NPCDelegate PreSetDefaultsEvent;
        public static event On_NPCDelegate PostSetDefaultsEvent;
        public static Type npcLoaderType;
        public static MethodInfo onHitByProjectile_Method;
        public static MethodInfo modifyIncomingHit_Method;
        public static MethodInfo onFindFrame_Method;
        public static MethodInfo onNPCAI_Method;
        public static MethodInfo onPreKill_Method;
        public static MethodInfo onPreDraw_Method;
        public static MethodInfo onPostDraw_Method;
        public static MethodInfo onCheckDead_Method;
        public override bool InstancePerEntity => true;
        public Dictionary<Type, NPCOverride> NPCOverrides { get; internal set; }
        #endregion

        void IVaultLoader.LoadData() {
            npcLoaderType = typeof(NPCLoader);
            Instances ??= [];
            ByID ??= [];
            LoaderMethodAndHook();
        }

        void IVaultLoader.UnLoadData() {
            Instances?.Clear();
            ByID?.Clear();
            PreSetDefaultsEvent = null;
            PostSetDefaultsEvent = null;
            npcLoaderType = null;
            onHitByProjectile_Method = null;
            modifyIncomingHit_Method = null;
            onFindFrame_Method = null;
            onNPCAI_Method = null;
            onPreKill_Method = null;
            onPreDraw_Method = null;
            onPostDraw_Method = null;
            onCheckDead_Method = null;
        }

        public override GlobalNPC Clone(NPC from, NPC to) {
            NPCRebuildLoader rebuildLoader = (NPCRebuildLoader)base.Clone(from, to);
            rebuildLoader.NPCOverrides = NPCOverrides;
            return rebuildLoader;
        }

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => lateInstantiation && ByID.ContainsKey(entity.type);

        public override void SetDefaults(NPC npc) {
            if (npc.Alives()) {
                PreSetDefaultsEvent?.Invoke(npc);
            }
            NPCOverride.SetDefaults(npc);
            if (npc.Alives()) {
                PostSetDefaultsEvent?.Invoke(npc);
            }
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyHitByItem(player, item, ref modifiers);
                }
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyHitByProjectile(projectile, ref modifiers);
                }
            }
        }

        public override bool CheckActive(NPC npc) {
            bool result = true;
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    if (!value.CheckActive()) {
                        result = false;
                    }
                }
            }
            return result;
        }

        public override void BossHeadSlot(NPC npc, ref int index) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.BossHeadSlot(ref index);
                }
            }
        }

        public override void BossHeadRotation(NPC npc, ref float rotation) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.BossHeadRotation(ref rotation);
                }
            }
        }

        public override bool? DrawHealthBar(NPC npc, byte hbPosition, ref float scale, ref Vector2 position) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.DrawHealthBar(hbPosition, ref scale, ref position);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        public override void ModifyHoverBoundingBox(NPC npc, ref Rectangle boundingBox) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyHoverBoundingBox(ref boundingBox);
                }
            }
        }

        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot) {
            //不要用TryFetchByID或者直接访问NPCOverride
            if (ByID.TryGetValue(npc.type, out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyNPCLoot(npc, npcLoot);
                }
            }
        }

        public override bool? CanFallThroughPlatforms(NPC npc) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanFallThroughPlatforms();
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        public override void ModifyActiveShop(NPC npc, string shopName, Item[] items) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyActiveShop(shopName, items);
                }
            }
        }

        public override void GetChat(NPC npc, ref string chat) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.GetChat(ref chat);
                }
            }
        }

        public override bool? CanBeHitByItem(NPC npc, Player player, Item item) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanBeHitByItem(player, item);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        public override bool CanBeHitByNPC(NPC npc, NPC attacker) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanBeHitByNPC(attacker);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return true;
        }

        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanBeHitByProjectile(projectile);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        private static MethodInfo GetMethodInfo(string key) => npcLoaderType.GetMethod(key, BindingFlags.Public | BindingFlags.Static);

        private static void DompLog(string name) => VaultMod.Instance.Logger.Info($"ERROR:Fail To Load! {name} Is Null!");

        private static void LoaderMethodAndHook() {
            {
                onHitByProjectile_Method = GetMethodInfo("OnHitByProjectile");
                if (onHitByProjectile_Method != null) {
                    VaultHook.Add(onHitByProjectile_Method, OnHitByProjectileHook);
                }
                else {
                    DompLog("onHitByProjectile_Method");
                }
            }
            {
                modifyIncomingHit_Method = GetMethodInfo("ModifyIncomingHit");
                if (modifyIncomingHit_Method != null) {
                    VaultHook.Add(modifyIncomingHit_Method, ModifyIncomingHitHook);
                }
                else {
                    DompLog("modifyIncomingHit_Method");
                }
            }
            {
                onFindFrame_Method = GetMethodInfo("FindFrame");
                if (onFindFrame_Method != null) {
                    VaultHook.Add(onFindFrame_Method, OnFindFrameHook);
                }
                else {
                    DompLog("onFindFrame_Method");
                }
            }
            {
                onNPCAI_Method = GetMethodInfo("NPCAI");
                if (onNPCAI_Method != null) {
                    VaultHook.Add(onNPCAI_Method, OnNPCAIHook);
                }
                else {
                    DompLog("onNPCAI_Method");
                }
            }
            {
                onPreDraw_Method = GetMethodInfo("PreDraw");
                if (onPreDraw_Method != null) {
                    VaultHook.Add(onPreDraw_Method, OnPreDrawHook);
                }
                else {
                    DompLog("onPreDraw_Method");
                }
            }
            {
                onPostDraw_Method = GetMethodInfo("PostDraw");
                if (onPostDraw_Method != null) {
                    VaultHook.Add(onPostDraw_Method, OnPostDrawHook);
                }
                else {
                    DompLog("onPostDraw_Method");
                }
            }
            {
                onCheckDead_Method = GetMethodInfo("CheckDead");
                if (onCheckDead_Method != null) {
                    VaultHook.Add(onCheckDead_Method, OnCheckDeadHook);
                }
                else {
                    DompLog("onCheckDead_Method");
                }
            }
            {
                onPreKill_Method = GetMethodInfo("PreKill");
                if (onPreKill_Method != null) {
                    VaultHook.Add(onPreKill_Method, OnPreKillHook);
                }
                else {
                    DompLog("onPreKill_Method");
                }
            }
        }

        public static bool OnPreKillHook(On_NPCDelegate2 orig, NPC npc) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.On_PreKill();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }

                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static bool OnCheckDeadHook(On_NPCDelegate2 orig, NPC npc) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.CheckDead();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }

                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static void OnNPCAIHook(On_NPCDelegate orig, NPC npc) {
            if (npc.type == NPCID.None || !npc.active) {
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool result = true;
                int type = npc.type;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.AI()) {
                        result = false;
                    }
                    npcOverrideInstance.DoNetWork();
                }
                npc.type = type;
                if (!result) {
                    return;
                }
            }

            try {
                orig.Invoke(npc);
            } catch {
                npc.active = false;
            }
        }

        public static bool OnPreDrawHook(On_DrawDelegate orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (npc.type == NPCID.None || !npc.active) {
                return false;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.Draw(spriteBatch, screenPos, drawColor);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            try {
                return orig.Invoke(npc, spriteBatch, screenPos, drawColor);
            } catch {
                return true;
            }
        }

        public static void OnPostDrawHook(On_DrawDelegate2 orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (npc.type == NPCID.None || !npc.active) {
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool result = true;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.PostDraw(spriteBatch, screenPos, drawColor)) {
                        result = false;
                    }
                }
                if (!result) {
                    return;
                }
            }

            orig.Invoke(npc, spriteBatch, screenPos, drawColor);
        }

        public static void OnHitByProjectileHook(On_OnHitByProjectileDelegate orig, NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            foreach (var inds in Instances) {
                if (inds.TargetID != NPCID.None && inds.TargetID != npc.type) {
                    continue;
                }

                bool? shouldOverride = null;
                if (inds.On_OnHitByProjectile_IfSpan(projectile)) {
                    shouldOverride = inds.On_OnHitByProjectile(npc, projectile, hit, damageDone);
                }

                if (shouldOverride.HasValue) {
                    if (shouldOverride.Value) {
                        npc.ModNPC?.OnHitByProjectile(projectile, hit, damageDone);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(npc, projectile, hit, damageDone);
        }

        public static void ModifyIncomingHitHook(On_ModifyIncomingHitDelegate orig, NPC npc, ref NPC.HitModifiers modifiers) {
            foreach (var inds in Instances) {
                if (inds.TargetID != NPCID.None && inds.TargetID != npc.type) {
                    continue;
                }

                bool? shouldOverride = inds.On_ModifyIncomingHit(npc, ref modifiers);

                if (shouldOverride.HasValue) {
                    if (shouldOverride.Value) {
                        npc.ModNPC?.ModifyIncomingHit(ref modifiers);
                        return;
                    }
                    else {
                        return;
                    }
                }
            }

            orig.Invoke(npc, ref modifiers);
        }

        public static void OnFindFrameHook(On_FindFrameDelegate orig, NPC npc, int frameHeight) {
            if (npc.type == NPCID.None || !npc.active) {
                orig.Invoke(npc, frameHeight);
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool reset = true;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.FindFrame(frameHeight)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(npc, frameHeight);
        }
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
