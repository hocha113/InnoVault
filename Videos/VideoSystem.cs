using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Videos
{
    /// <summary>
    /// 视频模块的帧循环：每帧在任何绘制之前统一取帧（与 Rig2DCanvas 同一时机，FNA 取帧时切换渲染目标不会落进别人的批次），
    /// 窗口失焦立即暂停，离开世界停掉 <see cref="VaultVideoPlayer.StopOnWorldExit"/> 的播放器，卸载时在主线程释放 GPU 资源
    /// </summary>
    internal sealed class VideoSystem : ModSystem
    {
        public override void Load() {
            if (Main.dedServ) {
                return;
            }
            Main.OnPreDraw += TickPlayers;
            Main.instance.Deactivated += OnDeactivated;
        }

        public override void OnWorldUnload() {
            if (!Main.dedServ) {
                VaultVideoPlayer.StopForWorldExit();
            }
        }

        public override void Unload() {
            if (Main.dedServ) {
                return;
            }
            Main.OnPreDraw -= TickPlayers;
            Main.instance.Deactivated -= OnDeactivated;
            VaultVideoPlayer.UnloadAll();
        }

        private static void TickPlayers(GameTime gameTime) => VaultVideoPlayer.TickAll();

        private static void OnDeactivated(object sender, EventArgs e) => VaultVideoPlayer.OnFocusLost();
    }

    /// <summary>
    /// 有播放器开着 <see cref="VaultVideoPlayer.SilenceMusic"/> 时把世界内的背景音乐切成静音
    /// <br/>音乐槽 0 即"无音乐"，与原版音乐音量为 0 时走同一分支；优先级取最高，压过 Boss 音乐
    /// </summary>
    internal sealed class VideoMusicSilence : ModSceneEffect
    {
        public override int Music => 0;

        public override SceneEffectPriority Priority => SceneEffectPriority.BossHigh;

        public override bool IsSceneEffectActive(Player player) => VaultVideoPlayer.AnySilencingMusic;
    }
}
