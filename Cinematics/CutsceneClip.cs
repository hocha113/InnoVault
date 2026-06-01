using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 一个可注册、可播放的演出片段，内容模组可继承此类来声明具体运镜
    /// </summary>
    public abstract class CutsceneClip : VaultType<CutsceneClip>
    {
        private CutsceneTimeline timeline;

        /// <summary>
        /// 当前演出的播放优先级，已有演出优先级更高时，低优先级演出不会打断它
        /// </summary>
        public virtual int Priority => 0;

        /// <summary>该演出的时间轴</summary>
        public CutsceneTimeline Timeline {
            get {
                EnsureTimeline();
                return timeline;
            }
        }

        /// <summary>该演出的总帧数</summary>
        public int Duration => Timeline.Duration;

        /// <summary>
        /// 播放前检查，返回 <see langword="false"/> 时，导演器会拒绝播放该演出
        /// </summary>
        /// <param name="player">请求播放演出的玩家</param>
        public virtual bool CanPlay(Player player) => player != null && player.active;

        internal virtual bool CanPlayWithSubject(Player player, object subject) => subject == null && CanPlay(player);

        /// <inheritdoc/>
        public override void VaultSetup() {
            timeline = new CutsceneTimeline();
            BuildTimeline(timeline);
        }

        /// <summary>
        /// 派生类在这里向时间轴添加轨道
        /// </summary>
        /// <param name="timeline">即将被缓存的时间轴</param>
        protected abstract void BuildTimeline(CutsceneTimeline timeline);

        private void EnsureTimeline() {
            if (timeline != null) {
                return;
            }

            timeline = new CutsceneTimeline();
            BuildTimeline(timeline);
        }
    }

    /// <summary>
    /// 绑定一个强类型演出主体的演出片段
    /// </summary>
    public abstract class CutsceneClip<TSubject> : CutsceneClip
    {
        /// <summary>
        /// 带演出主体的播放前检查
        /// </summary>
        public virtual bool CanPlay(Player player, TSubject subject) => CanPlay(player);

        internal sealed override bool CanPlayWithSubject(Player player, object subject) {
            return subject is TSubject typedSubject && CanPlay(player, typedSubject);
        }
    }
}
