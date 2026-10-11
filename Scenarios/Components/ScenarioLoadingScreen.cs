using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 换舞台进行到哪一步
    /// </summary>
    public enum ScenarioTravelPhase
    {
        /// <summary>没有在切换</summary>
        None,
        /// <summary>离场：离场演出播放、幕布合上，旧舞台仍在运行，玩家输入被锁住</summary>
        Departing,
        /// <summary>正在保存离开的舞台</summary>
        Saving,
        /// <summary>正在生成场景舞台</summary>
        Generating,
        /// <summary>正在读取目标舞台</summary>
        Loading,
        /// <summary>入场：玩家已出生在新舞台里，幕布正在拉开</summary>
        Arriving,
    }

    /// <summary>
    /// 加载画面绘制时可以读取的信息
    /// </summary>
    public readonly struct ScenarioLoadingContext
    {
        internal ScenarioLoadingContext(ScenarioTransition transition, ScenarioTravelPhase phase, string status,
            double? overallProgress, double? passProgress, string passMessage, int frames) {
            Transition = transition;
            Phase = phase;
            Status = status;
            OverallProgress = overallProgress;
            PassProgress = passProgress;
            PassMessage = passMessage;
            Frames = frames;
        }

        /// <summary>正在进行的换舞台</summary>
        public ScenarioTransition Transition { get; }

        /// <summary>当前步骤</summary>
        public ScenarioTravelPhase Phase { get; }

        /// <summary>原版加载文字（<c>Main.statusText</c>）</summary>
        public string Status { get; }

        /// <summary>生成阶段的总进度（0–1），不在生成时为空</summary>
        public double? OverallProgress { get; }

        /// <summary>生成阶段当前生成步骤的进度（0–1），不在生成时为空</summary>
        public double? PassProgress { get; }

        /// <summary>生成阶段当前生成步骤的说明文字，不在生成时为空</summary>
        public string PassMessage { get; }

        /// <summary>加载画面已经显示的帧数</summary>
        public int Frames { get; }
    }

    /// <summary>
    /// 加载画面组件：进入挂了它的舞台场景、或从它离开回到主世界时，在存盘、生成、读档期间代替原版加载文字铺满屏幕<br/>
    /// 进入时用目标场景的，目标是主世界时用离开的那个场景的；两边都没挂、幕布又开启时用框架的缺省加载画面（黑底、加载文字、生成进度条）。
    /// 绘制时 SpriteBatch 已按 UI 缩放矩阵开启
    /// </summary>
    public abstract class ScenarioLoadingScreen : ScenarioComponent
    {
        /// <summary>加载期间播放的音乐槽位，-1 表示不改</summary>
        public virtual int Music => -1;

        /// <summary>每帧绘制加载画面，画面需要自己铺满屏幕</summary>
        /// <param name="spriteBatch">已开启的 SpriteBatch</param>
        /// <param name="context">切换进度信息</param>
        public abstract void Draw(SpriteBatch spriteBatch, in ScenarioLoadingContext context);
    }
}
