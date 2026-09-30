using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 演出摄像机：镜头栈（<see cref="VaultCamera"/>）里优先级 1000 的一层，集中处理演出的焦点、缩放与输入锁定
    /// <br/>开场从上一帧玩家实际看到的画面起步（不是从原版以真身为中心的镜头跳过去）；没有焦点轨道时贴着下层走；
    /// 演出结束时权重在 <see cref="BlendOutFrames"/> 帧内降回 0，混回下层实时的镜头；缩放只改视图矩阵，不碰玩家的缩放设置
    /// </summary>
    internal sealed class CutsceneCameraRuntime : CameraSource
    {
        private const float ZoomSnapEpsilon = 0.001f;
        //没有焦点 / 缩放轨道时贴着下层走的追近比例：下层在演出开始那一帧换了构图（让位给演出的写法）也是平滑过去，不跳
        private const float FollowBelowLerp = 0.12f;

        private bool active;
        private bool seeded;
        private CameraState smoothed;
        private Vector2 focusTarget;
        private bool hasFocus;
        private float targetZoom = 1f;
        private bool hasZoom;
        private float zoomLerpSpeed = 0.02f;
        private float positionLerpSpeed = 0.03f;
        private int blendOutFrames = 36;
        private CutsceneInputLockFlags requestedInputLock;

        public override int Priority => 1000;

        //起步已经对齐当前画面，不需要再从 0 混入
        public override int BlendInFrames => 0;

        public override int BlendOutFrames => blendOutFrames;

        public override bool Active => active;

        /// <summary>摄像机期望聚焦的世界坐标（没有焦点轨道写过时为下层镜头中心）</summary>
        public Vector2 FocusTarget => hasFocus ? focusTarget : smoothed.Center;

        /// <summary>当前平滑后的镜头</summary>
        public CameraState Current => smoothed;

        /// <summary>当前帧请求锁定的输入</summary>
        public CutsceneInputLockFlags RequestedInputLock => requestedInputLock;

        /// <summary>
        /// 每帧时间轴更新前调用，用于清理上一帧的瞬时请求
        /// </summary>
        internal void PrepareFrame() {
            requestedInputLock = CutsceneInputLockFlags.None;
        }

        /// <summary>
        /// 开始接管摄像机：新演出不重置已有的平滑状态（前一段演出接着一段时镜头连续），焦点 / 缩放等轨道来写
        /// </summary>
        internal void Begin(int blendOut) {
            VaultCamera.Register(this);
            blendOutFrames = Math.Max(blendOut, 0);
            active = true;
            hasFocus = false;
            hasZoom = false;
            if (weight <= 0f) {
                seeded = false;
            }
        }

        /// <summary>
        /// 停止接管：权重按混出帧数降回 0，混回下层镜头
        /// </summary>
        internal void End() {
            active = false;
            requestedInputLock = CutsceneInputLockFlags.None;
        }

        /// <summary>
        /// 立即重置（换世界）
        /// </summary>
        internal void Reset() {
            active = false;
            seeded = false;
            hasFocus = false;
            hasZoom = false;
            weight = 0f;
            requestedInputLock = CutsceneInputLockFlags.None;
        }

        /// <summary>
        /// 设置摄像机焦点（此后一直生效，直到别的焦点轨道改写）
        /// </summary>
        internal void SetFocus(Vector2 target, float lerpSpeed = 0.03f) {
            focusTarget = target;
            hasFocus = true;
            positionLerpSpeed = MathHelper.Clamp(lerpSpeed, 0f, 1f);
        }

        /// <summary>
        /// 设置摄像机目标缩放
        /// </summary>
        internal void SetZoom(float zoom, float lerpSpeed = 0.02f) {
            targetZoom = Math.Max(0.1f, zoom);
            hasZoom = true;
            zoomLerpSpeed = MathHelper.Clamp(lerpSpeed, 0f, 1f);
        }

        /// <summary>
        /// 请求本帧锁定指定输入
        /// </summary>
        internal void RequestInputLock(CutsceneInputLockFlags flags) {
            requestedInputLock |= flags;
        }

        /// <summary>
        /// 触发屏幕震动（交给镜头栈，叠在最终镜头上）
        /// </summary>
        internal void Shake(Vector2 direction, float intensity, float decay = 0.9f, int duration = 20)
            => VaultCamera.Shake(direction, intensity, decay, duration);

        public override void OnBlendStart(in CameraState current) {
            smoothed = current;
            seeded = true;
        }

        public override void Update(in CameraState below) {
            if (!seeded) {
                smoothed = below;
                seeded = true;
            }
            if (!active) {
                //混出中：定在最后一个镜头上，由权重把画面交还下层
                return;
            }
            smoothed.Center = Vector2.Lerp(smoothed.Center, hasFocus ? focusTarget : below.Center, hasFocus ? positionLerpSpeed : FollowBelowLerp);
            float zoomGoal = hasZoom ? targetZoom : below.Zoom;
            smoothed.Zoom = MathHelper.Lerp(smoothed.Zoom, zoomGoal, hasZoom ? zoomLerpSpeed : FollowBelowLerp);
            if (Math.Abs(smoothed.Zoom - zoomGoal) <= ZoomSnapEpsilon) {
                smoothed.Zoom = zoomGoal;
            }
        }

        public override bool Evaluate(in CameraState below, out CameraState shot) {
            shot = seeded ? smoothed : below;
            return true;
        }

        /// <summary>
        /// 在 <see cref="Terraria.ModLoader.ModPlayer.SetControls"/> 中调用，应用输入锁定
        /// </summary>
        internal void ApplyInputLock(Player player) {
            if (!active || requestedInputLock == CutsceneInputLockFlags.None || player == null || !player.active) {
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
    }
}
