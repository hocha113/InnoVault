using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 镜头的钳制范围
    /// </summary>
    public enum ScenarioCameraBounds
    {
        /// <summary>原版：四边各留约 41 格；舞台比一屏还窄时镜头钉在左边</summary>
        Vanilla,
        /// <summary>原版范围，但舞台比可视范围还小的那个方向上居中，而不是钉在一边</summary>
        CenterWhenSmaller,
        /// <summary>限制在 <see cref="ScenarioCamera.Area"/>（缺省取场景的 <see cref="ScenarioArea"/> 外接框）之内；区域比可视范围小时居中</summary>
        Area,
        /// <summary>限制在玩家所在的那一间房里（<see cref="ScenarioCamera.Rooms"/> 或按 <see cref="ScenarioCamera.RoomSize"/> 等分的网格），换房间时平移过去</summary>
        Rooms,
        /// <summary>不钳制</summary>
        Unbounded,
    }

    /// <summary>
    /// 镜头组件：钳制范围、缩放范围，以及激活期间推入 <see cref="VaultCamera"/> 镜头栈的镜头源<br/>
    /// 场景舞台里没有任何场景设置钳制范围时按 <see cref="ScenarioCameraBounds.CenterWhenSmaller"/> 处理；主世界里没有时按原版。
    /// 钳制范围变化时（进出区域、换房间、改设置）镜头在 <see cref="BoundsBlendFrames"/> 帧内平滑过渡<br/>
    /// 场景挂了 <see cref="ScenarioArea"/> 时，钳制与缩放只在本地玩家进入区域后生效，<see cref="Source"/> 也只在那时接管（镜头体积）<br/>
    /// 多个生效中的场景都挂了它时逐属性叠加（<see cref="ScenarioComponent.LayerPriority"/>）：钳制范围连同它的区域、房间取自设置了
    /// <see cref="Bounds"/> 的最上层；各自的 <see cref="Source"/> 都会推入镜头栈，按镜头栈规则合成
    /// </summary>
    public class ScenarioCamera : ScenarioComponent
    {
        /// <summary>钳制范围变化时缺省的过渡帧数</summary>
        public const int DefaultBoundsBlendFrames = 30;

        private CameraSource _registered;

        /// <summary>钳制范围，为空表示不改</summary>
        public ScenarioCameraBounds? Bounds { get; set; }

        /// <summary><see cref="ScenarioCameraBounds.Area"/> 用的区域（物块坐标）；为空时取场景的 <see cref="ScenarioArea"/> 外接框，再没有时取整块舞台</summary>
        public Rectangle? Area { get; set; }

        /// <summary><see cref="ScenarioCameraBounds.Rooms"/> 用的房间（物块坐标）；为空时按 <see cref="RoomSize"/> 等分</summary>
        public IReadOnlyList<Rectangle> Rooms { get; set; }

        /// <summary>
        /// <see cref="Rooms"/> 为空时按这个尺寸（物块）把区域等分成房间网格，网格原点取 <see cref="Area"/>、场景区域外接框或舞台左上角
        /// </summary>
        public Point? RoomSize { get; set; }

        /// <summary>钳制范围变化时的过渡帧数，为空时取 <see cref="DefaultBoundsBlendFrames"/>；0 表示立即切换</summary>
        public int? BoundsBlendFrames { get; set; }

        /// <summary>缩放下限：玩家自己调的缩放低于它时抬到它（不低于 1），为空表示不限</summary>
        public float? MinZoom { get; set; }

        /// <summary>缩放上限：玩家自己调的缩放高于它时压到它，为空表示不限</summary>
        public float? MaxZoom { get; set; }

        /// <summary>强制缩放，优先于 <see cref="MinZoom"/> / <see cref="MaxZoom"/>，为空表示不强制。不改玩家设置</summary>
        public float? Zoom { get; set; }

        /// <summary>激活后推入镜头栈、停用时移除的镜头源（固定取景、跟随、轨道等）；为空表示不推</summary>
        public CameraSource Source { get; set; }

        internal bool SetsZoom => Zoom.HasValue || MinZoom.HasValue || MaxZoom.HasValue;

        //Area 模式的区域：显式设置、场景区域外接框，都没有时为空（按整块舞台）
        internal Rectangle? ResolveArea() => Area ?? Scenario?.Area?.Bounds;

        /// <inheritdoc/>
        protected override void OnStart() {
            if (Source == null) {
                return;
            }
            _registered = Scenario?.Area is ScenarioArea area ? new AreaGatedCameraSource(Source, area) : Source;
            VaultCamera.Register(_registered);
        }

        /// <inheritdoc/>
        protected override void OnDeactivate(ScenarioTransition transition) {
            if (_registered != null) {
                VaultCamera.Unregister(_registered);
                _registered = null;
            }
        }

        /// <inheritdoc/>
        protected override string Validate() {
            if (RoomSize is Point size && (size.X <= 0 || size.Y <= 0)) {
                return $"RoomSize {size} has no size";
            }
            if (Zoom is float zoom && zoom <= 0f || MinZoom is float min && min <= 0f || MaxZoom is float max && max <= 0f) {
                return "zoom values must be positive";
            }
            return null;
        }
    }

    //镜头体积：区域场景的镜头源只在本地玩家进入区域后接管，混入混出沿用内层来源自己的帧数
    internal sealed class AreaGatedCameraSource(CameraSource inner, ScenarioArea area) : CameraSource
    {
        public override int Priority => inner.Priority;

        public override int BlendInFrames => inner.BlendInFrames;

        public override int BlendOutFrames => inner.BlendOutFrames;

        public override bool Active => area.Engaged && inner.Active;

        public override void Update(in CameraState below) => inner.Update(below);

        public override bool Evaluate(in CameraState below, out CameraState shot) => inner.Evaluate(below, out shot);

        public override void OnBlendStart(in CameraState current) => inner.OnBlendStart(current);
    }
}
