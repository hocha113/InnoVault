using InnoVault.Cinematics;
using System;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 演出挂点组件：在场景的几个时刻播放 <see cref="CutsceneClip"/>（填演出的类型，例如 <c>typeof(MyArrivalClip)</c>）<br/>
    /// <see cref="ArrivalClip"/> 在场景开始时（<c>OnStart</c>，换舞台抵达后玩家已出生）播放；
    /// <see cref="DepartureClip"/> 在经换舞台离开这块舞台之前播放，播完或超过 <see cref="DepartureTimeout"/> 帧后幕布才开始合上；
    /// <see cref="AreaEnterClip"/> 在本地玩家进入场景区域时播放
    /// </summary>
    public class ScenarioCinematics : ScenarioComponent
    {
        /// <summary>缺省的离场演出超时帧数</summary>
        public const int DefaultDepartureTimeout = 600;

        /// <summary>场景开始时播放的演出类型，为空表示不播</summary>
        public Type ArrivalClip { get; init; }

        /// <summary>经换舞台离开前播放的演出类型，为空表示不播；只对舞台场景生效</summary>
        public Type DepartureClip { get; init; }

        /// <summary>本地玩家进入场景区域时播放的演出类型，为空表示不播</summary>
        public Type AreaEnterClip { get; init; }

        /// <summary>离场演出最多等待的帧数</summary>
        public int DepartureTimeout { get; init; } = DefaultDepartureTimeout;

        /// <inheritdoc/>
        protected override void OnStart() {
            if (ArrivalClip != null) {
                CutsceneDirector.Play(ArrivalClip);
            }
        }

        /// <inheritdoc/>
        protected override string Validate() {
            foreach (Type type in new[] { ArrivalClip, DepartureClip, AreaEnterClip }) {
                if (type != null && !typeof(CutsceneClip).IsAssignableFrom(type)) {
                    return $"{type.FullName} is not a CutsceneClip";
                }
            }
            return null;
        }
    }
}
