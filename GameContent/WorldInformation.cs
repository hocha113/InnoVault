using InnoVault.GameSystem;
using Terraria.ModLoader.IO;

namespace InnoVault.GameContent
{
    /// <summary>
    /// 一个用于记录简单世界信息的基类，继承它用于进行更好的交叉代码适配
    /// </summary>
    public class WorldInformation : SaveWorld
    {
        /// <summary>
        /// 世界进入次数，不分存档
        /// </summary>
        public static uint EnterCount { get; set; }
        /// <inheritdoc/>
        public override void SaveData(TagCompound tag) {
            tag[nameof(EnterCount)] = EnterCount;
        }
        /// <inheritdoc/>
        public override void LoadData(TagCompound tag) {
            if (tag.TryGet(nameof(EnterCount), out uint enterCount)) {
                EnterCount = enterCount;
            }
            else {
                EnterCount = 0;
            }
        }
    }
}
