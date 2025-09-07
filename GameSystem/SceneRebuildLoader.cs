using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using static InnoVault.GameSystem.SceneOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于场景重制节点的钩子均挂载于此处
    /// </summary>
    public class SceneRebuildLoader : IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_UpdateAudio_Dlelgate(Main main);
        public static List<IUpdateAudio> UpdateAudios { get; internal set; } = [];
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
        /// <summary>
        /// 存储声音接口内报错的时间戳
        /// </summary>
        private static readonly Dictionary<object, DateTime> lastErrorByKey = [];

        void IVaultLoader.LoadData() {
            On_Main.UpdateAudio_DecideOnTOWMusic += DecideOnTOWMusicEvent;
            On_Main.UpdateAudio_DecideOnNewMusic += DecideOnNewMusicEvent;
            UpdateAudios = VaultUtils.GetDerivedInstances<IUpdateAudio>();
            VaultHook.Add(typeof(Main).GetMethod("UpdateAudio", BindingFlags.Instance | BindingFlags.NonPublic), OnUpdateAudioHook);
        }

        void IVaultLoader.UnLoadData() {
            On_Main.UpdateAudio_DecideOnTOWMusic -= DecideOnTOWMusicEvent;
            On_Main.UpdateAudio_DecideOnNewMusic -= DecideOnNewMusicEvent;
            UpdateAudios?.Clear();
            lastErrorByKey?.Clear();
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

        /// <summary>
        /// 统一处理所有场景对象的行为，并包含错误处理逻辑
        /// </summary>
        /// <param name="audioAction">对 IUpdateAudio 实例执行的操作</param>
        /// <param name="instanceAction">对 SceneOverride 实例执行的操作</param>
        private static void ProcessSceneActions(Action<IUpdateAudio> audioAction, Action<SceneOverride> instanceAction) {
            foreach (var audio in UpdateAudios) {
                HandleSceneAction(audio, () => audioAction(audio));
            }
            foreach (var inds in Instances) {
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