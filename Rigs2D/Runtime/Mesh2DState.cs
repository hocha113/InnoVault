using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一张网格件的运行时可变状态，覆盖在 <see cref="Mesh2DDef"/> 的设计值之上
    /// <br/>消费方每帧按需改写（雷闪提亮、整层淡出、换稿），渲染器只读
    /// </summary>
    public struct Mesh2DState
    {
        /// <summary>
        /// 是否绘制
        /// </summary>
        public bool Visible;
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
        /// 绘制排序键（默认取设计层序）
        /// </summary>
        public float SortKey;
        /// <summary>
        /// 整片附加平移（世界像素）
        /// </summary>
        public Vector2 PositionOffset;
        /// <summary>
        /// 贴图覆写，<see langword="null"/> 用设计贴图
        /// </summary>
        public Asset<Texture2D> TextureOverride;

        /// <summary>
        /// 从设计值初始化
        /// </summary>
        public static Mesh2DState FromDef(Mesh2DDef def) => new() {
            Visible = def.Visible,
            TintMul = Color.White,
            DarkMul = 1f,
            AlphaMul = 1f,
            SortKey = def.Layer,
            PositionOffset = Vector2.Zero,
            TextureOverride = null,
        };
    }
}
