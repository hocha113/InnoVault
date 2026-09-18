using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 矢量绘图模块自带的着色器
    /// </summary>
    public static class VectorEffects
    {
        /// <summary>
        /// SDF 抗锯齿描边（vs+ps 成对，零分支）：沿条带横向 v 求边缘距离做软边，可选中心提亮与贴图相乘
        /// <br/>参数：<c>transformMatrix</c>、<c>uAA</c>（软边像素）、<c>uGlow</c>、<c>uGlowPower</c>、<c>uTextured</c>（0/1）；贴图在 <c>s0</c>
        /// <br/>由 <see cref="VectorDrawOptions.Antialias"/> 自动选用，也可直接当 <see cref="VectorDrawOptions.Effect"/> 传给任何描边网格
        /// </summary>
        [VaultLoaden("InnoVault/Effects/")]
        public static Asset<Effect> VectorStroke { get; set; }

        /// <summary>SDF 描边着色器是否已加载可用（缺失或编译产物不存在时为 false，渲染器会退回 <see cref="BasicEffect"/>）</summary>
        public static bool SdfStrokeAvailable => VectorStroke != null && VectorStroke.IsLoaded && VectorStroke.Value != null;
    }
}
