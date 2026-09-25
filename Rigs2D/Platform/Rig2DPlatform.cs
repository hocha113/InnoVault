using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D
{
    /// <summary>
    /// Rigs2D 核心与宿主之间的全部接缝：日志、时钟、服务端判定、主线程投递、物块光照、物块碰撞、默认地面探测、调试像素与文字、步进名册
    /// <br/>核心（Data / Runtime / Solvers / Animation / Platform）只经这里碰宿主。游戏里由 <c>Rigs2D/Tml</c> 的分部实现
    /// 在静态构造时装好；离线宿主（VisualSandbox 一类不带 tModLoader 的程序）在启动时自行赋值，未赋值的项都有无害的缺省行为
    /// </summary>
    public static partial class Rig2DPlatform
    {
        static Rig2DPlatform() {
            InstallHost();
        }

        /// <summary>
        /// 宿主装配点：游戏构建里由 <c>Rigs2D/Tml/Rig2DPlatform.Tml.cs</c> 实现；离线构建没有实现，调用在编译期消失
        /// </summary>
        static partial void InstallHost();

        /// <summary>
        /// 错误日志：(去重键, 消息)。缺省丢弃
        /// </summary>
        public static Action<string, string> ErrorSink { get; set; }
        /// <summary>
        /// 普通日志。缺省丢弃
        /// </summary>
        public static Action<string> InfoSink { get; set; }
        /// <summary>
        /// 宿主帧号（游戏里是 <c>Main.GameUpdateCount</c>）。缺省恒 0
        /// </summary>
        public static Func<uint> TickSource { get; set; }
        /// <summary>
        /// 是否运行在专用服务器上（服务器不取贴图、不登记热重载）。缺省为假
        /// </summary>
        public static Func<bool> ServerCheck { get; set; }
        /// <summary>
        /// 把动作投递到主线程（GPU 资源释放用）。缺省就地执行
        /// </summary>
        public static Action<Action> MainThreadQueue { get; set; }
        /// <summary>
        /// 世界位置的物块光照色（<see cref="Rig2DDrawContext.WorldLighting"/> 用）。缺省白色
        /// </summary>
        public static Func<Vector2, Color> TileLight { get; set; }
        /// <summary>
        /// 物块碰撞：(质点位置, 本步位移) → 物块允许的位移（<see cref="VerletStrandSolver"/> 的 <c>tileCollide</c> 用）。缺省不挡
        /// </summary>
        public static Func<Vector2, Vector2, Vector2> TileCollide { get; set; }
        /// <summary>
        /// 步态没有给 <c>Probe</c> 时的默认地面探测（游戏里是物块射线）。缺省探不到地
        /// </summary>
        public static Rig2DGroundProbe GroundProbe { get; set; }
        /// <summary>
        /// 调试叠层用的纯白像素贴图。缺省无（调试绘制跳过）
        /// </summary>
        public static Func<Texture2D> DebugPixel { get; set; }
        /// <summary>
        /// 调试叠层的描边小字：(批次, 文字, 位置, 颜色, 缩放)。缺省不画
        /// </summary>
        public static Action<SpriteBatch, string, Vector2, Color, float> DebugText { get; set; }
        /// <summary>
        /// 每个实例 <see cref="Rig2DInstance.Step"/> 末尾的通知（调试叠层名册）。缺省无
        /// </summary>
        public static Action<Rig2DInstance> Stepped { get; set; }

        /// <summary>
        /// 记一条错误（同键短时间内由宿主去重）
        /// </summary>
        public static void LogError(string key, string message) => ErrorSink?.Invoke(key, message);

        /// <summary>
        /// 记一条普通日志
        /// </summary>
        public static void LogInfo(string message) => InfoSink?.Invoke(message);

        /// <summary>
        /// 当前宿主帧号
        /// </summary>
        public static uint Tick => TickSource?.Invoke() ?? 0u;

        /// <summary>
        /// 是否专用服务器
        /// </summary>
        public static bool IsServer => ServerCheck?.Invoke() ?? false;

        /// <summary>
        /// 投递到主线程执行；宿主没接时就地执行
        /// </summary>
        public static void QueueMainThread(Action action) {
            if (action == null) {
                return;
            }
            if (MainThreadQueue != null) {
                MainThreadQueue(action);
            }
            else {
                action();
            }
        }
    }
}
