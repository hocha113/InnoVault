using System;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 全局演出导演器，负责播放、停止和推进当前时间轴
    /// </summary>
    public static class CutsceneDirector
    {
        /// <summary>全局演出摄像机运行时</summary>
        public static CutsceneCameraRuntime Camera { get; } = new();

        /// <summary>当前正在播放的演出</summary>
        public static CutsceneClip CurrentClip { get; private set; }

        /// <summary>当前演出上下文</summary>
        public static CutsceneContext CurrentContext { get; private set; }

        /// <summary>当前演出播放帧</summary>
        public static int CurrentTick { get; private set; }

        /// <summary>是否正在播放演出</summary>
        public static bool IsPlaying => CurrentClip != null;

        /// <summary>
        /// 按类型播放一个已注册的演出
        /// </summary>
        public static bool Play<T>(Player player = null, bool restartSameClip = true, object tag = null) where T : CutsceneClip {
            if (!CutsceneClip.TypeToInstance.TryGetValue(typeof(T), out CutsceneClip clip)) {
                return false;
            }
            return Play(clip, player, restartSameClip, tag);
        }

        /// <summary>
        /// 播放指定演出
        /// </summary>
        public static bool Play(CutsceneClip clip, Player player = null, bool restartSameClip = true, object tag = null) {
            if (VaultUtils.isServer || clip == null) {
                return false;
            }

            player ??= Main.LocalPlayer;
            if (player == null || !player.active || !clip.CanPlay(player, tag)) {
                return false;
            }

            if (CurrentClip != null) {
                if (CurrentClip == clip && !restartSameClip) {
                    return true;
                }

                if (CurrentClip.Priority > clip.Priority) {
                    return false;
                }

                Stop(immediate: true);
            }

            CurrentClip = clip;
            CurrentTick = 0;
            CurrentContext = new CutsceneContext(clip, player, Camera, tag) {
                Duration = clip.Duration,
                Tick = 0
            };

            Camera.Begin(player.Center);
            clip.Timeline.OnStart(CurrentContext);
            return true;
        }

        /// <summary>
        /// 平滑停止当前演出
        /// </summary>
        public static void Stop() => Stop(immediate: false);

        /// <summary>
        /// 跳过当前演出，并平滑恢复镜头
        /// </summary>
        public static void Skip() => Stop(immediate: false);

        /// <summary>
        /// 每帧推进当前演出
        /// </summary>
        public static void Update() {
            Camera.PrepareFrame();

            if (VaultUtils.isServer || CurrentClip == null || CurrentContext == null) {
                return;
            }

            Player player = CurrentContext.Player;
            if (player == null || !player.active) {
                Stop(immediate: false);
                return;
            }

            CurrentContext.Tick = CurrentTick;
            CurrentContext.Duration = CurrentClip.Duration;

            try {
                CurrentClip.Timeline.Update(CurrentContext);
            } catch (Exception ex) {
                VaultMod.LoggerError("[CutsceneDirector:Update]", $"Cutscene update failed: {ex.Message}");
                Stop(immediate: false);
                return;
            }

            if (CurrentClip == null) {
                return;
            }

            CurrentTick++;
            if (CurrentTick > CurrentClip.Duration) {
                Stop(immediate: false);
            }
        }

        /// <summary>
        /// 立即清空所有演出运行时状态
        /// </summary>
        public static void Reset() {
            Stop(immediate: true);
            Camera.Reset();
        }

        private static void Stop(bool immediate) {
            if (CurrentClip != null && CurrentContext != null) {
                try {
                    CurrentClip.Timeline.OnStop(CurrentContext);
                } catch (Exception ex) {
                    VaultMod.LoggerError("[CutsceneDirector:Stop]", $"Cutscene stop failed: {ex.Message}");
                }
            }

            CurrentClip = null;
            CurrentContext = null;
            CurrentTick = 0;

            if (immediate) {
                Camera.Reset();
            }
            else {
                Camera.End();
            }
        }
    }
}
