using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 演出时间轴中的一条轨道
    /// </summary>
    public abstract class CutsceneTrack
    {
        /// <summary>
        /// 创建一条轨道
        /// </summary>
        /// <param name="startTick">轨道开始帧</param>
        /// <param name="duration">轨道持续帧数</param>
        protected CutsceneTrack(int startTick, int duration) {
            StartTick = Math.Max(0, startTick);
            Duration = Math.Max(0, duration);
        }

        /// <summary>轨道开始帧</summary>
        public int StartTick { get; }

        /// <summary>轨道持续帧数</summary>
        public int Duration { get; }

        /// <summary>轨道结束帧</summary>
        public int EndTick => StartTick + Math.Max(Duration, 1);

        /// <summary>
        /// 判断该轨道在指定时间是否处于激活状态
        /// </summary>
        public virtual bool IsActiveAt(int tick) {
            if (Duration == 0) {
                return tick == StartTick;
            }
            return tick >= StartTick && tick < EndTick;
        }

        internal virtual void OnTimelineStart(CutsceneContext context) { }

        internal virtual void OnTimelineStop(CutsceneContext context) { }

        internal virtual void UpdateTrack(CutsceneContext context) {
            if (!IsActiveAt(context.Tick)) {
                return;
            }

            float progress = Duration <= 1 ? 1f : MathHelper.Clamp((context.Tick - StartTick) / (float)(Duration - 1), 0f, 1f);
            Update(context, progress);
        }

        /// <summary>
        /// 在轨道激活期间每帧更新
        /// </summary>
        /// <param name="context">演出运行时上下文</param>
        /// <param name="progress">该轨道的局部进度，范围为 0 到 1</param>
        protected abstract void Update(CutsceneContext context, float progress);
    }

    /// <summary>
    /// 控制摄像机焦点的轨道
    /// </summary>
    public sealed class CameraFocusTrack : CutsceneTrack
    {
        private readonly Func<CutsceneContext, Vector2> fromProvider;
        private readonly Func<CutsceneContext, Vector2> toProvider;
        private readonly Vector2 offset;
        private readonly bool interpolate;
        private readonly float lerpSpeed;
        private readonly CutsceneEase ease;
        private Vector2 capturedFrom;
        private bool captured;

        private CameraFocusTrack(
            int startTick,
            int duration,
            Func<CutsceneContext, Vector2> fromProvider,
            Func<CutsceneContext, Vector2> toProvider,
            Vector2 offset,
            bool interpolate,
            float lerpSpeed,
            CutsceneEase ease) : base(startTick, duration) {
            this.fromProvider = fromProvider;
            this.toProvider = toProvider ?? throw new ArgumentNullException(nameof(toProvider));
            this.offset = offset;
            this.interpolate = interpolate;
            this.lerpSpeed = MathHelper.Clamp(lerpSpeed, 0f, 1f);
            this.ease = ease;
        }

        /// <summary>
        /// 固定聚焦在一个世界坐标
        /// </summary>
        public static CameraFocusTrack Fixed(int startTick, int duration, Vector2 target, float lerpSpeed = 0.08f)
            => Follow(startTick, duration, _ => target, Vector2.Zero, lerpSpeed);

        /// <summary>
        /// 跟随一个动态世界坐标
        /// </summary>
        public static CameraFocusTrack Follow(
            int startTick,
            int duration,
            Func<CutsceneContext, Vector2> targetProvider,
            Vector2 offset = default,
            float lerpSpeed = 0.08f)
            => new(startTick, duration, null, targetProvider, offset, false, lerpSpeed, CutsceneEase.Linear);

        /// <summary>
        /// 在两个动态世界坐标之间插值
        /// </summary>
        public static CameraFocusTrack Lerp(
            int startTick,
            int duration,
            Func<CutsceneContext, Vector2> fromProvider,
            Func<CutsceneContext, Vector2> toProvider,
            Vector2 offset = default,
            float lerpSpeed = 0.08f,
            CutsceneEase ease = CutsceneEase.QuadInOut)
            => new(startTick, duration, fromProvider, toProvider, offset, true, lerpSpeed, ease);

        /// <summary>
        /// 聚焦在两个动态世界坐标的中点
        /// </summary>
        public static CameraFocusTrack Midpoint(
            int startTick,
            int duration,
            Func<CutsceneContext, Vector2> firstProvider,
            Func<CutsceneContext, Vector2> secondProvider,
            Vector2 offset = default,
            float lerpSpeed = 0.08f)
            => Follow(startTick, duration, context => (firstProvider(context) + secondProvider(context)) * 0.5f, offset, lerpSpeed);

        /// <inheritdoc/>
        internal override void OnTimelineStart(CutsceneContext context) => captured = false;

        /// <inheritdoc/>
        protected override void Update(CutsceneContext context, float progress) {
            Vector2 target;
            if (interpolate) {
                if (!captured) {
                    capturedFrom = fromProvider(context);
                    captured = true;
                }

                float eased = CutsceneEaseHelper.Evaluate(ease, progress);
                target = Vector2.Lerp(capturedFrom, toProvider(context), eased);
            }
            else {
                target = toProvider(context);
            }

            context.SetCameraFocus(target + offset, lerpSpeed);
        }
    }

    /// <summary>
    /// 控制摄像机缩放倍率的轨道
    /// </summary>
    public sealed class CameraZoomTrack : CutsceneTrack
    {
        private readonly float fromZoom;
        private readonly float toZoom;
        private readonly float lerpSpeed;
        private readonly CutsceneEase ease;

        /// <summary>
        /// 创建一条缩放轨道
        /// </summary>
        public CameraZoomTrack(int startTick, int duration, float fromZoom, float toZoom, float lerpSpeed = 0.05f, CutsceneEase ease = CutsceneEase.QuadInOut)
            : base(startTick, duration) {
            this.fromZoom = Math.Max(0.1f, fromZoom);
            this.toZoom = Math.Max(0.1f, toZoom);
            this.lerpSpeed = MathHelper.Clamp(lerpSpeed, 0f, 1f);
            this.ease = ease;
        }

        /// <inheritdoc/>
        protected override void Update(CutsceneContext context, float progress) {
            float eased = CutsceneEaseHelper.Evaluate(ease, progress);
            context.SetCameraZoom(MathHelper.Lerp(fromZoom, toZoom, eased), lerpSpeed);
        }
    }

    /// <summary>
    /// 触发一次屏幕震动的轨道
    /// </summary>
    public sealed class CameraShakeTrack : CutsceneTrack
    {
        private readonly Vector2 direction;
        private readonly float intensity;
        private readonly float decay;
        private readonly int shakeDuration;
        private bool fired;

        /// <summary>
        /// 创建一条屏幕震动轨道
        /// </summary>
        public CameraShakeTrack(int startTick, Vector2 direction, float intensity, float decay = 0.9f, int duration = 20)
            : base(startTick, 0) {
            this.direction = direction;
            this.intensity = intensity;
            this.decay = decay;
            shakeDuration = Math.Max(1, duration);
        }

        internal override void OnTimelineStart(CutsceneContext context) => fired = false;

        internal override void UpdateTrack(CutsceneContext context) {
            if (fired || context.Tick < StartTick) {
                return;
            }

            fired = true;
            context.Shake(direction, intensity, decay, shakeDuration);
        }

        /// <inheritdoc/>
        protected override void Update(CutsceneContext context, float progress) { }
    }

    /// <summary>
    /// 在一段时间内锁定玩家输入的轨道
    /// </summary>
    public sealed class InputLockTrack : CutsceneTrack
    {
        private readonly CutsceneInputLockFlags flags;

        /// <summary>
        /// 创建一条输入锁定轨道
        /// </summary>
        public InputLockTrack(int startTick, int duration, CutsceneInputLockFlags flags = CutsceneInputLockFlags.All)
            : base(startTick, duration) {
            this.flags = flags;
        }

        /// <inheritdoc/>
        protected override void Update(CutsceneContext context, float progress) {
            context.RequestInputLock(flags);
        }
    }

    /// <summary>
    /// 在指定帧触发一次回调的轨道
    /// </summary>
    public sealed class EventTrack : CutsceneTrack
    {
        private readonly Action<CutsceneContext> action;
        private bool fired;

        /// <summary>
        /// 创建一条事件轨道
        /// </summary>
        public EventTrack(int tick, Action<CutsceneContext> action)
            : base(tick, 0) {
            this.action = action ?? throw new ArgumentNullException(nameof(action));
        }

        internal override void OnTimelineStart(CutsceneContext context) => fired = false;

        internal override void UpdateTrack(CutsceneContext context) {
            if (fired || context.Tick < StartTick) {
                return;
            }

            fired = true;
            action(context);
        }

        /// <inheritdoc/>
        protected override void Update(CutsceneContext context, float progress) { }
    }
}
