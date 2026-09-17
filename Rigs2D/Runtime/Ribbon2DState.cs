using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一条带状件的运行时可变状态，覆盖在 <see cref="Ribbon2DDef"/> 的设计值之上
    /// <br/>消费方每帧按需改写（受击变宽、蓄力变亮、纹理滚动、换色贴图），渲染器只读
    /// </summary>
    public struct Ribbon2DState
    {
        /// <summary>
        /// 是否绘制
        /// </summary>
        public bool Visible;
        /// <summary>
        /// 宽度乘子
        /// </summary>
        public float WidthMul;
        /// <summary>
        /// 额外着色乘子
        /// </summary>
        public Color TintMul;
        /// <summary>
        /// 额外 RGB 压暗乘子
        /// </summary>
        public float DarkMul;
        /// <summary>
        /// 额外不透明度乘子
        /// </summary>
        public float AlphaMul;
        /// <summary>
        /// 纹理沿链方向的 u 偏移（单位：贴图宽；每帧递增即可做滚动）
        /// </summary>
        public float UvOffset;
        /// <summary>
        /// 绘制排序键（默认取设计层序）
        /// </summary>
        public float SortKey;
        /// <summary>
        /// 贴图覆写，<see langword="null"/> 用设计贴图
        /// </summary>
        public Asset<Texture2D> TextureOverride;

        /// <summary>
        /// 从设计值初始化
        /// </summary>
        public static Ribbon2DState FromDef(Ribbon2DDef def) => new() {
            Visible = def.Visible,
            WidthMul = 1f,
            TintMul = Color.White,
            DarkMul = 1f,
            AlphaMul = 1f,
            UvOffset = 0f,
            SortKey = def.Layer,
            TextureOverride = null,
        };
    }
}
