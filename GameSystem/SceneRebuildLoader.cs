using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于场景重制节点的钩子均挂载于此处
    /// </summary>
    public class SceneRebuildLoader : IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_UpdateAudio_Dlelgate(Main main);
        public delegate bool On_IsSceneEffectActive_Dlelgate(ModSceneEffect modSceneEffect, Player player);
        public static List<IUpdateAudio> UpdateAudios { get; internal set; } = [];
        internal static readonly HashSet<string> ActiveSceneEffects = [];
        private static readonly List<VaultHookMethodCache<SceneOverride>> hooks = [];
        internal static VaultHookMethodCache<SceneOverride> HookDecideMusic;
        internal static VaultHookMethodCache<SceneOverride> HookPostUpdateAudio;
        internal static VaultHookMethodCache<SceneOverride> HookPreIsSceneEffectActive;
        internal static VaultHookMethodCache<SceneOverride> HookPostIsSceneEffectActive;
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
        /// <summary>
        /// 存储声音接口内报错的时间戳
        /// </summary>
        private static readonly Dictionary<object, DateTime> lastErrorByKey = [];

        void IVaultLoader.LoadData() {
            foreach (var sceneOverride in VaultUtils.GetDerivedInstances<SceneOverride>()) {
                VaultTypeRegistry<SceneOverride>.Register(sceneOverride);//这里提取手动加载好所有的SceneOverride实例
                foreach (var name in sceneOverride.GetActiveSceneEffectFullNames()) {
                    ActiveSceneEffects.Add(name);
                }
            }
            VaultTypeRegistry<SceneOverride>.CompleteLoading();

            foreach (var playerOverride in VaultUtils.GetDerivedInstances<PlayerOverride>()) {
                foreach (var name in playerOverride.GetActiveSceneEffectFullNames()) {
                    ActiveSceneEffects.Add(name);
                }
            }

            HookDecideMusic = AddHook<Action>(scene => scene.DecideMusic);
            HookPostUpdateAudio = AddHook<Action>(scene => scene.PostUpdateAudio);
            HookPreIsSceneEffectActive = AddHook<Func<ModSceneEffect, Player, bool?>>(scene => scene.PreIsSceneEffectActive);
            HookPostIsSceneEffectActive = AddHook<Action<ModSceneEffect, Player>>(scene => scene.PostIsSceneEffectActive);

            On_Main.UpdateAudio_DecideOnTOWMusic += DecideOnTOWMusicEvent;
            On_Main.UpdateAudio_DecideOnNewMusic += DecideOnNewMusicEvent;         

            UpdateAudios = VaultUtils.GetDerivedInstances<IUpdateAudio>();
            VaultHook.Add(typeof(Main).GetMethod("UpdateAudio", BindingFlags.Instance | BindingFlags.NonPublic), OnUpdateAudioHook);

            foreach (var sceneEffect in VaultUtils.GetDerivedTypes<ModSceneEffect>()) {
                if (!ActiveSceneEffects.Contains(sceneEffect.FullName)) {
                    continue;//如果不包含则跳过挂载钩子，节省性能
                }
                VaultHook.Add(sceneEffect.GetMethod("IsSceneEffectActive", BindingFlags.Instance | BindingFlags.Public), OnIsSceneEffectActiveHook);
            }
        }

        void IVaultLoader.UnLoadData() {
            On_Main.UpdateAudio_DecideOnTOWMusic -= DecideOnTOWMusicEvent;
            On_Main.UpdateAudio_DecideOnNewMusic -= DecideOnNewMusicEvent;
            UpdateAudios?.Clear();
            lastErrorByKey?.Clear();
        }

        private static VaultHookMethodCache<SceneOverride> AddHook<F>(Expression<Func<SceneOverride, F>> func) where F : Delegate {
            VaultHookMethodCache<SceneOverride> hook = VaultHookMethodCache<SceneOverride>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        private void DecideOnTOWMusicEvent(On_Main.orig_UpdateAudio_DecideOnTOWMusic orig, Main self) {
            orig.Invoke(self);
            ProcessSceneActions(audio => audio.DecideMusic(), inds => inds.DecideMusic());
        }

        private void DecideOnNewMusicEvent(On_Main.orig_UpdateAudio_DecideOnNewMusic orig, Main self) {
            orig.Invoke(self);
            ProcessSceneActions(audio => audio.DecideMusic(), inds => inds.DecideMusic());
        }

        private static void OnUpdateAudioHook(On_UpdateAudio_Dlelgate orig, Main main) {
            orig.Invoke(main);
            ProcessSceneActions(audio => audio.PostUpdateAudio(), inds => inds.PostUpdateAudio());
        }

        private static bool OnIsSceneEffectActiveHook(On_IsSceneEffectActive_Dlelgate orig, ModSceneEffect modSceneEffect, Player player) {
            if (!ActiveSceneEffects.Contains(modSceneEffect.FullName)) {
                return orig.Invoke(modSceneEffect, player);//不包含则直接返回原逻辑
            }

            bool? result = null;

            foreach (var scene in HookPreIsSceneEffectActive.Enumerate()) {
                HandleSceneAction(scene, () => {
                    bool? newResult = scene.PreIsSceneEffectActive(modSceneEffect, player);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                });
            }

            if (result.HasValue) {
                return result.Value;
            }

            foreach (var playerOverride in PlayerRebuildLoader.HookPreIsSceneEffectActiveByPlayer.Enumerate()) {
                HandleSceneAction(playerOverride, () => {
                    playerOverride.Player = player;
                    if (!playerOverride.CanOverride()) {
                        return;
                    }
                    bool? newResult = playerOverride.PreIsSceneEffectActive(modSceneEffect);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                });
            }

            if (result.HasValue) {
                return result.Value;
            }

            result = orig.Invoke(modSceneEffect, player);

            foreach (var playerOverride in PlayerRebuildLoader.HookPostIsSceneEffectActiveByPlayer.Enumerate()) {
                HandleSceneAction(playerOverride, () => {
                    playerOverride.Player = player;
                    if (!playerOverride.CanOverride()) {
                        return;
                    }
                    playerOverride.PostIsSceneEffectActive(modSceneEffect);
                });
            }

            foreach (var scene in HookPostIsSceneEffectActive.Enumerate()) {
                HandleSceneAction(scene, () => scene.PostIsSceneEffectActive(modSceneEffect, player));
            }

            return result.Value;
        }

        /// <summary>
        /// 统一处理所有场景对象的行为，并包含错误处理逻辑
        /// </summary>
        /// <param name="audioAction">对 IUpdateAudio 实例执行的操作</param>
        /// <param name="instanceAction">对 SceneOverride 实例执行的操作</param>
        private static void ProcessSceneActions(Action<IUpdateAudio> audioAction, Action<SceneOverride> instanceAction) {
            foreach (var audio in UpdateAudios) {
                HandleSceneAction(audio, () => audioAction(audio));
            }
            foreach (var inds in HookDecideMusic.Enumerate()) {
                HandleSceneAction(inds, () => {
                    if (inds.CanOverride()) {
                        instanceAction(inds);
                    }
                });
            }
        }

        /// <summary>
        /// 安全地执行场景相关的操作，包含冷却和详细的异常日志记录
        /// </summary>
        private static void HandleSceneAction(object obj, Action action) {
            if (lastErrorByKey.TryGetValue(obj, out var errorTime) && (DateTime.UtcNow - errorTime).TotalSeconds < VaultMod.LogCooldownSeconds) {
                return; //错误在冷却时间内，暂时忽略以避免刷屏
            }

            try {
                action();
            } catch (Exception ex) {
                lastErrorByKey[obj] = DateTime.UtcNow;

                string logMessage = $"Scene action failed for object [{obj.GetType().Name}]. Details:\n{ex}";
                VaultMod.LoggerError($"[SceneRebuildLoader]", logMessage);

                string userMessage = $"Error in scene component '{obj.GetType().Name}'. See logs for details.";
                VaultUtils.Text(userMessage, Color.Red);
            }
        }
    }
}