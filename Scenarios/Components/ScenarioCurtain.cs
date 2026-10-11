using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 转场幕布组件：换舞台前幕布合上（离场），存盘、生成、读档期间由加载画面接着盖住，抵达后幕布拉开（入场）<br/>
    /// 用哪一方的幕布：进入场景用目标场景的，返回主世界用离开的那个场景的，两个场景舞台之间用目标的；那一方没挂时用缺省值
    /// （黑色，合上与拉开各 <see cref="DefaultFrames"/> 帧）。合上与拉开都设为 0 时关闭幕布，加载期间显示原版加载文字<br/>
    /// 离场期间锁住玩家输入、拒绝新的场景请求；玩家在离场期间死亡时取消这次换舞台并拉开幕布
    /// </summary>
    public class ScenarioCurtain : ScenarioComponent
    {
        /// <summary>缺省的合上、拉开帧数</summary>
        public const int DefaultFrames = 20;

        /// <summary>合上用的帧数，0 表示立即合上</summary>
        public int CloseFrames { get; init; } = DefaultFrames;

        /// <summary>拉开用的帧数，0 表示立即拉开</summary>
        public int OpenFrames { get; init; } = DefaultFrames;

        /// <summary>幕布颜色</summary>
        public Color Color { get; init; } = Color.Black;

        /// <summary>幕布是否开启（合上或拉开至少一边有帧数）</summary>
        public bool Enabled => CloseFrames > 0 || OpenFrames > 0;

        /// <summary>
        /// 绘制幕布，SpriteBatch 已按 UI 缩放矩阵开启。缺省按 <paramref name="coverage"/> 铺一层半透明的 <see cref="Color"/>
        /// </summary>
        /// <param name="spriteBatch">已开启的 SpriteBatch</param>
        /// <param name="coverage">遮盖程度，0 为完全拉开，1 为完全合上</param>
        public virtual void Draw(SpriteBatch spriteBatch, float coverage) {
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color * coverage);
        }
    }
}
