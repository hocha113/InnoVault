using Microsoft.Xna.Framework;
using System;
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
        void IVaultLoader.LoadData() {
            On_Dust.NewDust += OnNewDustHook;
            On_Dust.NewDustDirect += OnNewDustDirectHook;
            On_Dust.NewDustPerfect += OnNewDustPerfectHook;
            On_Dust.UpdateDust += OnUpdateDustHook;
            VaultHook.Add(typeof(Main).GetMethod("DrawDust", BindingFlags.NonPublic | BindingFlags.Instance), OnDrawDustHook);
        }

        void IVaultLoader.UnLoadData() {
            On_Dust.NewDust -= OnNewDustHook;
            On_Dust.NewDustDirect -= OnNewDustDirectHook;
            On_Dust.NewDustPerfect -= OnNewDustPerfectHook;
            On_Dust.UpdateDust -= OnUpdateDustHook;
            Instance.Clear();
        }

        private static int OnNewDustHook(On_Dust.orig_NewDust orig, Vector2 Position, int Width, int Height
            , int Type, float SpeedX, float SpeedY, int Alpha, Color newColor, float Scale) {
            int dustIndex = orig(Position, Width, Height, Type, SpeedX, SpeedY, Alpha, newColor, Scale);
            foreach (var globalDust in  Instance) {
                globalDust.OnSpawn(Main.dust[dustIndex]);
            }
            return dustIndex;
        }

        private static Dust OnNewDustDirectHook(On_Dust.orig_NewDustDirect orig, Vector2 Position, int Width, int Height
            , int Type, float SpeedX, float SpeedY, int Alpha, Color newColor, float Scale) {
            Dust dust = orig(Position, Width, Height, Type, SpeedX, SpeedY, Alpha, newColor, Scale);
            foreach (var globalDust in Instance) {
                globalDust.OnSpawn(dust);
            }
            return dust;
        }

        private Dust OnNewDustPerfectHook(On_Dust.orig_NewDustPerfect orig, Vector2 Position, int Type, Vector2? Velocity, int Alpha, Color newColor, float Scale) {
            Dust dust = orig(Position, Type, Velocity, Alpha, newColor, Scale);
            foreach (var globalDust in Instance) {
                globalDust.OnSpawn(dust);
            }
            return dust;
        }

        private static void OnUpdateDustHook(On_Dust.orig_UpdateDust orig) {
            bool reset = true;
            foreach (var globalDust in Instance) {
                if (!globalDust.PreUpdateDusts()) {
                    reset = false;
                }
            }

            if (reset) {
                orig.Invoke();
            }

            foreach (var globalDust in Instance) {
                globalDust.PostUpdateDusts();
            }
        }

        private static void OnDrawDustHook(Action<Main> orig, Main main) {
            bool reset = true;
            foreach (var globalDust in Instance) {
                if (!globalDust.PreDraws()) {
                    reset = false;
                }
            }

            if (reset) {
                orig.Invoke(main);
            }

            foreach (var globalDust in Instance) {
                globalDust.PostDraws();
            }
        }
    }
}
