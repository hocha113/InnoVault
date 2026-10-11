using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 跟随镜头：画面跟着一个实体（缺省本地玩家）走，带死区（实体在死区里活动时镜头不动）与按速度前瞻，用临界阻尼弹簧平滑。
    /// 场景镜头组件、Boss 场地等直接 new 一个交给 <see cref="VaultCamera.Register"/>
    /// </summary>
    public class FollowCameraSource : CameraSource
    {
        private readonly int _priority;
        private Vector2 _center;
        private Vector2 _velocity;
        private bool _seeded;

        /// <summary>建一个跟随镜头</summary>
        /// <param name="priority">镜头栈优先级，缺省 200（在场景缩放层之上、演出之下）</param>
        public FollowCameraSource(int priority = 200) {
            _priority = priority;
        }

        /// <inheritdoc/>
        public override int Priority => _priority;

        /// <summary>是否接管，缺省为真</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>跟随的实体，为空时取本地玩家</summary>
        public Func<Entity> Target { get; set; }

        /// <summary>死区半尺寸（世界像素）：目标离画面中心不超过它时镜头不动</summary>
        public Vector2 DeadZone { get; set; } = new(48f, 32f);

        /// <summary>前瞻：画面中心往目标速度方向提前的帧数（速度 × 帧数）</summary>
        public float LookAhead { get; set; } = 12f;

        /// <summary>前瞻偏移的上限（世界像素）</summary>
        public float MaxLookAhead { get; set; } = 160f;

        /// <summary>平滑角频率（rad/s，越大跟得越紧；0 = 不平滑）</summary>
        public float Smoothing { get; set; } = 8f;

        /// <inheritdoc/>
        public override bool Active => Enabled && ResolveTarget() != null;

        /// <inheritdoc/>
        public override void OnBlendStart(in CameraState current) {
            _center = current.Center;
            _velocity = Vector2.Zero;
            _seeded = true;
        }

        /// <inheritdoc/>
        public override void Update(in CameraState below) {
            Entity target = ResolveTarget();
            if (target == null) {
                return;
            }
            if (!_seeded) {
                _center = target.Center;
                _seeded = true;
            }
            Vector2 lead = target.velocity * LookAhead;
            if (lead.Length() > MaxLookAhead) {
                lead = Vector2.Normalize(lead) * MaxLookAhead;
            }
            Vector2 focus = target.Center + lead;
            Vector2 offset = focus - _center;
            Vector2 desired = _center + new Vector2(Excess(offset.X, DeadZone.X), Excess(offset.Y, DeadZone.Y));
            if (Smoothing <= 0f) {
                _center = desired;
                return;
            }
            Rigs2D.Solvers.Spring2D.Critical(ref _center, ref _velocity, desired, Smoothing);
        }

        /// <inheritdoc/>
        public override bool Evaluate(in CameraState below, out CameraState shot) {
            shot = new CameraState(_seeded ? _center : below.Center, below.Zoom);
            return _seeded;
        }

        private Entity ResolveTarget() {
            Entity target = Target?.Invoke() ?? Main.LocalPlayer;
            return target != null && target.active ? target : null;
        }

        private static float Excess(float value, float half) => value > half ? value - half : value < -half ? value + half : 0f;
    }

    /// <summary>
    /// 镜头锁定的轴
    /// </summary>
    public enum RailAxis
    {
        /// <summary>镜头只沿水平方向移动，竖直坐标锁在 <see cref="RailCameraSource.Line"/>（横版）</summary>
        Horizontal,
        /// <summary>镜头只沿竖直方向移动，水平坐标锁在 <see cref="RailCameraSource.Line"/>（竖井）</summary>
        Vertical,
    }

    /// <summary>
    /// 轨道镜头：锁定一个轴，另一个轴沿用下层镜头（通常是跟随玩家的原版镜头）
    /// </summary>
    public class RailCameraSource : CameraSource
    {
        private readonly int _priority;

        /// <summary>建一个轨道镜头</summary>
        /// <param name="axis">镜头移动的方向</param>
        /// <param name="line">被锁定那个轴的世界坐标（像素）</param>
        /// <param name="priority">镜头栈优先级，缺省 200</param>
        public RailCameraSource(RailAxis axis, float line, int priority = 200) {
            Axis = axis;
            Line = line;
            _priority = priority;
        }

        /// <inheritdoc/>
        public override int Priority => _priority;

        /// <summary>是否接管，缺省为真</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>镜头移动的方向</summary>
        public RailAxis Axis { get; set; }

        /// <summary>被锁定那个轴的世界坐标（像素）</summary>
        public float Line { get; set; }

        /// <inheritdoc/>
        public override bool Active => Enabled;

        /// <inheritdoc/>
        public override bool Evaluate(in CameraState below, out CameraState shot) {
            Vector2 center = below.Center;
            if (Axis == RailAxis.Horizontal) {
                center.Y = Line;
            }
            else {
                center.X = Line;
            }
            shot = new CameraState(center, below.Zoom);
            return true;
        }
    }

    /// <summary>
    /// 区域全景镜头：把一块区域（物块坐标）连同玩家一起装进画面，竞技场一类直接用
    /// </summary>
    public class AreaFitCameraSource : FramingCameraSource
    {
        private readonly int _priority;

        /// <summary>建一个区域全景镜头</summary>
        /// <param name="area">要装进画面的区域（物块坐标）</param>
        /// <param name="priority">镜头栈优先级，缺省 200</param>
        public AreaFitCameraSource(Rectangle area, int priority = 200) {
            Area = area;
            _priority = priority;
            Margin = 32f;
        }

        /// <inheritdoc/>
        public override int Priority => _priority;

        /// <summary>是否接管，缺省为真</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>要装进画面的区域（物块坐标）</summary>
        public Rectangle Area { get; set; }

        /// <summary>是否把本地玩家作为必须留在画面里的主体，缺省为真</summary>
        public bool KeepPlayer { get; set; } = true;

        /// <inheritdoc/>
        public override bool Active => Enabled;

        /// <inheritdoc/>
        protected override void GatherSubjects(List<CameraSubject> into) {
            Rectangle world = new(Area.X * 16, Area.Y * 16, Area.Width * 16, Area.Height * 16);
            into.Add(CameraSubject.FromRect(world));
            Player player = Main.LocalPlayer;
            if (KeepPlayer && player != null && player.active) {
                into.Add(CameraSubject.FromRect(player.Hitbox, 2f, required: true));
            }
        }
    }
}
