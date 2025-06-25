using Microsoft.Xna.Framework;
using Mono.Cecil;
using System;
using System.Collections.Generic;
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
        public static MethodInfo onProjectileAI_Method;
        public static MethodInfo onPreDraw_Method;
        public static MethodInfo onPostDraw_Method;
        public override bool InstancePerEntity => true;
        public Dictionary<Type, ProjOverride> ProjOverrides { get; internal set; }

        void IVaultLoader.LoadData() {
            onProjectileAI_Method = typeof(ProjectileLoader).GetMethod("ProjectileAI", BindingFlags.Static | BindingFlags.Public);
            VaultHook.Add(onProjectileAI_Method, OnProjectileAIHook);
            onPreDraw_Method = typeof(ProjectileLoader).GetMethod("PreDraw", BindingFlags.Static | BindingFlags.Public);
            VaultHook.Add(onPreDraw_Method, OnPreDrawHook);
            onPostDraw_Method = typeof(ProjectileLoader).GetMethod("PostDraw", BindingFlags.Static | BindingFlags.Public);
            VaultHook.Add(onPostDraw_Method, OnPostDrawHook);
        }

        void IVaultLoader.UnLoadData() {
            PreSetDefaultsEvent = null;
            PostSetDefaultsEvent = null;
            onProjectileAI_Method = null;
            onPreDraw_Method = null;
            onPostDraw_Method = null;
        }

        public override GlobalProjectile Clone(Projectile from, Projectile to) {
            ProjRebuildLoader rebuildLoader = (ProjRebuildLoader)base.Clone(from, to);
            rebuildLoader.ProjOverrides = ProjOverrides;
            return rebuildLoader;
        }

        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) => lateInstantiation && ByID.ContainsKey(entity.type);

        public override void SetDefaults(Projectile entity) {
            if (entity.Alives()) {
                PreSetDefaultsEvent?.Invoke(entity);
            }
            ProjOverride.SetDefaults(entity);
            if (entity.Alives()) {
                PostSetDefaultsEvent?.Invoke(entity);
            }
        }

        public override void OnSpawn(Projectile proj, IEntitySource source) {
            if (proj.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.OnSpawn(source);
                }
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) {
            if (proj.TryGetOverride(out var values)) {
                bool? result = null;
                foreach (var value in values.Values) {
                    result = value.ShouldUpdatePosition();
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }
            return true;
        }

        public override void OnHitNPC(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone) {
            if (proj.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.OnHitNPC(target, hit, damageDone);
                }
            }
        }

        public override void OnHitPlayer(Projectile proj, Player target, Player.HurtInfo info) {
            if (proj.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.OnHitPlayer(target, info);
                }
            }
        }

        public override void OnKill(Projectile proj, int timeLeft) {
            if (proj.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.OnKill(timeLeft);
                }
            }
        }

        public static void OnProjectileAIHook(On_Projectile_Void_Delegate orig, Projectile proj) {
            if (proj.TryGetOverride(out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    result = value.AI();
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(proj);
        }

        public static bool OnPreDrawHook(On_PreDraw_Delegate orig, Projectile proj, ref Color lightColor) {
            if (proj.TryGetOverride(out var values)) {
                bool? result = null;
                foreach (var value in values.Values) {
                    result = value.Draw(ref lightColor);
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }
            return orig.Invoke(proj, ref lightColor);
        }

        public static void OnPostDrawHook(On_PostDraw_Delegate orig, Projectile proj, Color lightColor) {
            if (proj.TryGetOverride(out var values)) {
                bool result = true;
                foreach (var value in values.Values) {
                    result = value.PostDraw(lightColor);
                }
                if (!result) {
                    return;
                }
            }
            orig.Invoke(proj, lightColor);
        }
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
    }
}
