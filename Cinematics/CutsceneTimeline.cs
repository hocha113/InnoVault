using System;
using System.Collections.Generic;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 由若干轨道组成的演出时间轴
    /// </summary>
    public sealed class CutsceneTimeline
    {
        private readonly List<CutsceneTrack> tracks = [];

        /// <summary>演出总帧数</summary>
        public int Duration { get; set; }

        /// <summary>按添加顺序执行的轨道集合</summary>
        public IReadOnlyList<CutsceneTrack> Tracks => tracks;

        /// <summary>
        /// 添加一条轨道，并根据轨道结束帧自动扩展总时长
        /// </summary>
        public CutsceneTimeline Add(CutsceneTrack track) {
            ArgumentNullException.ThrowIfNull(track);
            tracks.Add(track);
            Duration = Math.Max(Duration, track.EndTick);
            return this;
        }

        /// <summary>
        /// 在指定帧触发一次事件
        /// </summary>
        public CutsceneTimeline AddEvent(int tick, Action<CutsceneContext> action) => Add(new EventTrack(tick, action));

        internal void OnStart(CutsceneContext context) {
            for (int i = 0; i < tracks.Count; i++) {
                tracks[i].OnTimelineStart(context);
            }
        }

        internal void OnStop(CutsceneContext context) {
            for (int i = 0; i < tracks.Count; i++) {
                tracks[i].OnTimelineStop(context);
            }
        }

        internal void Update(CutsceneContext context) {
            for (int i = 0; i < tracks.Count; i++) {
                tracks[i].UpdateTrack(context);
            }
        }
    }
}
