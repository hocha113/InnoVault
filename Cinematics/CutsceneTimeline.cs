using System;
using System.Collections.Generic;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 由若干轨道组成的演出时间轴
    /// <br/>时钟两种：缺省内部时钟逐帧 +1（<see cref="HoldWhile"/> 为真时停、<see cref="AddWait"/> 的门没开时停）；
    /// <see cref="UseClock"/> 换成外部时钟后每帧直接读外部帧号（例如 Boss 已同步的入场计时：顿帧时它停，演出跟着停；
    /// 联机各端读同一个同步量，演出节拍对齐）。事件轨道按"帧号越过即触发"，外部时钟跳帧也不会漏
    /// </summary>
    public sealed class CutsceneTimeline
    {
        private readonly List<CutsceneTrack> tracks = [];
        private readonly List<WaitTrack> waits = [];

        /// <summary>演出总帧数</summary>
        public int Duration { get; set; }

        /// <summary>按添加顺序执行的轨道集合</summary>
        public IReadOnlyList<CutsceneTrack> Tracks => tracks;

        /// <summary>外部时钟（空 = 内部逐帧时钟）</summary>
        public Func<CutsceneContext, int> Clock { get; set; }

        /// <summary>内部时钟的暂停条件（顿帧、全场时停时返回真）</summary>
        public Func<CutsceneContext, bool> HoldCondition { get; set; }

        /// <summary>
        /// 添加一条轨道，并根据轨道结束帧自动扩展总时长
        /// </summary>
        public CutsceneTimeline Add(CutsceneTrack track) {
            ArgumentNullException.ThrowIfNull(track);
            tracks.Add(track);
            if (track is WaitTrack wait) {
                waits.Add(wait);
            }
            Duration = Math.Max(Duration, track.EndTick);
            return this;
        }

        /// <summary>
        /// 在指定帧触发一次事件
        /// </summary>
        public CutsceneTimeline AddEvent(int tick, Action<CutsceneContext> action) => Add(new EventTrack(tick, action));

        /// <summary>
        /// 在 <paramref name="tick"/> 设一道门：内部时钟走到这一帧后停住，直到 <paramref name="until"/> 为真（斧落地、法相躺稳），
        /// 或等满 <paramref name="timeout"/> 帧（超时调 <paramref name="onTimeout"/>，演出照常往下走——前置条件过期时的退路）。
        /// 停住期间各轨道仍按这一帧更新（镜头照常跟随）。外部时钟下门不起作用（节拍由时钟的主人决定）
        /// </summary>
        public CutsceneTimeline AddWait(int tick, Func<CutsceneContext, bool> until, int timeout, Action<CutsceneContext> onTimeout = null)
            => Add(new WaitTrack(tick, until, timeout, onTimeout));

        /// <summary>
        /// 换成外部时钟：每帧 <see cref="CutsceneContext.Tick"/> = <paramref name="clock"/>(上下文)
        /// </summary>
        public CutsceneTimeline UseClock(Func<CutsceneContext, int> clock) {
            Clock = clock;
            return this;
        }

        /// <summary>
        /// 内部时钟的暂停条件
        /// </summary>
        public CutsceneTimeline HoldWhile(Func<CutsceneContext, bool> hold) {
            HoldCondition = hold;
            return this;
        }

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

        //当前帧有没有没开的门（会顺带推进等待计数、处理超时）
        internal bool Blocked(CutsceneContext context) {
            for (int i = 0; i < waits.Count; i++) {
                if (waits[i].Blocks(context)) {
                    return true;
                }
            }
            return false;
        }
    }
}
