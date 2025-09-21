using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
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
        public delegate void On_SetChatButtonsDelegate(ref string button, ref string button2);
        public delegate void On_NPCSetDefaultDelegate();
        public static event On_NPCDelegate PreSetDefaultsEvent;
        public static event On_NPCDelegate PostSetDefaultsEvent;
        public static Type npcLoaderType;
        public static MethodInfo onHitByProjectile_Method;
        public static MethodInfo modifyIncomingHit_Method;
        public static MethodInfo onFindFrame_Method;
        public static MethodInfo onSetChatButtons_Method;
        public static MethodInfo onNPCUsesPartyHat_Method;
        public static MethodInfo onUsesPartyHat_Method;
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
            UniversalInstances ??= [];
            LoaderMethodAndHook();

            On_Main.DrawNPCHeadBoss += OnDrawNPCHeadBossHook;
            On_NPC.GetBossHeadTextureIndex += OnGetBossHeadTextureIndexHook;
            On_NPC.GetBossHeadRotation += OnGetBossHeadRotationHook;
            On_NPC.GetBossHeadSpriteEffects += OnGetBossHeadSpriteEffectsHook;
        }

        void IVaultLoader.UnLoadData() {
            Instances?.Clear();
            ByID?.Clear();
            UniversalInstances?.Clear();
            PreSetDefaultsEvent = null;
            PostSetDefaultsEvent = null;
            npcLoaderType = null;
            onHitByProjectile_Method = null;
            modifyIncomingHit_Method = null;
            onFindFrame_Method = null;
            onSetChatButtons_Method = null;
            onNPCUsesPartyHat_Method = null;
            onUsesPartyHat_Method = null;
            onNPCAI_Method = null;
            onPreKill_Method = null;
            onPreDraw_Method = null;
            onPostDraw_Method = null;
            onCheckDead_Method = null;
            On_Main.DrawNPCHeadBoss -= OnDrawNPCHeadBossHook;
            On_NPC.GetBossHeadTextureIndex -= OnGetBossHeadTextureIndexHook;
            On_NPC.GetBossHeadRotation -= OnGetBossHeadRotationHook;
            On_NPC.GetBossHeadSpriteEffects -= OnGetBossHeadSpriteEffectsHook;
        }

        public override GlobalNPC Clone(NPC from, NPC to) {
            NPCRebuildLoader rebuildLoader = (NPCRebuildLoader)base.Clone(from, to);
            rebuildLoader.NPCOverrides = NPCOverrides;
            return rebuildLoader;
        }

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => lateInstantiation && ByID.ContainsKey(entity.type);

        public static void UniversalForEach(Action<NPCOverride> action) {
            foreach (var inds in UniversalInstances) {
                action(inds);
            }
        }

        public static bool? UniversalForEach(Func<NPCOverride, bool?> action) {
            bool? result = null;
            foreach (var inds in UniversalInstances) {
                bool? newResult = action(inds);
                if (newResult.HasValue) {
                    result = newResult;
                }
            }
            return result;
        }

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

        public override void BossHeadSpriteEffects(NPC npc, ref SpriteEffects spriteEffects) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.BossHeadSpriteEffects(ref spriteEffects);
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

        public override bool? CanGoToStatue(NPC npc, bool toKingStatue) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanGoToStatue(toKingStatue);
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

        public override void GetChat(NPC npc, ref string chat) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.GetChat(ref chat);
                }
            }
        }

        public override bool PreChatButtonClicked(NPC npc, bool firstButton) {
            if (npc.TryGetOverride(out var values)) {
                bool reset = true;
                foreach (var value in values.Values) {
                    if (!value.PreChatButtonClicked(firstButton)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return false;
                }
            }
            return true;
        }

        public override void OnChatButtonClicked(NPC npc, bool firstButton) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.OnChatButtonClicked(firstButton);
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

        public override void SaveData(NPC npc, TagCompound tag) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    try {
                        value.SaveData(tag);
                    } catch (Exception ex) {
                        LogAndDeactivateNPC(npc, ex);
                    }
                }
            }
        }

        public override void LoadData(NPC npc, TagCompound tag) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    try {
                        value.LoadData(tag);
                    } catch (Exception ex) {
                        LogAndDeactivateNPC(npc, ex);
                    }
                }
            }
        }

        public override bool NeedSaving(NPC npc) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    try {
                        if (value.NeedSaving()) {
                            return true;
                        }
                    } catch (Exception ex) {
                        LogAndDeactivateNPC(npc, ex);
                    }
                }
            }
            return false;
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
                onSetChatButtons_Method = GetMethodInfo("SetChatButtons");
                if (onSetChatButtons_Method != null) {
                    VaultHook.Add(onSetChatButtons_Method, OnSetChatButtonsHook);
                }
                else {
                    DompLog("onSetChatButtons_Method");
                }
            }
            {
                onNPCUsesPartyHat_Method = typeof(NPC).GetMethod("UsesPartyHat", BindingFlags.Public | BindingFlags.Instance);
                if (onNPCUsesPartyHat_Method != null) {
                    VaultHook.Add(onNPCUsesPartyHat_Method, OnPreUsesPartyHatHook);
                }
                else {
                    DompLog("onNPCUsesPartyHat_Method");
                }
            }
            {
                onUsesPartyHat_Method = GetMethodInfo("UsesPartyHat");
                if (onUsesPartyHat_Method != null) {
                    VaultHook.Add(onUsesPartyHat_Method, OnUsesPartyHatHook);
                }
                else {
                    DompLog("onUsesPartyHat_Method");
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

        internal static void LogAndDeactivateNPC(NPC npc, Exception ex) {
            if (npc == null) {
                string nullNpcMsg = "An error occurred: NPC was null and could not be processed.";
                VaultUtils.Text($"{nullNpcMsg} For detailed error information, please refer to the log file", Color.Red);
                VaultMod.Instance.Logger.Error($"{nullNpcMsg} Error: {ex}");
                return;
            }

            string npcMsg = $"An error occurred in original AI for NPC {npc.FullName}. Deactivating it.";
            VaultUtils.Text($"{npcMsg} For detailed error information, please refer to the log file", Color.Red);
            VaultMod.Instance.Logger.Error($"{npcMsg} Error: {ex}");
            npc.active = false;
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
                try {
                    orig.Invoke(npc);
                } catch (Exception ex) {
                    LogAndDeactivateNPC(npc, ex);
                }
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
            } catch (Exception ex) {
                LogAndDeactivateNPC(npc, ex);
            }
        }

        public static bool OnPreDrawHook(On_DrawDelegate orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc, spriteBatch, screenPos, drawColor);
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

            return orig.Invoke(npc, spriteBatch, screenPos, drawColor);
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
            if (npc.TryGetOverride(out var npcOverrides)) {
                foreach (var inds in npcOverrides.Values) {
                    if (!inds.DoHitByProjectileByInstance(projectile, in hit, damageDone)) {
                        return;
                    }
                }
            }

            foreach (var inds in Instances) {//临时性，后面删
                if (inds.TargetID != NPCID.None) {
                    continue;
                }

                if (!inds.DoHitByProjectileByInstance(projectile, in hit, damageDone)) {
                    return;
                }
            }

            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                if (!inds.DoHitByProjectileByInstance(projectile, in hit, damageDone)) {
                    return;
                }
            }

            orig.Invoke(npc, projectile, hit, damageDone);
        }

        public static void ModifyIncomingHitHook(On_ModifyIncomingHitDelegate orig, NPC npc, ref NPC.HitModifiers modifiers) {
            if (npc.TryGetOverride(out var npcOverrides)) {
                foreach (var inds in npcOverrides.Values) {
                    if (!inds.DoModifyIncomingHitByInstance(ref modifiers)) {
                        return;
                    }
                }
            }

            foreach (var inds in Instances) {//临时性，后面删
                if (inds.TargetID != NPCID.None) {
                    continue;
                }

                if (!inds.DoModifyIncomingHitByInstance(ref modifiers)) {
                    return;
                }
            }

            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                if (!inds.DoModifyIncomingHitByInstance(ref modifiers)) {
                    return;
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

        public static void OnSetChatButtonsHook(On_SetChatButtonsDelegate orig, ref string button, ref string button2) {
            var npc = Main.LocalPlayer.TalkNPC;
            if (npc == null) {
                orig.Invoke(ref button, ref button2);
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool reset = true;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.SetChatButtons(ref button, ref button2)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(ref button, ref button2);
        }

        public static bool OnPreUsesPartyHatHook(On_NPCDelegate2 orig, NPC npc) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;

                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.PreUsesPartyHat();
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

        public static bool OnUsesPartyHatHook(On_NPCDelegate2 orig, NPC npc) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;

                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.UsesPartyHat();
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

        public static void OnDrawNPCHeadBossHook(On_Main.orig_DrawNPCHeadBoss orig, Entity theNPC, byte alpha
            , float headScale, float rotation, SpriteEffects effects, int bossHeadId, float x, float y) {
            if (!theNPC.active || theNPC is not NPC npc) {
                orig.Invoke(theNPC, alpha, headScale, rotation, effects, bossHeadId, x, y);
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    npcOverrideInstance.ModifyDrawNPCHeadBoss(ref x, ref y, ref bossHeadId, ref alpha, ref headScale, ref rotation, ref effects);
                }

                bool reset = true;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.PreDrawNPCHeadBoss(Main.BossNPCHeadRenderer, new Vector2(x, y), bossHeadId, alpha, headScale, rotation, effects)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            if (UniversalInstances.Count > 0) {
                foreach (var inds in UniversalInstances) {
                    inds.UniversalSetNPCInstance(npc);
                    inds.ModifyDrawNPCHeadBoss(ref x, ref y, ref bossHeadId, ref alpha, ref headScale, ref rotation, ref effects);
                }

                bool universalReset = true;

                foreach (var inds in UniversalInstances) {
                    inds.UniversalSetNPCInstance(npc);
                    if (!inds.PreDrawNPCHeadBoss(Main.BossNPCHeadRenderer, new Vector2(x, y), bossHeadId, alpha, headScale, rotation, effects)) {
                        universalReset = false;
                    }
                }

                if (!universalReset) {
                    return;
                }
            }

            orig.Invoke(theNPC, alpha, headScale, rotation, effects, bossHeadId, x, y);
        }

        public static int OnGetBossHeadTextureIndexHook(On_NPC.orig_GetBossHeadTextureIndex orig, NPC npc) {
            if (!npc.active) {//不需要判定ID
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                int index = -1;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    int newIndex = npcOverrideInstance.GetBossHeadTextureIndex();
                    if (newIndex >= 0) {
                        index = newIndex;
                    }
                }
                if (index >= 0) {
                    return index;
                }
            }

            if (UniversalInstances.Count > 0) {
                int index = -1;
                foreach (var inds in UniversalInstances) {
                    inds.UniversalSetNPCInstance(npc);
                    int newIndex = inds.GetBossHeadTextureIndex();
                    if (newIndex >= 0) {
                        index = newIndex;
                    }
                }
                if (index >= 0) {
                    return index;
                }
            }

            return orig.Invoke(npc);
        }

        public static float OnGetBossHeadRotationHook(On_NPC.orig_GetBossHeadRotation orig, NPC npc) {
            if (!npc.active) {//不需要判定ID
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                float? rotation = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    float? newRotation = npcOverrideInstance.GetBossHeadRotation();
                    if (newRotation.HasValue) {
                        rotation = newRotation.Value;
                    }
                }
                if (rotation.HasValue) {
                    return rotation.Value;
                }
            }

            if (UniversalInstances.Count > 0) {
                float? rotation = null;
                foreach (var inds in UniversalInstances) {
                    float? newRotation = inds.GetBossHeadRotation();
                    if (newRotation.HasValue) {
                        rotation = newRotation.Value;
                    }
                }
                if (rotation.HasValue) {
                    return rotation.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static SpriteEffects OnGetBossHeadSpriteEffectsHook(On_NPC.orig_GetBossHeadSpriteEffects orig, NPC npc) {
            if (!npc.active) {//不需要判定ID
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                SpriteEffects? spriteEffects = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    SpriteEffects? newSpriteEffects = npcOverrideInstance.GetBossHeadSpriteEffects();
                    if (newSpriteEffects.HasValue) {
                        spriteEffects = newSpriteEffects.Value;
                    }
                }
                if (spriteEffects.HasValue) {
                    return spriteEffects.Value;
                }
            }

            if (UniversalInstances.Count > 0) {
                SpriteEffects? spriteEffects = null;
                foreach (var inds in UniversalInstances) {
                    SpriteEffects? newSpriteEffects = inds.GetBossHeadSpriteEffects();
                    if (newSpriteEffects.HasValue) {
                        spriteEffects = newSpriteEffects.Value;
                    }
                }
                if (spriteEffects.HasValue) {
                    return spriteEffects.Value;
                }
            }

            return orig.Invoke(npc);
        }
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
