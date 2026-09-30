using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.Graphics;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 镜头栈：把各个 <see cref="CameraSource"/> 按优先级逐层混合成最终镜头，统一处理震屏与缩放
    /// <br/>时机：原版镜头与所有模组的 <c>ModPlayer</c> / <c>ModSystem.ModifyScreenPosition</c> 都跑完之后（InnoVault 挂在
    /// <c>Main.DoDraw_UpdateCameraPosition</c> 之后），所以别处直接写 <c>Main.screenPosition</c> 的结果就是最底层；
    /// 缩放经 <c>ModifyTransformMatrix</c> 只改本帧的视图矩阵，从不改玩家设置里的 <c>Main.GameZoomTarget</c>
    /// <br/>来源的进出按各自的混合帧数平滑；新来源从"上一帧玩家实际看到的画面"（<see cref="LastComposed"/>）起步，
    /// 演出开场不会从原始镜头跳一下，结束时混回的是下层实时的镜头（下层自己在动也跟得上）
    /// </summary>
    public static class VaultCamera
    {
        private static readonly List<CameraSource> sources = [];
        private static readonly List<CameraSource> sorted = [];
        private static bool dirty;
        private static Action clampToWorld;
        private static bool clampResolved;

        private static Vector2 shakeDirection;
        private static float shakeIntensity;
        private static float shakeDecay;
        private static int shakeDuration;
        private static int shakeTimer;

        /// <summary>缩放下限（原版低于 1 会露出未绘制的黑边）</summary>
        public static float MinZoom { get; set; } = 1f;
        /// <summary>缩放上限</summary>
        public static float MaxZoom { get; set; } = 4f;
        /// <summary>本帧原始镜头（原版 + 其他模组直接写的结果）</summary>
        public static CameraState Base { get; private set; } = new(Vector2.Zero, 1f);
        /// <summary>本帧合成后的镜头（不含震屏）</summary>
        public static CameraState Composed { get; private set; } = new(Vector2.Zero, 1f);
        /// <summary>上一帧玩家实际看到的镜头（新来源从这里起步）</summary>
        public static CameraState LastComposed { get; private set; } = new(Vector2.Zero, 1f);
        /// <summary>本帧是否有来源在改缩放</summary>
        public static bool ControllingZoom { get; private set; }
        /// <summary>本帧是否有来源在接管（任一权重大于 0）</summary>
        public static bool Engaged { get; private set; }
        /// <summary>进世界后是否已经合成过至少一帧（之前 <see cref="LastComposed"/> 无意义，来源不起步）</summary>
        public static bool HasComposed { get; private set; }
        /// <summary>已登记的来源（按优先级升序）</summary>
        public static IReadOnlyList<CameraSource> Sources {
            get {
                EnsureSorted();
                return sorted;
            }
        }
        /// <summary>屏幕像素尺寸</summary>
        public static Vector2 ScreenSize => new(Main.screenWidth, Main.screenHeight);
        /// <summary>原版强制最小缩放（超大分辨率下大于 1）</summary>
        public static float ForcedMinimumZoom => Main.ForcedMinimumZoom;
        /// <summary>某缩放下画面覆盖的世界尺寸</summary>
        public static Vector2 VisibleSize(float zoom) => CameraFraming.VisibleSize(ScreenSize, zoom, ForcedMinimumZoom);

        /// <summary>
        /// 登记一个来源（重复登记无效）
        /// </summary>
        public static void Register(CameraSource source) {
            if (source == null || sources.Contains(source)) {
                return;
            }
            sources.Add(source);
            dirty = true;
        }

        /// <summary>
        /// 注销一个来源（立即失效，不做混出；想平滑退出就先让它 <see cref="CameraSource.Active"/> 为假，等权重归零再注销）
        /// </summary>
        public static void Unregister(CameraSource source) {
            if (source != null && sources.Remove(source)) {
                source.weight = 0f;
                dirty = true;
            }
        }

        /// <summary>
        /// 某来源下面各层在最近一次合成时给出的镜头
        /// </summary>
        public static CameraState Below(CameraSource source) {
            EnsureSorted();
            CameraState state = Base;
            for (int i = 0; i < sorted.Count; i++) {
                CameraSource s = sorted[i];
                if (s == source) {
                    break;
                }
                if (s.weight > 0f && s.Evaluate(state, out CameraState shot)) {
                    state = CameraState.Lerp(state, shot, Ease(s.weight));
                }
            }
            return state;
        }

        /// <summary>
        /// 震屏（叠在最终镜头上）
        /// </summary>
        /// <param name="direction">方向，零向量为随机方向</param>
        /// <param name="intensity">初始偏移像素</param>
        /// <param name="decay">每帧衰减系数</param>
        /// <param name="duration">持续帧数</param>
        public static void Shake(Vector2 direction, float intensity, float decay = 0.9f, int duration = 20) {
            if (Main.dedServ || intensity <= 0f || duration <= 0) {
                return;
            }
            if (direction == Vector2.Zero) {
                direction = Main.rand.NextFloat(MathHelper.TwoPi).ToRotationVector2();
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

        private static float Ease(float w) {
            w = MathHelper.Clamp(w, 0f, 1f);
            return w * w * (3f - 2f * w);
        }

        private static void EnsureSorted() {
            if (!dirty && sorted.Count == sources.Count) {
                return;
            }
            sorted.Clear();
            sorted.AddRange(sources);
            //优先级升序；同级按登记顺序（List.Sort 不稳定，带下标比较）
            Dictionary<CameraSource, int> order = [];
            for (int i = 0; i < sources.Count; i++) {
                order[sources[i]] = i;
            }
            sorted.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : order[a].CompareTo(order[b]));
            dirty = false;
        }

        //==================== 推进（游戏帧） ====================

        internal static void Update() {
            if (Main.dedServ || !HasComposed) {
                //进世界后还没画过一帧：没有"当前画面"可起步，等第一帧合成
                return;
            }
            EnsureSorted();
            for (int i = 0; i < sorted.Count; i++) {
                CameraSource s = sorted[i];
                bool active;
                try {
                    active = s.Active;
                } catch (Exception ex) {
                    active = false;
                    VaultMod.LoggerError($"[VaultCamera:{s.GetType().Name}]", $"Active threw: {ex.Message}");
                }
                if (active && s.weight <= 0f) {
                    s.OnBlendStart(LastComposed);
                }
                if (active) {
                    s.weight = s.BlendInFrames <= 0 ? 1f : MathF.Min(1f, s.weight + 1f / s.BlendInFrames);
                }
                else if (s.weight > 0f) {
                    s.weight = s.BlendOutFrames <= 0 ? 0f : MathF.Max(0f, s.weight - 1f / s.BlendOutFrames);
                }
                if (s.weight > 0f) {
                    try {
                        s.Update(s.lastBelow);
                    } catch (Exception ex) {
                        VaultMod.LoggerError($"[VaultCamera:{s.GetType().Name}]", $"Update threw: {ex.Message}");
                    }
                }
            }
            if (shakeTimer < shakeDuration) {
                shakeTimer++;
            }
        }

        //==================== 应用（绘制帧） ====================

        internal static void Apply() {
            if (Main.dedServ || Main.gameMenu) {
                return;
            }
            Vector2 screen = ScreenSize;
            CameraState state = new(Main.screenPosition + screen * 0.5f, MathHelper.Clamp(Main.GameZoomTarget, 1f, 2f));
            Base = state;
            EnsureSorted();
            bool any = false;
            for (int i = 0; i < sorted.Count; i++) {
                CameraSource s = sorted[i];
                s.lastBelow = state;
                if (s.weight <= 0f) {
                    continue;
                }
                bool ok;
                CameraState shot;
                try {
                    ok = s.Evaluate(state, out shot);
                } catch (Exception ex) {
                    ok = false;
                    shot = state;
                    VaultMod.LoggerError($"[VaultCamera:{s.GetType().Name}]", $"Evaluate threw: {ex.Message}");
                }
                if (!ok || !float.IsFinite(shot.Center.X) || !float.IsFinite(shot.Center.Y) || !float.IsFinite(shot.Zoom)) {
                    continue;
                }
                state = CameraState.Lerp(state, shot, Ease(s.weight));
                any = true;
            }
            if (any) {
                state.Zoom = MathHelper.Clamp(state.Zoom, MinZoom, MathF.Max(MaxZoom, MinZoom));
            }
            Composed = state;
            LastComposed = state;
            HasComposed = true;
            Engaged = any;
            ControllingZoom = any && MathF.Abs(state.Zoom - Base.Zoom) > 0.0005f;
            Vector2 shake = ShakeOffset();
            if (!any && shake == Vector2.Zero) {
                return;
            }
            Main.screenPosition = state.Center - screen * 0.5f + shake;
            Main.screenPosition.X = (int)Main.screenPosition.X;
            Main.screenPosition.Y = (int)Main.screenPosition.Y;
            ClampToWorld();
        }

        private static Vector2 ShakeOffset() {
            if (shakeTimer >= shakeDuration || shakeIntensity <= 0.5f) {
                return Vector2.Zero;
            }
            float progress = shakeTimer / (float)shakeDuration;
            float current = shakeIntensity * MathF.Pow(shakeDecay, shakeTimer) * (1f - progress);
            float sign = shakeTimer % 2 == 0 ? 1f : -1f;
            return shakeDirection.RotatedBy(Main.rand.NextFloat(-0.3f, 0.3f)) * current * sign;
        }

        private static void ClampToWorld() {
            if (!clampResolved) {
                clampResolved = true;
                MethodInfo m = typeof(Main).GetMethod("ClampScreenPositionToWorld", BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null) {
                    clampToWorld = m.CreateDelegate<Action>();
                }
            }
            clampToWorld?.Invoke();
        }

        internal static void ModifyTransform(ref SpriteViewMatrix transform) {
            if (Main.dedServ || Main.gameMenu || !ControllingZoom) {
                return;
            }
            transform.Zoom = new Vector2(Main.ForcedMinimumZoom * Composed.Zoom);
        }

        /// <summary>
        /// 清掉全部来源的权重与震屏（换世界时）；登记保留
        /// </summary>
        internal static void ResetState() {
            for (int i = 0; i < sources.Count; i++) {
                sources[i].weight = 0f;
            }
            shakeTimer = shakeDuration = 0;
            shakeIntensity = 0f;
            Engaged = false;
            ControllingZoom = false;
            HasComposed = false;
        }

        internal static void Clear() {
            ResetState();
            sources.Clear();
            sorted.Clear();
            clampToWorld = null;
            clampResolved = false;
        }
    }
}
