using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using static InnoVault.GameSystem.ProjOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 所有关于弹幕行为覆盖和性质加载的钩子在此处挂载
    /// </summary>
    public class ProjRebuildLoader : GlobalProjectile, IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_Projectile_Void_Delegate(Projectile proj);
        public delegate bool On_PreDraw_Delegate(Projectile projectile, ref Color lightColor);
        public delegate void On_PostDraw_Delegate(Projectile projectile, Color lightColor);
        public static event On_Projectile_Void_Delegate PreSetDefaultsEvent;
        public static event On_Projectile_Void_Delegate PostSetDefaultsEvent;
        private delegate bool? DelegateDraw(ref Color drawColor);
        public static MethodInfo onProjectileAI_Method;
        public static MethodInfo onPreDraw_Method;
        public static MethodInfo onPostDraw_Method;
        public override bool InstancePerEntity => true;
        private static readonly List<VaultHookList<ProjOverride>> hooks = [];
        internal static VaultHookList<ProjOverride> HookAI;
        internal static VaultHookList<ProjOverride> HookPostAI;
        internal static VaultHookList<ProjOverride> HookOnSpawn;
        internal static VaultHookList<ProjOverride> HookShouldUpdatePosition;
        internal static VaultHookList<ProjOverride> HookOnHitNPC;
        internal static VaultHookList<ProjOverride> HookOnHitPlayer;
        internal static VaultHookList<ProjOverride> HookOnKill;
        internal static VaultHookList<ProjOverride> HookDraw;
        internal static VaultHookList<ProjOverride> HookPostDraw;
        public Dictionary<Type, ProjOverride> ProjOverrides { get; internal set; }
        //这些列表属于每个ProjRebuildLoader的实例(即每个弹幕)，只存储对当前弹幕生效的、且重写了对应方法的ProjOverride实例
        public List<ProjOverride> AIOverrides { get; private set; }
        public List<ProjOverride> PostAIOverrides { get; private set; }
        public List<ProjOverride> OnSpawnOverrides { get; private set; }
        public List<ProjOverride> ShouldUpdatePositionOverrides { get; private set; }
        public List<ProjOverride> OnHitNPCOverrides { get; private set; }
        public List<ProjOverride> OnHitPlayerOverrides { get; private set; }
        public List<ProjOverride> OnKillOverrides { get; private set; }
        public List<ProjOverride> DrawOverrides { get; private set; }
        public List<ProjOverride> PostDrawOverrides { get; private set; }

        public void InitializeList() {
            AIOverrides = [];
            PostAIOverrides = [];
            OnSpawnOverrides = [];
            ShouldUpdatePositionOverrides = [];
            OnHitNPCOverrides = [];
            OnHitPlayerOverrides = [];
            OnKillOverrides = [];
            DrawOverrides = [];
            PostDrawOverrides = [];
        }

        void IVaultLoader.LoadData() {
            onProjectileAI_Method = typeof(ProjectileLoader).GetMethod("ProjectileAI", BindingFlags.Static | BindingFlags.Public);
            VaultHook.Add(onProjectileAI_Method, OnProjectileAIHook);
            onPreDraw_Method = typeof(ProjectileLoader).GetMethod("PreDraw", BindingFlags.Static | BindingFlags.Public);
            VaultHook.Add(onPreDraw_Method, OnPreDrawHook);
            onPostDraw_Method = typeof(ProjectileLoader).GetMethod("PostDraw", BindingFlags.Static | BindingFlags.Public);
            VaultHook.Add(onPostDraw_Method, OnPostDrawHook);
        }

        void IVaultLoader.SetupData() {
            HookAI = AddHook<Func<bool>>(p => p.AI);
            HookPostAI = AddHook<Action>(p => p.PostAI);
            HookOnSpawn = AddHook<Action<IEntitySource>>(p => p.OnSpawn);
            HookShouldUpdatePosition = AddHook<Func<bool?>>(p => p.ShouldUpdatePosition);
            HookOnHitNPC = AddHook<Action<NPC, NPC.HitInfo, int>>(p => p.OnHitNPC);
            HookOnHitPlayer = AddHook<Action<Player, Player.HurtInfo>>(p => p.OnHitPlayer);
            HookOnKill = AddHook<Action<int>>(p => p.OnKill);
            HookDraw = AddHook<DelegateDraw>(p => p.Draw);
            HookPostDraw = AddHook<Func<Color, bool>>(p => p.PostDraw);
        }

        void IVaultLoader.UnLoadData() {
            PreSetDefaultsEvent = null;
            PostSetDefaultsEvent = null;
            onProjectileAI_Method = null;
            onPreDraw_Method = null;
            onPostDraw_Method = null;
        }

        private static VaultHookList<ProjOverride> AddHook<F>(Expression<Func<ProjOverride, F>> func) where F : Delegate {
            VaultHookList<ProjOverride> hook = VaultHookList<ProjOverride>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        public override GlobalProjectile Clone(Projectile from, Projectile to) {
            ProjRebuildLoader rebuildLoader = (ProjRebuildLoader)base.Clone(from, to);
            rebuildLoader.ProjOverrides = ProjOverrides;
            return rebuildLoader;
        }

        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) => lateInstantiation && ByID.ContainsKey(entity.type);

        public static void UniversalForEach(Projectile projectile, Action<ProjOverride> action) {
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetProjInstance(projectile);
                action(inds);
            }
        }

        public static bool UniversalForEach(Projectile projectile, Func<ProjOverride, bool> action, bool startBool = true) {
            bool result = startBool;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetProjInstance(projectile);
                bool newResult = action(inds);
                if (newResult != startBool) {
                    result = newResult;
                }
            }
            return result;
        }

        public static bool? UniversalForEach(Projectile projectile, Func<ProjOverride, bool?> action) {
            bool? result = null;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetProjInstance(projectile);
                bool? newResult = action(inds);
                if (newResult.HasValue) {
                    result = newResult;
                }
            }
            return result;
        }

        public override void SetDefaults(Projectile entity) {
            if (entity.Alives()) {
                PreSetDefaultsEvent?.Invoke(entity);
            }
            InitializeList();
            ProjOverride.SetDefaults(entity);
            if (entity.Alives()) {
                PostSetDefaultsEvent?.Invoke(entity);
            }
        }

        public override void OnSpawn(Projectile proj, IEntitySource source) {
            foreach (var value in OnSpawnOverrides) {
                value.OnSpawn(source);
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) {
            bool? result = null;
            foreach (var value in ShouldUpdatePositionOverrides) {
                result = value.ShouldUpdatePosition();
            }
            if (result.HasValue) {
                return result.Value;
            }
            return true;
        }

        public override void OnHitNPC(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone) {
            foreach (var value in OnHitNPCOverrides) {
                value.OnHitNPC(target, hit, damageDone);
            }
        }

        public override void OnHitPlayer(Projectile proj, Player target, Player.HurtInfo info) {
            foreach (var value in OnHitPlayerOverrides) {
                value.OnHitPlayer(target, info);
            }
        }

        public override void OnKill(Projectile proj, int timeLeft) {
            foreach (var value in OnKillOverrides) {
                value.OnKill(timeLeft);
            }
        }

        public static void OnProjectileAIHook(On_Projectile_Void_Delegate orig, Projectile proj) {
            if (!UniversalForEach(proj, inds => inds.AI())) {
                return;
            }

            if (proj.TryGetGlobalProjectile(out ProjRebuildLoader gProj)) {
                bool result = true;
                foreach (var value in gProj.AIOverrides) {
                    if (!value.AI()) {
                        result = false;
                    }
                }
                if (!result) {
                    return;
                }
            }

            orig.Invoke(proj);

            if (gProj != null) {
                foreach (var value in gProj.PostAIOverrides) {
                    value.PostAI();
                }
            }

            UniversalForEach(proj, inds => inds.PostAI());
        }

        public static bool OnPreDrawHook(On_PreDraw_Delegate orig, Projectile proj, ref Color lightColor) {
            if (proj.TryGetGlobalProjectile(out ProjRebuildLoader gProj)) {
                bool? result = null;
                foreach (var value in gProj.DrawOverrides) {
                    bool? newResult = value.Draw(ref lightColor);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }
            return orig.Invoke(proj, ref lightColor);
        }

        public static void OnPostDrawHook(On_PostDraw_Delegate orig, Projectile proj, Color lightColor) {
            orig.Invoke(proj, lightColor);

            if (proj.TryGetGlobalProjectile(out ProjRebuildLoader gProj)) {
                foreach (var value in gProj.PostDrawOverrides) {
                    value.PostDraw(lightColor);
                }
            }
        }
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
