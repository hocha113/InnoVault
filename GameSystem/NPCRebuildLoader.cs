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
    internal class NPCRebuildLoader : GlobalNPC, IVaultLoader
    {
        #region Data
        internal delegate void On_NPCDelegate(NPC npc);
        internal delegate bool On_NPCDelegate2(NPC npc);
        internal delegate bool On_DrawDelegate(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);
        internal delegate void On_DrawDelegate2(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);
        internal delegate void On_OnHitByProjectileDelegate(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone);
        internal delegate void On_ModifyIncomingHitDelegate(NPC npc, ref NPC.HitModifiers modifiers);
        internal delegate void On_NPCSetDefaultDelegate();
        public static Type npcLoaderType;
        public static MethodInfo onHitByProjectile_Method;
        public static MethodInfo modifyIncomingHit_Method;
        public static MethodInfo onNPCAI_Method;
        public static MethodInfo onPreKill_Method;
        public static MethodInfo onPreDraw_Method;
        public static MethodInfo onPostDraw_Method;
        public static MethodInfo onCheckDead_Method;
        public override bool InstancePerEntity => true;
        public Dictionary<Type, NPCOverride> NPCOverrides { get; set; }
        #endregion

        void IVaultLoader.LoadData() {
            npcLoaderType = typeof(NPCLoader);
            LoaderMethodAndHook();
        }

        void IVaultLoader.UnLoadData() {
            npcLoaderType = null;
            onHitByProjectile_Method = null;
            modifyIncomingHit_Method = null;
            onNPCAI_Method = null;
            onPreKill_Method = null;
            onPreDraw_Method = null;
            onPostDraw_Method = null;
            onCheckDead_Method = null;
        }

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) {
            return lateInstantiation && ByID.ContainsKey(entity.type);
        }

        public override void SetDefaults(NPC npc) => NPCOverride.SetDefaults(npc);

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
                    result = value.CheckActive();
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

        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot) {
            //不要用TryFetchByID或者直接访问NPCOverride
            if (ByID.TryGetValue(npc.type, out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyNPCLoot(npc, npcLoot);
                }
            }
        }

        private static MethodInfo GetMethodInfo(string key) => npcLoaderType.GetMethod(key, BindingFlags.Public | BindingFlags.Static);

        private static void DompLog(string name) => VaultMod.Instance.Logger.Info($"ERROR:Fail To Load! {name} Is Null!");

        private void LoaderMethodAndHook() {
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
                    result = npcOverrideInstance.On_PreKill();
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
                    result = npcOverrideInstance.CheckDead();
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
                    result = npcOverrideInstance.AI();
                    npcOverrideInstance.DoNetWork();
                }
                npc.type = type;
                if (!result) {
                    return;
                }
            }

            orig.Invoke(npc);
        }

        public static bool OnPreDrawHook(On_DrawDelegate orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (npc.type == NPCID.None || !npc.active) {
                return false;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    result = npcOverrideInstance.Draw(spriteBatch, screenPos, drawColor);
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
                    result = npcOverrideInstance.PostDraw(spriteBatch, screenPos, drawColor);
                }
                if (!result) {
                    return;
                }
            }

            orig.Invoke(npc, spriteBatch, screenPos, drawColor);
        }

        public void OnHitByProjectileHook(On_OnHitByProjectileDelegate orig, NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
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

        public void ModifyIncomingHitHook(On_ModifyIncomingHitDelegate orig, NPC npc, ref NPC.HitModifiers modifiers) {
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
    }
}
