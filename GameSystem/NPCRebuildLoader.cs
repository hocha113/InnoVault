using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
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
        public delegate void On_OnHitByItemDelegate(NPC npc, Player player, Item item, in NPC.HitInfo hit, int damageDone);
        public delegate void On_OnHitByProjectileDelegate(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone);
        public delegate void On_ModifyIncomingHitDelegate(NPC npc, ref NPC.HitModifiers modifiers);
        public delegate void On_FindFrameDelegate(NPC npc, int frameHeight);
        public delegate void On_SetChatButtonsDelegate(ref string button, ref string button2);
        public delegate void On_NPCSetDefaultDelegate();
        public delegate bool DelegateOn_OnHitByItem(Player player, Item item, in NPC.HitInfo hit, int damageDone);
        public delegate bool? DelegateOn_OnHitByProjectile(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone);
        public delegate void DelegateModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers);
        public delegate void DelegateModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers);
        public delegate bool? DelegateCanBeHitByNPC(NPC attacker);
        public static event On_NPCDelegate PreSetDefaultsEvent;
        public static event On_NPCDelegate PostSetDefaultsEvent;
        public static Type npcLoaderType;
        public static MethodInfo onHitByItem_Method;
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
        private static readonly List<VaultHookMethodCache<NPCOverride>> hooks = [];
        internal static VaultHookMethodCache<NPCOverride> HookAI;
        internal static VaultHookMethodCache<NPCOverride> HookPostAI;
        internal static VaultHookMethodCache<NPCOverride> HookOn_PreKill;
        internal static VaultHookMethodCache<NPCOverride> HookCheckActive;
        internal static VaultHookMethodCache<NPCOverride> HookCheckDead;
        internal static VaultHookMethodCache<NPCOverride> HookDraw;
        internal static VaultHookMethodCache<NPCOverride> HookPostDraw;
        internal static VaultHookMethodCache<NPCOverride> HookFindFrame;
        internal static VaultHookMethodCache<NPCOverride> HookModifyNPCLoot;
        internal static VaultHookMethodCache<NPCOverride> HookOnHitByItem;
        internal static VaultHookMethodCache<NPCOverride> HookOnHitByProjectile;
        internal static VaultHookMethodCache<NPCOverride> HookModifyHitByItem;
        internal static VaultHookMethodCache<NPCOverride> HookModifyHitByProjectile;
        internal static VaultHookMethodCache<NPCOverride> HookCanBeHitByItem;
        internal static VaultHookMethodCache<NPCOverride> HookCanBeHitByNPC;
        internal static VaultHookMethodCache<NPCOverride> HookCanBeHitByProjectile;
        public Dictionary<Type, NPCOverride> NPCOverrides { get; internal set; }
        public List<NPCOverride> AIOverrides { get; private set; }
        public List<NPCOverride> PostAIOverrides { get; private set; }
        public List<NPCOverride> On_PreKillOverrides { get; private set; }
        public List<NPCOverride> CheckActiveOverrides { get; private set; }
        public List<NPCOverride> CheckDeadOverrides { get; private set; }
        public List<NPCOverride> DrawOverrides { get; private set; }
        public List<NPCOverride> PostDrawOverrides { get; private set; }
        public List<NPCOverride> FindFrameOverrides { get; private set; }
        public List<NPCOverride> ModifyNPCLootOverrides { get; private set; }
        public List<NPCOverride> OnHitByItemOverrides { get; private set; }
        public List<NPCOverride> OnHitByProjectileOverrides { get; private set; }
        public List<NPCOverride> ModifyHitByItemOverrides { get; private set; }
        public List<NPCOverride> ModifyHitByProjectileOverrides { get; private set; }
        public List<NPCOverride> CanBeHitByItemOverrides { get; private set; }
        public List<NPCOverride> CanBeHitByNPCOverrides { get; private set; }
        public List<NPCOverride> CanBeHitByProjectileOverrides { get; private set; }
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

        void IVaultLoader.SetupData() {
            HookAI = AddHook<Func<bool>>(n => n.AI);
            HookPostAI = AddHook<Action>(n => n.PostAI);
            HookOn_PreKill = AddHook<Func<bool?>>(n => n.On_PreKill);
            HookCheckActive = AddHook<Func<bool>>(n => n.CheckActive);
            HookCheckDead = AddHook<Func<bool?>>(n => n.CheckDead);
            HookDraw = AddHook<Func<SpriteBatch, Vector2, Color, bool?>>(n => n.Draw);
            HookPostDraw = AddHook<Func<SpriteBatch, Vector2, Color, bool>>(n => n.PostDraw);
            HookFindFrame = AddHook<Func<int, bool>>(n => n.FindFrame);
            HookModifyNPCLoot = AddHook<Action<NPC, NPCLoot>>(n => n.ModifyNPCLoot);
            HookOnHitByItem = AddHook<DelegateOn_OnHitByItem>(n => n.On_OnHitByItem);
            HookOnHitByProjectile = AddHook<DelegateOn_OnHitByProjectile>(n => n.On_OnHitByProjectile);
            HookModifyHitByItem = AddHook<DelegateModifyHitByItem>(n => n.ModifyHitByItem);
            HookModifyHitByProjectile = AddHook<DelegateModifyHitByProjectile>(n => n.ModifyHitByProjectile);
            HookCanBeHitByItem = AddHook<Func<Player, Item, bool?>>(n => n.CanBeHitByItem);
            HookCanBeHitByNPC = AddHook<DelegateCanBeHitByNPC>(n => n.CanBeHitByNPC);
            HookCanBeHitByProjectile = AddHook<Func<Projectile, bool?>>(n => n.CanBeHitByProjectile);
        }

        void IVaultLoader.UnLoadData() {
            Instances?.Clear();
            OverrideIDToInstances?.Clear();
            TypeToOverrideID?.Clear();
            OverrideIDToType?.Clear();
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
            hooks.Clear();
            HookAI = null;
            HookPostAI = null;
            HookOn_PreKill = null;
            HookCheckActive = null;
            HookCheckDead = null;
            HookDraw = null;
            HookPostDraw = null;
            HookFindFrame = null;
            HookModifyNPCLoot = null;
            HookOnHitByItem = null;
            HookOnHitByProjectile = null;
            HookModifyHitByItem = null;
            HookModifyHitByProjectile = null;
            HookCanBeHitByItem = null;
            HookCanBeHitByNPC = null;
            HookCanBeHitByProjectile = null;
            VaultTypeRegistry<NPCOverride>.ClearRegisteredVaults();
            VaultType<NPCOverride>.TypeToMod.Clear();
        }

        private static VaultHookMethodCache<NPCOverride> AddHook<F>(Expression<Func<NPCOverride, F>> func) where F : Delegate {
            VaultHookMethodCache<NPCOverride> hook = VaultHookMethodCache<NPCOverride>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        public override GlobalNPC Clone(NPC from, NPC to) {
            NPCRebuildLoader rebuildLoader = (NPCRebuildLoader)base.Clone(from, to);
            //克隆时确保新的GlobalNPC实例拥有自己独立的列表集合
            rebuildLoader.AIOverrides = [.. AIOverrides];
            rebuildLoader.PostAIOverrides = [.. PostAIOverrides];
            rebuildLoader.On_PreKillOverrides = [.. On_PreKillOverrides];
            rebuildLoader.CheckActiveOverrides = [.. CheckActiveOverrides];
            rebuildLoader.CheckDeadOverrides = [.. CheckDeadOverrides];
            rebuildLoader.DrawOverrides = [.. DrawOverrides];
            rebuildLoader.PostDrawOverrides = [.. PostDrawOverrides];
            rebuildLoader.FindFrameOverrides = [.. FindFrameOverrides];
            rebuildLoader.ModifyNPCLootOverrides = [.. ModifyNPCLootOverrides];
            rebuildLoader.OnHitByItemOverrides = [.. OnHitByItemOverrides];
            rebuildLoader.OnHitByProjectileOverrides = [.. OnHitByProjectileOverrides];
            rebuildLoader.ModifyHitByItemOverrides = [.. ModifyHitByItemOverrides];
            rebuildLoader.ModifyHitByProjectileOverrides = [.. ModifyHitByProjectileOverrides];
            rebuildLoader.CanBeHitByItemOverrides = [.. CanBeHitByItemOverrides];
            rebuildLoader.CanBeHitByNPCOverrides = [.. CanBeHitByNPCOverrides];
            rebuildLoader.CanBeHitByProjectileOverrides = [.. CanBeHitByProjectileOverrides];
            rebuildLoader.NPCOverrides = NPCOverrides;
            return rebuildLoader;
        }

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => lateInstantiation && ByID.ContainsKey(entity.type);

        public static void UniversalForEach(NPC npc, Action<NPCOverride> action) {
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                action(inds);
            }
        }

        public static bool UniversalForEach(NPC npc, Func<NPCOverride, bool> action, bool startBool = true) {
            bool result = startBool;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                bool newResult = action(inds);
                if (newResult != startBool) {
                    result = newResult;
                }
            }
            return result;
        }

        public static bool? UniversalForEach(NPC npc, Func<NPCOverride, bool?> action) {
            bool? result = null;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                bool? newResult = action(inds);
                if (newResult.HasValue) {
                    result = newResult;
                }
            }
            return result;
        }

        public void InitializeNPC() {
            //当GlobalNPC实例被创建时，初始化它的列表字段
            AIOverrides = [];
            PostAIOverrides = [];
            On_PreKillOverrides = [];
            CheckActiveOverrides = [];
            CheckDeadOverrides = [];
            DrawOverrides = [];
            PostDrawOverrides = [];
            FindFrameOverrides = [];
            ModifyNPCLootOverrides = [];
            OnHitByItemOverrides = [];
            OnHitByProjectileOverrides = [];
            ModifyHitByItemOverrides = [];
            ModifyHitByProjectileOverrides = [];
            CanBeHitByItemOverrides = [];
            CanBeHitByNPCOverrides = [];
            CanBeHitByProjectileOverrides = [];
        }

        public override void SetDefaults(NPC npc) {
            if (npc.Alives()) {
                PreSetDefaultsEvent?.Invoke(npc);
            }
            InitializeNPC();
            NPCOverride.SetDefaults(npc);
            if (npc.Alives()) {
                PostSetDefaultsEvent?.Invoke(npc);
            }
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers) {
            foreach (var value in ModifyHitByItemOverrides) {
                value.ModifyHitByItem(player, item, ref modifiers);
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers) {
            foreach (var value in ModifyHitByProjectileOverrides) {
                value.ModifyHitByProjectile(projectile, ref modifiers);
            }
        }

        public override bool CheckActive(NPC npc) {
            bool result = true;
            foreach (var value in CheckActiveOverrides) {
                if (!value.CheckActive()) {
                    result = false;
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
            foreach (var value in Instances) {
                if (value.TargetID == -1 || value.TargetID == npc.type) {
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
            bool? reset = null;
            foreach (var value in CanBeHitByItemOverrides) {
                bool? newReset = value.CanBeHitByItem(player, item);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
            }
            if (reset.HasValue) {
                return reset.Value;
            }
            return null;
        }

        public override bool CanBeHitByNPC(NPC npc, NPC attacker) {
            bool? reset = null;
            foreach (var value in CanBeHitByNPCOverrides) {
                bool? newReset = value.CanBeHitByNPC(attacker);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
            }
            if (reset.HasValue) {
                return reset.Value;
            }
            return true;
        }

        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) {
            bool? reset = null;
            foreach (var value in CanBeHitByProjectileOverrides) {
                bool? newReset = value.CanBeHitByProjectile(projectile);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
            }
            if (reset.HasValue) {
                return reset.Value;
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
                onHitByItem_Method = GetMethodInfo("OnHitByItem");
                if (onHitByItem_Method != null) {
                    VaultHook.Add(onHitByItem_Method, On_OnHitByItemHook);
                }
                else {
                    DompLog("onHitByItem_Method");
                }
            }
            {
                onHitByProjectile_Method = GetMethodInfo("OnHitByProjectile");
                if (onHitByProjectile_Method != null) {
                    VaultHook.Add(onHitByProjectile_Method, On_OnHitByProjectileHook);
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

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool? result = null;
                foreach (var value in gNpc.On_PreKillOverrides) {
                    bool? newResult = value.On_PreKill();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    NPCLoader.blockLoot?.Clear();//这里因为提前返回，所以手动清理一下物品ban位
                    return result.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static bool OnCheckDeadHook(On_NPCDelegate2 orig, NPC npc) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool? result = null;
                foreach (var value in gNpc.CheckDeadOverrides) {
                    bool? newResult = value.CheckDead();
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

            if (!UniversalForEach(npc, inds => inds.AI())) {
                return;
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool result = true;
                foreach (var value in gNpc.AIOverrides) {
                    if (!value.AI()) {
                        result = false;
                    }
                }
                if (result) {
                    try {
                        orig.Invoke(npc);
                    } catch (Exception ex) {
                        LogAndDeactivateNPC(npc, ex);
                    }
                }

                foreach (var value in gNpc.PostAIOverrides) {
                    value.PostAI();
                }

                //所有逻辑处理完成后，统一做一次网络同步
                if (gNpc.NPCOverrides != null) {
                    foreach (var npcOverrideInstance in gNpc.NPCOverrides.Values) {
                        npcOverrideInstance.DoNetWork();
                    }
                }
            }
            else {
                try {
                    orig.Invoke(npc);
                } catch (Exception ex) {
                    LogAndDeactivateNPC(npc, ex);
                }
            }

            UniversalForEach(npc, inds => inds.PostAI());
        }

        public static bool OnPreDrawHook(On_DrawDelegate orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc, spriteBatch, screenPos, drawColor);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool? result = null;
                foreach (var value in gNpc.DrawOverrides) {
                    bool? newResult = value.Draw(spriteBatch, screenPos, drawColor);
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

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool reset = true;
                foreach (var value in gNpc.PostDrawOverrides) {
                    if (!value.PostDraw(spriteBatch, screenPos, drawColor)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(npc, spriteBatch, screenPos, drawColor);
        }

        public static void On_OnHitByItemHook(On_OnHitByItemDelegate orig, NPC npc, Player player, Item item, in NPC.HitInfo hit, int damageDone) {
            if (npc.TryGetGlobalNPC(out NPCRebuildLoader rebuildLoader)) {
                bool reset = true;
                foreach (var inds in rebuildLoader.OnHitByItemOverrides) {
                    if (!inds.On_OnHitByItem(player, item, hit, damageDone)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(npc, player, item, hit, damageDone);
        }

        public static void On_OnHitByProjectileHook(On_OnHitByProjectileDelegate orig, NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            if (npc.TryGetGlobalNPC(out NPCRebuildLoader rebuildLoader)) {
                foreach (var inds in rebuildLoader.OnHitByProjectileOverrides) {
                    if (!inds.DoHitByProjectileByInstance(projectile, in hit, damageDone)) {
                        return;
                    }
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

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool reset = true;
                foreach (var value in gNpc.FindFrameOverrides) {
                    if (!value.FindFrame(frameHeight)) {
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
