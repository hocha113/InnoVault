using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 时间轴轨道每帧接收的运行时上下文
    /// </summary>
    public sealed class CutsceneContext
    {
        private readonly CutsceneCameraRuntime camera;
        private readonly object subject;

        internal CutsceneContext(CutsceneClip clip, Player player, CutsceneCameraRuntime camera, object subject) {
            Clip = clip;
            Player = player;
            this.camera = camera;
            this.subject = subject;
        }

        /// <summary>当前正在播放的演出定义</summary>
        public CutsceneClip Clip { get; internal set; }

        /// <summary>演出绑定的本地玩家</summary>
        public Player Player { get; internal set; }

        /// <summary>当前播放帧，从 0 开始</summary>
        public int Tick { get; internal set; }

        /// <summary>当前演出总帧数</summary>
        public int Duration { get; internal set; }

        /// <summary>当前播放进度，范围为 0 到 1</summary>
        public float Progress => Duration <= 0 ? 1f : MathHelper.Clamp(Tick / (float)Duration, 0f, 1f);

        /// <summary>当前帧的玩家中心，玩家无效时返回零向量</summary>
        public Vector2 PlayerCenter => Player != null && Player.active ? Player.Center : Vector2.Zero;

        /// <summary>
        /// 尝试将演出主体转换成指定类型
        /// </summary>
        public bool TryGetSubject<T>(out T value) {
            if (subject is T typedValue) {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// 设置本帧摄像机焦点
        /// </summary>
        public void SetCameraFocus(Vector2 focusTarget, float lerpSpeed = 0.03f) {
            camera.SetFocus(focusTarget, lerpSpeed);
        }

        /// <summary>
        /// 设置本帧摄像机目标缩放
        /// </summary>
        public void SetCameraZoom(float zoom, float lerpSpeed = 0.02f) {
            camera.SetZoom(zoom, lerpSpeed);
        }

        /// <summary>
        /// 请求本帧锁定指定输入
        /// </summary>
        public void RequestInputLock(CutsceneInputLockFlags flags = CutsceneInputLockFlags.All) {
            camera.RequestInputLock(flags);
        }

        /// <summary>
        /// 触发屏幕震动
        /// </summary>
        public void Shake(Vector2 direction, float intensity, float decay = 0.9f, int duration = 20) {
            camera.Shake(direction, intensity, decay, duration);
        }

        /// <summary>
        /// 停止当前演出
        /// </summary>
        public void Stop() {
            CutsceneDirector.Stop();
        }
    }
}
