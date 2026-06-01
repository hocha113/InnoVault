using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 时间轴轨道每帧接收的运行时上下文
    /// </summary>
    public sealed class CutsceneContext
    {
        internal CutsceneContext(CutsceneClip clip, Player player, CutsceneCameraRuntime camera, object tag) {
            Clip = clip;
            Player = player;
            Camera = camera;
            Tag = tag;
        }

        /// <summary>当前正在播放的演出定义</summary>
        public CutsceneClip Clip { get; internal set; }

        /// <summary>演出绑定的本地玩家</summary>
        public Player Player { get; internal set; }

        /// <summary>全局摄像机运行时</summary>
        public CutsceneCameraRuntime Camera { get; }

        /// <summary>调用方传入的自定义上下文对象，例如 Actor、NPC 或剧情数据</summary>
        public object Tag { get; internal set; }

        /// <summary>当前播放帧，从 0 开始</summary>
        public int Tick { get; internal set; }

        /// <summary>当前演出总帧数</summary>
        public int Duration { get; internal set; }

        /// <summary>当前播放进度，范围为 0 到 1</summary>
        public float Progress => Duration <= 0 ? 1f : MathHelper.Clamp(Tick / (float)Duration, 0f, 1f);

        /// <summary>当前帧的玩家中心，玩家无效时返回零向量</summary>
        public Vector2 PlayerCenter => Player != null && Player.active ? Player.Center : Vector2.Zero;

        /// <summary>
        /// 尝试将 <see cref="Tag"/> 转换成指定类型
        /// </summary>
        public bool TryGetTag<T>(out T value) {
            if (Tag is T typedValue) {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }
    }
}
