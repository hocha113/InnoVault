using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 演出摄像机运行时，集中处理屏幕位置、缩放、震动和输入锁定
    /// </summary>
    internal sealed class CutsceneCameraRuntime
    {
        private const float RestoreZoomLerpSpeed = 0.02f;
        private const float ZoomSnapEpsilon = 0.001f;

        private Vector2 smoothedScreenPosition;
        private bool initialized;
        private bool restoringZoom;
        private float currentZoom = 1f;
        private float restoreZoom = 1f;
        private float zoomLerpSpeed = 0.02f;
        private float positionLerpSpeed = 0.03f;

        private Vector2 shakeDirection;
        private float shakeIntensity;
        private float shakeDecay;
        private int shakeDuration;
        private int shakeTimer;

        private CutsceneInputLockFlags requestedInputLock;

        /// <summary>当前是否正在由演出控制镜头位置</summary>
        public bool Active { get; private set; }

        /// <summary>摄像机期望聚焦的世界坐标</summary>
        public Vector2 FocusTarget { get; private set; }

        /// <summary>摄像机目标缩放倍率</summary>
        public float TargetZoom { get; private set; } = 1f;

        /// <summary>摄像机当前缩放倍率</summary>
        public float CurrentZoom => currentZoom;

        /// <summary>当前帧请求锁定的输入</summary>
        public CutsceneInputLockFlags RequestedInputLock => requestedInputLock;

        /// <summary>
        /// 每帧时间轴更新前调用，用于清理上一帧的瞬时请求
        /// </summary>
        internal void PrepareFrame() {
            requestedInputLock = CutsceneInputLockFlags.None;
        }

        /// <summary>
        /// 开始接管摄像机
        /// </summary>
        internal void Begin(Vector2 initialFocus) {
            bool wasControllingZoom = Active || restoringZoom;
            float gameZoom = Math.Max(0.1f, Main.GameZoomTarget);

            if (!wasControllingZoom) {
                restoreZoom = gameZoom;
            }

            Active = true;
            restoringZoom = false;
            FocusTarget = initialFocus;
            currentZoom = gameZoom;
            TargetZoom = currentZoom;
            initialized = false;
        }

        /// <summary>
        /// 停止接管屏幕位置，并让缩放平滑恢复到演出开始前的值
        /// </summary>
        internal void End() {
            Active = false;
            restoringZoom = true;
            TargetZoom = restoreZoom;
            requestedInputLock = CutsceneInputLockFlags.None;
        }

        /// <summary>
        /// 立即重置运行时状态
        /// </summary>
        internal void Reset() {
            if (!VaultUtils.isServer && (Active || restoringZoom)) {
                Main.GameZoomTarget = restoreZoom;
            }

            Active = false;
            initialized = false;
            restoringZoom = false;
            currentZoom = !VaultUtils.isServer ? Math.Max(0.1f, Main.GameZoomTarget) : restoreZoom;
            TargetZoom = currentZoom;
            shakeTimer = 0;
            shakeDuration = 0;
            shakeIntensity = 0f;
            requestedInputLock = CutsceneInputLockFlags.None;
        }

        /// <summary>
        /// 设置本帧摄像机焦点
        /// </summary>
        internal void SetFocus(Vector2 focusTarget, float lerpSpeed = 0.03f) {
            FocusTarget = focusTarget;
            positionLerpSpeed = MathHelper.Clamp(lerpSpeed, 0f, 1f);
        }

        /// <summary>
        /// 设置本帧摄像机目标缩放
        /// </summary>
        internal void SetZoom(float zoom, float lerpSpeed = 0.02f) {
            TargetZoom = Math.Max(0.1f, zoom);
            zoomLerpSpeed = MathHelper.Clamp(lerpSpeed, 0f, 1f);
        }

        /// <summary>
        /// 请求本帧锁定指定输入
        /// </summary>
        internal void RequestInputLock(CutsceneInputLockFlags flags) {
            requestedInputLock |= flags;
        }

        /// <summary>
        /// 触发屏幕震动
        /// </summary>
        /// <param name="direction">震动方向，零向量会随机方向</param>
        /// <param name="intensity">初始偏移像素强度</param>
        /// <param name="decay">每帧衰减系数</param>
        /// <param name="duration">持续帧数</param>
        internal void Shake(Vector2 direction, float intensity, float decay = 0.9f, int duration = 20) {
            if (intensity <= 0f || duration <= 0) {
                return;
            }

            if (direction == Vector2.Zero) {
                float angle = Main.rand.NextFloat(MathHelper.TwoPi);
                direction = angle.ToRotationVector2();
            }
            else {
                direction.Normalize();
            }

            shakeDirection = direction;
            shakeIntensity = intensity;
            shakeDecay = MathHelper.Clamp(decay, 0f, 0.99f);
            shakeDuration = duration;
            shakeTimer = 0;
        }

        /// <summary>
        /// 在 <see cref="Terraria.ModLoader.ModPlayer.ModifyScreenPosition"/> 中调用，应用摄像机位置和缩放
        /// </summary>
        internal void ApplyScreenPosition() {
            if (VaultUtils.isServer) {
                return;
            }

            if (!Active && !restoringZoom) {
                initialized = false;
                return;
            }

            float zoomTarget = Active ? TargetZoom : restoreZoom;
            float speed = Active ? zoomLerpSpeed : RestoreZoomLerpSpeed;
            currentZoom = MathHelper.Lerp(currentZoom, zoomTarget, MathHelper.Clamp(speed, 0f, 1f));
            if (Math.Abs(currentZoom - zoomTarget) <= ZoomSnapEpsilon) {
                currentZoom = zoomTarget;
            }
            Main.GameZoomTarget = currentZoom;

            if (!Active) {
                initialized = false;
                restoringZoom = currentZoom != zoomTarget;
                return;
            }

            if (!initialized) {
                smoothedScreenPosition = Main.screenPosition;
                initialized = true;
            }

            Vector2 screenSize = new(Main.screenWidth, Main.screenHeight);
            Vector2 desiredScreenPosition = FocusTarget - screenSize * 0.5f;
            smoothedScreenPosition = Vector2.Lerp(smoothedScreenPosition, desiredScreenPosition, positionLerpSpeed);
            Main.screenPosition = smoothedScreenPosition + ConsumeShakeOffset();
        }

        /// <summary>
        /// 在 <see cref="Terraria.ModLoader.ModPlayer.SetControls"/> 中调用，应用输入锁定
        /// </summary>
        internal void ApplyInputLock(Player player) {
            if (!Active || requestedInputLock == CutsceneInputLockFlags.None || player == null || !player.active) {
                return;
            }

            if (requestedInputLock.HasFlag(CutsceneInputLockFlags.Movement)) {
                player.controlLeft = false;
                player.controlRight = false;
                player.controlUp = false;
                player.controlDown = false;
            }

            if (requestedInputLock.HasFlag(CutsceneInputLockFlags.Jump)) {
                player.controlJump = false;
            }

            if (requestedInputLock.HasFlag(CutsceneInputLockFlags.UseItem)) {
                player.controlUseItem = false;
            }

            if (requestedInputLock.HasFlag(CutsceneInputLockFlags.UseTile)) {
                player.controlUseTile = false;
            }

            if (requestedInputLock.HasFlag(CutsceneInputLockFlags.Utility)) {
                player.controlHook = false;
                player.controlThrow = false;
                player.controlMount = false;
                player.controlQuickHeal = false;
                player.controlQuickMana = false;
                player.controlSmart = false;
            }
        }

        private Vector2 ConsumeShakeOffset() {
            if (shakeTimer >= shakeDuration || shakeIntensity <= 0.5f) {
                return Vector2.Zero;
            }

            float progress = shakeTimer / (float)shakeDuration;
            float currentIntensity = shakeIntensity * MathF.Pow(shakeDecay, shakeTimer) * (1f - progress);
            float sign = shakeTimer % 2 == 0 ? 1f : -1f;
            float rotJitter = Main.rand.NextFloat(-0.3f, 0.3f);
            shakeTimer++;
            return shakeDirection.RotatedBy(rotJitter) * currentIntensity * sign;
        }
    }
}
