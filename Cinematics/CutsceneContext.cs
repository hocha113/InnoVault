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

        /// <summary>当前这道等待门已经等了多少帧（不在门前为 0）</summary>
        public int WaitTicks { get; internal set; }

        /// <summary>最近一道等待门是否因超时放行（本次播放内）</summary>
        public bool LastWaitTimedOut { get; internal set; }

        /// <summary>是否由外部时钟驱动</summary>
        public bool ExternalClock => Clip?.Timeline.Clock != null;

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
        /// 设置本帧摄像机目标缩放，<paramref name="zoom"/> 是相对玩家当前缩放的倍率：1 = 玩家自己的缩放，1.5 = 在玩家的基础上再推近一半。
        /// 玩家的缩放变了，目标跟着变，演出结束或轨道释放后画面回到玩家自己的缩放。
        /// 写缩放的自定义轨道请重写 <see cref="CutsceneTrack.ControlsZoom"/> 为 <see langword="true"/>
        /// </summary>
        public void SetCameraZoom(float zoom, float lerpSpeed = 0.02f) {
            camera.SetZoom(zoom, lerpSpeed, false);
        }

        /// <summary>
        /// 设置本帧摄像机目标缩放，<paramref name="zoom"/> 是屏幕上的实际缩放（与 <c>Main.GameZoomTarget</c> 同一单位），不随玩家的缩放变化。
        /// 只用于必须精确控制可见范围的场合（按主体构图装下画面之类）
        /// </summary>
        public void SetCameraZoomAbsolute(float zoom, float lerpSpeed = 0.02f) {
            camera.SetZoom(zoom, lerpSpeed, true);
        }

        internal void EndZoomFrame(bool zoomOwned) => camera.EndZoomFrame(zoomOwned);

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
