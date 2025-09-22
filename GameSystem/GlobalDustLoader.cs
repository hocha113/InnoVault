using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Terraria;
using static InnoVault.GameSystem.GlobalDust;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 所有的<see cref="GlobalDust"/>钩子均挂载于此
    /// </summary>
    public class GlobalDustLoader : IVaultLoader
    {
        private static readonly List<VaultHookMethodCache<GlobalDust>> hooks = [];
        internal static VaultHookMethodCache<GlobalDust> HookOnSpawn;
        internal static VaultHookMethodCache<GlobalDust> HookPreUpdateDustAll;
        internal static VaultHookMethodCache<GlobalDust> HookPostUpdateDustAll;
        internal static VaultHookMethodCache<GlobalDust> HookPreDrawAll;
        internal static VaultHookMethodCache<GlobalDust> HookPostDrawAll;
        void IVaultLoader.LoadData() {
            On_Dust.NewDust += OnNewDustHook;
            On_Dust.NewDustDirect += OnNewDustDirectHook;
            On_Dust.NewDustPerfect += OnNewDustPerfectHook;
            On_Dust.UpdateDust += OnUpdateDustHook;
            VaultHook.Add(typeof(Main).GetMethod("DrawDust", BindingFlags.NonPublic | BindingFlags.Instance), OnDrawDustHook);
        }

        void IVaultLoader.SetupData() {
            HookOnSpawn = AddHook<Action<Dust>>(d => d.OnSpawn);
            HookPreUpdateDustAll = AddHook<Func<bool>>(d => d.PreUpdateDustAll);
            HookPostUpdateDustAll = AddHook<Action>(d => d.PostUpdateDustAll);
            HookPreDrawAll = AddHook<Func<bool>>(d => d.PreDrawAll);
            HookPostDrawAll = AddHook<Action>(d => d.PostDrawAll);
        }

        void IVaultLoader.UnLoadData() {
            On_Dust.NewDust -= OnNewDustHook;
            On_Dust.NewDustDirect -= OnNewDustDirectHook;
            On_Dust.NewDustPerfect -= OnNewDustPerfectHook;
            On_Dust.UpdateDust -= OnUpdateDustHook;
            Instance.Clear();
        }

        private static VaultHookMethodCache<GlobalDust> AddHook<F>(Expression<Func<GlobalDust, F>> func) where F : Delegate {
            VaultHookMethodCache<GlobalDust> hook = VaultHookMethodCache<GlobalDust>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        private static int OnNewDustHook(On_Dust.orig_NewDust orig, Vector2 Position, int Width, int Height
            , int Type, float SpeedX, float SpeedY, int Alpha, Color newColor, float Scale) {
            int dustIndex = orig(Position, Width, Height, Type, SpeedX, SpeedY, Alpha, newColor, Scale);
            foreach (var globalDust in HookOnSpawn.Enumerate()) {
                globalDust.OnSpawn(Main.dust[dustIndex]);
            }
            return dustIndex;
        }

        private static Dust OnNewDustDirectHook(On_Dust.orig_NewDustDirect orig, Vector2 Position, int Width, int Height
            , int Type, float SpeedX, float SpeedY, int Alpha, Color newColor, float Scale) {
            Dust dust = orig(Position, Width, Height, Type, SpeedX, SpeedY, Alpha, newColor, Scale);
            foreach (var globalDust in HookOnSpawn.Enumerate()) {
                globalDust.OnSpawn(dust);
            }
            return dust;
        }

        private Dust OnNewDustPerfectHook(On_Dust.orig_NewDustPerfect orig, Vector2 Position, int Type, Vector2? Velocity, int Alpha, Color newColor, float Scale) {
            Dust dust = orig(Position, Type, Velocity, Alpha, newColor, Scale);
            foreach (var globalDust in HookOnSpawn.Enumerate()) {
                globalDust.OnSpawn(dust);
            }
            return dust;
        }

        private static void OnUpdateDustHook(On_Dust.orig_UpdateDust orig) {
            bool reset = true;
            foreach (var globalDust in HookPreUpdateDustAll.Enumerate()) {
                if (!globalDust.PreUpdateDustAll()) {
                    reset = false;
                }
            }

            if (reset) {
                orig.Invoke();
            }

            foreach (var globalDust in HookPostUpdateDustAll.Enumerate()) {
                globalDust.PostUpdateDustAll();
            }
        }

        private static void OnDrawDustHook(Action<Main> orig, Main main) {
            bool reset = true;
            foreach (var globalDust in HookPreDrawAll.Enumerate()) {
                if (!globalDust.PreDrawAll()) {
                    reset = false;
                }
            }

            if (reset) {
                orig.Invoke(main);
            }

            foreach (var globalDust in HookPostDrawAll.Enumerate()) {
                globalDust.PostDrawAll();
            }
        }
    }
}
