using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    //游戏宿主：世界绘制环境
    public partial struct Rig2DDrawContext
    {
        /// <summary>
        /// 世界绘制环境：视口 = 屏幕位置，物块光照，默认批次参数
        /// </summary>
        public static Rig2DDrawContext World(float alpha = 1f) => new() {
            ViewOffset = Main.screenPosition,
            Light = null,
            WorldLighting = true,
            Ambient = Color.White,
            Tint = Color.White,
            Alpha = alpha,
            BatchMatrix = Main.GameViewMatrix.TransformationMatrix,
            Rasterizer = Main.Rasterizer,
            Sampler = Main.DefaultSamplerState,
            LayerMin = float.NegativeInfinity,
            LayerMax = float.PositiveInfinity,
        };
    }
}
