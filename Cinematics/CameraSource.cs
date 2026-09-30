using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 镜头来源：想接管镜头的一方（角色专属构图、Boss 场地构图、过场演出……）各实现一个，交给 <see cref="VaultCamera.Register"/>。
    /// 镜头栈按 <see cref="Priority"/> 从低到高逐层合成：每一层拿到它下面合成好的镜头（<c>below</c>），给出自己想要的镜头，
    /// 再按自己的混合权重盖上去。<see cref="Active"/> 变真时权重在 <see cref="BlendInFrames"/> 帧内升到 1，变假时在 <see cref="BlendOutFrames"/> 帧内降回 0，
    /// 所以任何来源的进出都是平滑的，下层在变也跟得上
    /// <br/>最底层是原版镜头（含其他模组 <c>ModifyScreenPosition</c> 直接写的结果）；整栈在所有模组的 <c>ModifyScreenPosition</c> 之后才应用，
    /// 直接写 <c>Main.screenPosition</c> 的旧写法照常生效、成为底层
    /// <br/>只在客户端运行；来源对象由登记方持有，卸载时 InnoVault 清空登记
    /// </summary>
    public abstract class CameraSource
    {
        internal float weight;
        internal CameraState lastBelow;

        /// <summary>
        /// 优先级：越大越靠上（后合成、盖在上面）。演出缺省 1000
        /// </summary>
        public virtual int Priority => 0;
        /// <summary>
        /// 激活后权重升满用的帧数（0 = 立即）
        /// </summary>
        public virtual int BlendInFrames => 20;
        /// <summary>
        /// 失活后权重降零用的帧数（0 = 立即）
        /// </summary>
        public virtual int BlendOutFrames => 30;
        /// <summary>
        /// 本帧是否想接管（每个游戏帧读一次）
        /// </summary>
        public abstract bool Active { get; }
        /// <summary>
        /// 当前混合权重（0 ~ 1，未经缓动）
        /// </summary>
        public float Weight => weight;

        /// <summary>
        /// 每个游戏帧（绘制无关，暂停时不跑）调用一次：平滑、跟随一类有时间状态的量在这里推进。
        /// <paramref name="below"/> 是上一帧这一层之下合成好的镜头
        /// </summary>
        public virtual void Update(in CameraState below) { }

        /// <summary>
        /// 给出本层想要的镜头。返回 <see langword="false"/> = 本帧不表态（这一层跳过）
        /// </summary>
        /// <param name="below">下面各层合成好的镜头</param>
        /// <param name="shot">本层想要的镜头</param>
        public abstract bool Evaluate(in CameraState below, out CameraState shot);

        /// <summary>
        /// 权重从 0 开始升起的那一帧调用（<paramref name="current"/> = 上一帧玩家实际看到的镜头）：
        /// 平滑型来源在这里从"当前画面"起步，而不是从某个原始镜头跳过来
        /// </summary>
        public virtual void OnBlendStart(in CameraState current) { }
    }

    /// <summary>
    /// 现成的构图来源：每帧收集主体（<see cref="GatherSubjects"/>），按 <see cref="CameraFraming.Fit"/> 求镜头，再用临界阻尼弹簧平滑。
    /// Boss 场地镜头、多人对峙一类直接继承
    /// </summary>
    public abstract class FramingCameraSource : CameraSource
    {
        private readonly List<CameraSubject> subjects = [];
        private CameraState smoothed;
        private Vector2 centerVelocity;
        private float zoomVelocity;
        private bool seeded;

        /// <summary>主体外留白（世界像素）</summary>
        public float Margin { get; set; } = 96f;
        /// <summary>缩放下限（原版低于 1 露黑边）</summary>
        public float MinZoom { get; set; } = 1f;
        /// <summary>缩放上限</summary>
        public float MaxZoom { get; set; } = 1.6f;
        /// <summary>画面偏移（占可见尺寸比例）</summary>
        public Vector2 Bias { get; set; }
        /// <summary>平滑角频率（rad/s，越大跟得越紧；0 = 不平滑）</summary>
        public float Smoothing { get; set; } = 6f;

        /// <summary>
        /// 收集本帧的构图主体
        /// </summary>
        protected abstract void GatherSubjects(List<CameraSubject> into);

        /// <inheritdoc/>
        public override void OnBlendStart(in CameraState current) {
            smoothed = current;
            centerVelocity = Vector2.Zero;
            zoomVelocity = 0f;
            seeded = true;
        }

        /// <inheritdoc/>
        public override void Update(in CameraState below) {
            subjects.Clear();
            GatherSubjects(subjects);
            if (subjects.Count == 0) {
                return;
            }
            CameraState want = CameraFraming.Fit(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(subjects), VaultCamera.ScreenSize,
                Margin, MinZoom, MaxZoom, VaultCamera.ForcedMinimumZoom, Bias);
            if (!seeded || Smoothing <= 0f) {
                smoothed = want;
                seeded = true;
                return;
            }
            Rigs2D.Solvers.Spring2D.Critical(ref smoothed.Center, ref centerVelocity, want.Center, Smoothing);
            Rigs2D.Solvers.Spring2D.Critical(ref smoothed.Zoom, ref zoomVelocity, want.Zoom, Smoothing);
        }

        /// <inheritdoc/>
        public override bool Evaluate(in CameraState below, out CameraState shot) {
            shot = smoothed;
            return seeded;
        }
    }
}
