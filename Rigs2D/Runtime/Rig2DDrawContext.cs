using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 逐件绘制前回调：件即将以 <paramref name="texture"/> 的 <paramref name="frame"/> 矩形绘制。
    /// 用于 Immediate 批次里逐件设置 shader 参数（材质种子、UV 矩形一类）；Deferred 批次下设置不会按件生效，那是调用方的批次责任
    /// </summary>
    /// <param name="rig">正在绘制的实例</param>
    /// <param name="pieceIndex">件索引（同 <see cref="Rig2DInstance.Pieces"/>）</param>
    /// <param name="texture">实际使用的贴图（含覆写）</param>
    /// <param name="frame">帧源矩形</param>
    public delegate void Rig2DPieceDrawHook(Rig2DInstance rig, int pieceIndex, Texture2D texture, Rectangle frame);

    /// <summary>
    /// 一次骨架绘制的环境：视口偏移、光照来源、整体着色、批次参数
    /// <br/>把"画在世界里"与"画在图鉴舞台上"的差异收进一个结构体，渲染器本身不碰 <see cref="Main.screenPosition"/>；
    /// 需要中途 End / Begin 的消费方（加色层、shader 层）从这里取批次矩阵与光栅态，保证与本体同一坐标系
    /// </summary>
    public struct Rig2DDrawContext
    {
        /// <summary>
        /// 从世界坐标减去的视口偏移（世界绘制 = <see cref="Main.screenPosition"/>；舞台坐标 = 零）
        /// </summary>
        public Vector2 ViewOffset;
        /// <summary>
        /// 光照采样委托；<see langword="null"/> 时按 <see cref="WorldLighting"/> 决定走物块光照还是 <see cref="Ambient"/>
        /// </summary>
        public Func<Vector2, Color> Light;
        /// <summary>
        /// <see cref="Light"/> 为空时是否按世界位置取物块光照
        /// </summary>
        public bool WorldLighting;
        /// <summary>
        /// <see cref="Light"/> 为空且不取物块光照时的环境色（剪影模式给黑）
        /// </summary>
        public Color Ambient;
        /// <summary>
        /// 整体着色乘子
        /// </summary>
        public Color Tint;
        /// <summary>
        /// 整体不透明度乘子
        /// </summary>
        public float Alpha;
        /// <summary>
        /// 当前批次的变换矩阵（供消费方 End / Begin 时沿用）
        /// </summary>
        public Matrix BatchMatrix;
        /// <summary>
        /// 当前批次的光栅态（舞台裁剪或 <see cref="Main.Rasterizer"/>）
        /// </summary>
        public RasterizerState Rasterizer;
        /// <summary>
        /// 当前批次的采样态
        /// </summary>
        public SamplerState Sampler;
        /// <summary>
        /// 只画层序键不小于此值的件（默认不限）
        /// </summary>
        public float LayerMin;
        /// <summary>
        /// 只画层序键不大于此值的件（默认不限）
        /// </summary>
        public float LayerMax;
        /// <summary>
        /// 逐件绘制前回调（可空）；见 <see cref="Rig2DPieceDrawHook"/>
        /// </summary>
        public Rig2DPieceDrawHook BeforePiece;
        /// <summary>
        /// 带状件混合态覆盖（可空）：非空时 <see cref="Rig2DRibbonRenderer"/> 忽略定义里的 <c>additive</c>，全部带状件用它重开批次。
        /// 整副骨架的加色 bloom / 残影通道用它把带状件也画成加色（件的混合态本来就跟着调用方开的批次走，不需要这个）
        /// </summary>
        public BlendState RibbonBlendOverride;

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

        /// <summary>
        /// 舞台绘制环境：场景坐标（视口零偏移）、固定环境色、调用方给的批次矩阵与裁剪
        /// </summary>
        /// <param name="matrix">舞台批次矩阵</param>
        /// <param name="ambient">环境光（剪影模式传 <see cref="Color.Black"/>）</param>
        /// <param name="scissor">舞台裁剪光栅态，空则 <see cref="RasterizerState.CullNone"/></param>
        /// <param name="alpha">整体不透明度</param>
        public static Rig2DDrawContext Stage(Matrix matrix, Color ambient, RasterizerState scissor = null, float alpha = 1f) => new() {
            ViewOffset = Vector2.Zero,
            Light = null,
            WorldLighting = false,
            Ambient = ambient,
            Tint = Color.White,
            Alpha = alpha,
            BatchMatrix = matrix,
            Rasterizer = scissor ?? RasterizerState.CullNone,
            Sampler = SamplerState.LinearClamp,
            LayerMin = float.NegativeInfinity,
            LayerMax = float.PositiveInfinity,
        };

        /// <summary>
        /// 取某世界位置的光照色（已含 <see cref="Tint"/> 与 <see cref="Alpha"/>）
        /// </summary>
        public readonly Color LightAt(Vector2 world) {
            Color c;
            if (Light != null) {
                c = Light(world);
            }
            else if (WorldLighting) {
                c = Lighting.GetColor((int)(world.X / 16f), (int)(world.Y / 16f));
            }
            else {
                c = Ambient;
            }
            if (Tint != Color.White) {
                c = c.MultiplyRGBA(Tint);
            }
            return c * Alpha;
        }

        /// <summary>
        /// 取不受光照件在某世界位置的着色：光照项换成白色，但固定环境色（剪影 / 洗色）环境照常生效；含 <see cref="Tint"/> 与 <see cref="Alpha"/>
        /// </summary>
        public readonly Color UnlitAt() {
            Color c = Light != null || WorldLighting ? Color.White : Ambient;
            if (Tint != Color.White) {
                c = c.MultiplyRGBA(Tint);
            }
            return c * Alpha;
        }

        /// <summary>
        /// 复制一份带状件混合态覆盖的环境（<see langword="null"/> 恢复按定义）
        /// </summary>
        public readonly Rig2DDrawContext WithRibbonBlend(BlendState blend) {
            Rig2DDrawContext c = this;
            c.RibbonBlendOverride = blend;
            return c;
        }

        /// <summary>
        /// 复制一份只画指定层序区间的环境
        /// </summary>
        public readonly Rig2DDrawContext Layers(float min, float max) {
            Rig2DDrawContext c = this;
            c.LayerMin = min;
            c.LayerMax = max;
            return c;
        }

        /// <summary>
        /// 复制一份换了着色与不透明度的环境
        /// </summary>
        public readonly Rig2DDrawContext WithTint(Color tint, float alpha) {
            Rig2DDrawContext c = this;
            c.Tint = tint;
            c.Alpha = alpha;
            return c;
        }

        /// <summary>
        /// 复制一份带逐件回调的环境
        /// </summary>
        public readonly Rig2DDrawContext WithPieceHook(Rig2DPieceDrawHook hook) {
            Rig2DDrawContext c = this;
            c.BeforePiece = hook;
            return c;
        }

        /// <summary>
        /// 复制一份固定光照（不采样）的环境：残影 / 洗色层用
        /// </summary>
        public readonly Rig2DDrawContext Flat(Color color, float alpha = 1f) {
            Rig2DDrawContext c = this;
            c.Light = null;
            c.WorldLighting = false;
            c.Ambient = color;
            c.Tint = Color.White;
            c.Alpha = alpha;
            return c;
        }
    }
}
