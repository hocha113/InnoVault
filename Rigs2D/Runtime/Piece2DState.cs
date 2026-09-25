using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一件贴图的运行时可变状态，覆盖在 <see cref="Piece2DDef"/> 的设计值之上
    /// <br/>消费方每帧按需改写（张钳角、尾扇张合缩放、走地权重排序、换色贴图），渲染器只读
    /// </summary>
    public struct Piece2DState
    {
        /// <summary>
        /// 是否绘制
        /// </summary>
        public bool Visible;
        /// <summary>
        /// 是否水平镜像（锚点与轴角一并镜像）
        /// </summary>
        public bool Mirror;
        /// <summary>
        /// 额外缩放乘子（贴图坐标系），与设计缩放、骨架 Scale 相乘
        /// </summary>
        public Vector2 ScaleMul;
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
        /// 附加旋转（弧度，绕近端锚点；钳口开合、下巴张合用）
        /// </summary>
        public float ExtraRotation;
        /// <summary>
        /// 绘制排序键（默认取设计层序；需要连续过渡层次时逐帧改写）
        /// </summary>
        public float SortKey;
        /// <summary>
        /// 多帧图集当前帧
        /// </summary>
        public int Frame;
        /// <summary>
        /// 贴图覆写（换色 / 换稿），<see langword="null"/> 用设计贴图
        /// </summary>
        public Asset<Texture2D> TextureOverride;
        /// <summary>
        /// 绘制位置附加偏移（世界像素；落步下沉等"只动画不动骨"的场合）
        /// </summary>
        public Vector2 PositionOffset;
        /// <summary>
        /// 按角换帧的当前桶（迟滞判定用；<c>-1</c> = 尚未判过，下一次直接取当前桶）
        /// </summary>
        public int FrameBucket;

        /// <summary>
        /// 从设计值初始化
        /// </summary>
        public static Piece2DState FromDef(Piece2DDef def) => new() {
            Visible = def.Visible,
            Mirror = def.Mirror,
            ScaleMul = Vector2.One,
            TintMul = Color.White,
            DarkMul = 1f,
            AlphaMul = 1f,
            ExtraRotation = 0f,
            SortKey = def.Layer,
            Frame = 0,
            TextureOverride = null,
            PositionOffset = Vector2.Zero,
            FrameBucket = -1,
        };
    }
}
