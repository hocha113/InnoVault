using Microsoft.Xna.Framework;
using System;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// 用于UI动画的轻量级浮点数包装。<see cref="Current"/>会被基类自动每帧
    /// <see cref="MathHelper.Lerp(float, float, float)"/>逼近<see cref="Target"/>，<br/>
    /// 适合用作面板透明度、滑入进度、悬停高亮等需要平滑过渡的状态量
    /// </summary>
    /// <remarks>
    /// 提供 <see cref="float"/> 的隐式转换运算符，可以在大多数运算和绘制接口中无缝替代普通浮点数<br/>
    /// 例如 <c>Color.White * panelAlpha</c> 这样的写法可以直接传入 <see cref="AnimatedFloat"/> 实例<br/>
    /// <para/>
    /// 之所以以 <see langword="class"/> 形式实现，是为了避免值类型在通过属性返回 / 局部变量赋值时
    /// 被静默拷贝导致 <c>TweenTo</c>、<c>Snap</c> 等 mutate 操作失效的陷阱
    /// </remarks>
    public class AnimatedFloat
    {
        /// <summary>
        /// 默认 Lerp 系数（在 60FPS 下每帧靠近目标的比例）
        /// </summary>
        public const float DefaultSpeed = 0.12f;

        /// <summary>
        /// 默认吸附阈值
        /// </summary>
        public const float DefaultSnapEpsilon = 0.001f;

        /// <summary>
        /// 当前值，会随<see cref="Update"/>逐步逼近<see cref="Target"/>
        /// </summary>
        public float Current;
        /// <summary>
        /// 目标值。修改这个字段即可让<see cref="Current"/>开始向其过渡
        /// </summary>
        public float Target;
        /// <summary>
        /// Lerp 系数，越大过渡越快；为 1 时<see cref="Update"/>会瞬间吸附到目标值<br/>
        /// 该系数定义为 <b>60FPS 下每帧的靠近比例</b>，传入<see cref="Update"/>的<c>frames</c>会做指数补偿，<br/>
        /// 因此在不同帧率下动画的"墙钟速度"保持一致
        /// </summary>
        public float Speed;
        /// <summary>
        /// 当 <c>|Current - Target|</c> 小于此阈值时直接吸附到目标值，避免长尾抖动
        /// </summary>
        public float SnapEpsilon;

        /// <summary>
        /// 创建一个动画浮点数（无参构造也会得到合理默认值，避免 <c>Speed=0</c> 死锁）
        /// </summary>
        public AnimatedFloat() : this(0f, DefaultSpeed, DefaultSnapEpsilon) { }

        /// <summary>
        /// 创建一个动画浮点数
        /// </summary>
        /// <param name="initial">初始值</param>
        /// <param name="speed">Lerp 系数</param>
        /// <param name="snapEpsilon">吸附阈值</param>
        public AnimatedFloat(float initial, float speed = DefaultSpeed, float snapEpsilon = DefaultSnapEpsilon) {
            Current = initial;
            Target = initial;
            Speed = speed;
            SnapEpsilon = snapEpsilon;
        }

        /// <summary>
        /// 推进一次插值。<see cref="Current"/>会以<see cref="Speed"/>为系数靠近<see cref="Target"/><br/>
        /// 当 <paramref name="frames"/> 不等于 1 时会做指数补偿（<c>1-(1-Speed)^frames</c>），从而保证不同帧率下的动画速度保持一致
        /// </summary>
        /// <param name="frames">本次更新代表多少个"60FPS 帧"，由<see cref="UIHandleLoader"/>每帧填入</param>
        public void Update(float frames = 1f) {
            if (Speed <= 0f) {
                return;
            }

            float clampedSpeed = MathHelper.Clamp(Speed, 0f, 1f);
            float t = (frames == 1f || clampedSpeed >= 1f)
                ? clampedSpeed
                : 1f - MathF.Pow(1f - clampedSpeed, MathF.Max(frames, 0f));

            Current = MathHelper.Lerp(Current, Target, t);
            if (Math.Abs(Current - Target) <= SnapEpsilon) {
                Current = Target;
            }
        }

        /// <summary>
        /// 立即将<see cref="Current"/>与<see cref="Target"/>都设为指定值，跳过过渡动画
        /// </summary>
        public void Snap(float value) {
            Current = value;
            Target = value;
        }

        /// <summary>
        /// 设置一个新的目标值，下一次<see cref="Update"/>开始向它过渡
        /// </summary>
        public void TweenTo(float target) => Target = target;

        /// <summary>
        /// 是否已经停止过渡（Current已经吸附到Target）
        /// </summary>
        public bool IsSettled => Current == Target;

        /// <summary>
        /// 隐式转换为<see cref="float"/>，使该结构体能在多数运算与绘制接口中无缝替代普通浮点数<br/>
        /// 当 <paramref name="a"/> 为 <see langword="null"/> 时返回 0，以便和 <c>default</c> / 字段未初始化场景安全配合
        /// </summary>
        public static implicit operator float(AnimatedFloat a) => a is null ? 0f : a.Current;
    }
}
