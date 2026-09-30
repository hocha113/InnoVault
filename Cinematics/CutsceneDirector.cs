using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 全局演出导演器，负责播放、停止和推进当前时间轴
    /// <br/>镜头：演出是镜头栈（<see cref="VaultCamera"/>）里优先级 1000 的一层——从上一帧玩家实际看到的画面起步，
    /// 结束后按 <see cref="CutsceneClip.BlendOutFrames"/> 混回下层实时镜头；一段演出被另一段接上时镜头连续不跳
    /// <br/>时钟：缺省内部逐帧；时间轴 <see cref="CutsceneTimeline.UseClock"/> 后跟外部时钟（同步的 Boss 计时），
    /// <see cref="CutsceneTimeline.HoldWhile"/> / <see cref="CutsceneTimeline.AddWait"/> 让内部时钟停
    /// </summary>
    public static class CutsceneDirector
    {
        /// <summary>全局演出摄像机（镜头栈的一层）</summary>
        private static CutsceneCameraRuntime Camera { get; } = new();

        internal static void ApplyCameraInputLock(Player player) => Camera.ApplyInputLock(player);

        /// <summary>当前正在播放的演出</summary>
        public static CutsceneClip CurrentClip { get; private set; }

        /// <summary>当前演出上下文</summary>
        public static CutsceneContext CurrentContext { get; private set; }

        /// <summary>当前演出播放帧</summary>
        public static int CurrentTick { get; private set; }

        /// <summary>是否正在播放演出</summary>
        public static bool IsPlaying => CurrentClip != null;

        /// <summary>
        /// 当前生效的输入锁（没有演出为 <see cref="CutsceneInputLockFlags.None"/>）。上一次时间轴更新时各轨道请求的并集，
        /// 本地玩家下一帧的 <c>SetControls</c> / <c>ProcessTriggers</c> 读到的就是它
        /// </summary>
        public static CutsceneInputLockFlags InputLock => IsPlaying ? Camera.RequestedInputLock : CutsceneInputLockFlags.None;

        /// <summary>
        /// 某类输入此刻是否被演出锁住（任一位命中即真）。模组自己的键位在 <c>ProcessTriggers</c> 里查
        /// <see cref="CutsceneInputLockFlags.Abilities"/>；收回、变身这类会打断演出前置条件的操作更应该在演出期间整个拒绝（查 <see cref="IsPlaying"/>）
        /// </summary>
        public static bool IsInputLocked(CutsceneInputLockFlags flags = CutsceneInputLockFlags.Abilities) => (InputLock & flags) != 0;

        /// <summary>
        /// 按类型播放一个已注册的演出
        /// </summary>
        public static bool Play<T>(Player player = null, bool restartSameClip = true) where T : CutsceneClip {
            if (!CutsceneClip.TypeToInstance.TryGetValue(typeof(T), out CutsceneClip clip)) {
                return false;
            }
            return PlayCore(clip, player, restartSameClip, null);
        }

        /// <summary>
        /// 按类型播放一个绑定演出主体的演出
        /// </summary>
        public static bool Play<TClip, TSubject>(TSubject subject, Player player = null, bool restartSameClip = true)
            where TClip : CutsceneClip<TSubject> {
            if (!CutsceneClip.TypeToInstance.TryGetValue(typeof(TClip), out CutsceneClip clip)) {
                return false;
            }
            return PlayCore(clip, player, restartSameClip, subject);
        }

        private static bool PlayCore(CutsceneClip clip, Player player, bool restartSameClip, object subject) {
            if (VaultUtils.isServer || clip == null) {
                return false;
            }

            player ??= Main.LocalPlayer;
            if (player == null || !player.active || !clip.CanPlayWithSubject(player, subject)) {
                return false;
            }

            if (CurrentClip != null) {
                if (CurrentClip == clip && !restartSameClip) {
                    return true;
                }

                if (CurrentClip.Priority > clip.Priority) {
                    return false;
                }

                //被接上：只停时间轴，镜头保留平滑状态，新演出从当前画面接着走
                StopClip();
            }

            CurrentClip = clip;
            CurrentTick = 0;
            CurrentContext = new CutsceneContext(clip, player, Camera, subject) {
                Duration = clip.Duration,
                Tick = 0
            };

            Camera.Begin(clip.BlendOutFrames);
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
        /// 对当前演出触发一次屏幕震动
        /// </summary>
        public static void Shake(Vector2 direction, float intensity, float decay = 0.9f, int duration = 20) {
            if (CurrentClip == null) {
                return;
            }

            Camera.Shake(direction, intensity, decay, duration);
        }

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

            CutsceneTimeline timeline = CurrentClip.Timeline;
            bool external = timeline.Clock != null;
            if (external) {
                try {
                    CurrentTick = Math.Max(0, timeline.Clock(CurrentContext));
                } catch (Exception ex) {
                    VaultMod.LoggerError("[CutsceneDirector:Clock]", $"Cutscene clock failed: {ex.Message}");
                    Stop(immediate: false);
                    return;
                }
            }

            CurrentContext.Tick = CurrentTick;
            CurrentContext.Duration = CurrentClip.Duration;

            try {
                timeline.Update(CurrentContext);
            } catch (Exception ex) {
                VaultMod.LoggerError("[CutsceneDirector:Update]", $"Cutscene update failed: {ex.Message}");
                Stop(immediate: false);
                return;
            }

            if (CurrentClip == null) {
                return;
            }

            if (external) {
                if (CurrentTick >= CurrentClip.Duration) {
                    Stop(immediate: false);
                }
                return;
            }

            bool hold;
            try {
                hold = (timeline.HoldCondition?.Invoke(CurrentContext) ?? false) || timeline.Blocked(CurrentContext);
            } catch (Exception ex) {
                VaultMod.LoggerError("[CutsceneDirector:Hold]", $"Cutscene hold check failed: {ex.Message}");
                hold = false;
            }
            if (CurrentClip == null) {
                return;
            }
            if (!hold) {
                CurrentTick++;
            }
            if (CurrentTick >= CurrentClip.Duration) {
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

        private static void StopClip() {
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
        }

        private static void Stop(bool immediate) {
            StopClip();

            if (immediate) {
                Camera.Reset();
            }
            else {
                Camera.End();
            }
        }
    }
}
