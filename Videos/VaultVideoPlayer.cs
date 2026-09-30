using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Terraria;
using FnaVideo = Microsoft.Xna.Framework.Media.Video;
using FnaVideoPlayer = Microsoft.Xna.Framework.Media.VideoPlayer;

namespace InnoVault.Videos
{
    /// <summary>
    /// 视频音轨跟随的游戏音量滑条
    /// </summary>
    public enum VideoVolumeChannel
    {
        /// <summary>音效音量（默认）：关掉音乐的玩家仍听得到过场里的台词与音效</summary>
        Sound,
        /// <summary>音乐音量</summary>
        Music,
        /// <summary>环境音量</summary>
        Ambient,
        /// <summary>不乘游戏音量，只用 <see cref="VaultVideoPlayer.Volume"/></summary>
        None,
    }

    /// <summary>
    /// <see cref="VaultVideo"/> 的播放器：包装 FNA 自带的 <see cref="FnaVideoPlayer"/>（Theora 解码 + GPU 上的 YUV 转 RGB + 音轨输出）
    /// <br/>用法：<c>player.Play(video)</c>，之后在任意 SpriteBatch 里 <see cref="DrawFit"/> / <see cref="Draw"/> 或直接取 <see cref="Texture"/>；
    /// 取帧由 InnoVault 在每帧任何绘制之前统一完成，调用方不需要自己驱动
    /// <br/>音量乘上 <see cref="VolumeChannel"/> 对应的游戏滑条；游戏暂停、窗口失焦时自动暂停（<see cref="PauseWhenGamePaused"/> / <see cref="PauseWhenUnfocused"/>）；
    /// <see cref="SilenceMusic"/> 在世界里把背景音乐切成静音；离开世界时默认停止（<see cref="StopOnWorldExit"/>）
    /// <br/>成本：解码在主线程同步进行，每出一帧新画面付一次解码与上传（见 <see cref="Stats"/>）；只能从头播，不能跳转；画面不带透明通道
    /// <br/>控制方法应在主线程调用，其它线程的调用会被排到主线程执行；专用服务器上全部为空操作；不再使用时 <see cref="Dispose"/> 释放渲染目标
    /// </summary>
    public sealed class VaultVideoPlayer : IDisposable
    {
        //正在播放或暂停的播放器，每帧统一取帧
        private static readonly List<VaultVideoPlayer> active = [];
        //持有 FNA 播放器（渲染目标、着色器、顶点缓冲）的实例，卸载时统一释放
        private static readonly HashSet<VaultVideoPlayer> owners = [];

        private FnaVideoPlayer fnaPlayer;
        private FnaVideo fnaVideo;
        private bool isLooped;
        private bool userPaused;
        private bool autoPaused;
        private bool disposed;
        private int lastFrameIndex = -1;
        private float appliedVolume = -1f;
        private bool appliedMute;

        /// <summary>当前（或最近一次）播放的视频</summary>
        public VaultVideo Video { get; private set; } = VaultVideo.Empty;
        /// <summary>播放状态；自动暂停不改变它，另见 <see cref="IsAutoPaused"/></summary>
        public MediaState State { get; private set; } = MediaState.Stopped;
        /// <summary>是否因游戏暂停或窗口失焦被自动暂停</summary>
        public bool IsAutoPaused => autoPaused;
        /// <summary>非循环播放是否已自然播完；播完后 <see cref="Texture"/> 保留最后一帧，直到下一次 <see cref="Play"/> 或 <see cref="Stop"/></summary>
        public bool IsFinished { get; private set; }
        /// <summary>最近一次 <see cref="Play"/> 失败或播放中出错的原因</summary>
        public string LastError { get; private set; }
        /// <summary>取帧耗时统计，每次 <see cref="Play"/> 清零</summary>
        public VideoPlaybackStats Stats { get; } = new();
        /// <summary>最新一帧画面（FNA 播放器的渲染目标）；还没有画面时为空。不要释放它，也不要跨 <see cref="Play"/> 缓存</summary>
        public Texture2D Texture { get; private set; }
        /// <summary>当前是否有可画的画面</summary>
        public bool HasFrame => Texture != null && !Texture.IsDisposed;

        /// <summary>是否循环播放</summary>
        public bool IsLooped {
            get => isLooped;
            set {
                isLooped = value;
                if (fnaPlayer != null) {
                    fnaPlayer.IsLooped = value;
                }
            }
        }
        /// <summary>音量倍率（0~1），再乘上 <see cref="VolumeChannel"/> 对应的游戏音量</summary>
        public float Volume { get; set; } = 1f;
        /// <summary>音轨跟随的游戏音量滑条，默认 <see cref="VideoVolumeChannel.Sound"/></summary>
        public VideoVolumeChannel VolumeChannel { get; set; } = VideoVolumeChannel.Sound;
        /// <summary>是否静音</summary>
        public bool IsMuted { get; set; }
        /// <summary>单人游戏暂停时是否自动暂停，默认是</summary>
        public bool PauseWhenGamePaused { get; set; } = true;
        /// <summary>游戏窗口失焦时是否自动暂停，默认是（否则切回来时要一次补解失焦期间的全部帧）</summary>
        public bool PauseWhenUnfocused { get; set; } = true;
        /// <summary>播放期间是否把世界内的背景音乐切成静音，默认否；主菜单音乐不受影响</summary>
        public bool SilenceMusic { get; set; }
        /// <summary>离开世界时是否自动停止，默认是</summary>
        public bool StopOnWorldExit { get; set; } = true;

        /// <summary>当前播放位置；停止时为 0</summary>
        public TimeSpan Position => State == MediaState.Stopped || fnaPlayer == null ? TimeSpan.Zero : fnaPlayer.PlayPosition;
        /// <summary>播放进度（0~1）；时长未知时为 0，自然播完为 1</summary>
        public float Progress {
            get {
                if (IsFinished) {
                    return 1f;
                }
                double total = Video.Duration.TotalSeconds;
                return total <= 0 ? 0f : (float)Math.Clamp(Position.TotalSeconds / total, 0, 1);
            }
        }

        /// <summary>非循环播放自然播完时触发（在主线程，绘制开始之前）</summary>
        public event Action<VaultVideoPlayer> Finished;

        /// <summary>当前正在播放或暂停的播放器数量</summary>
        public static int ActiveCount => active.Count;

        internal static bool AnySilencingMusic {
            get {
                foreach (VaultVideoPlayer player in active) {
                    if (player.SilenceMusic) {
                        return true;
                    }
                }
                return false;
            }
        }

        #region 控制

        /// <summary>
        /// 从头播放一段视频，会先停掉正在播的内容；失败时 <see cref="State"/> 保持 <see cref="MediaState.Stopped"/>，原因见 <see cref="LastError"/>
        /// </summary>
        /// <param name="video">要播放的视频</param>
        /// <param name="loop">是否循环</param>
        public void Play(VaultVideo video, bool loop = false) {
            if (disposed || Main.dedServ || DeferToMainThread(() => Play(video, loop))) {
                return;
            }
            StopPlayback();
            Texture = null;
            Video = video ?? VaultVideo.Empty;
            isLooped = loop;
            IsFinished = false;
            LastError = null;
            Stats.Reset();
            lastFrameIndex = -1;
            if (Video.IsEmpty) {
                LastError = Video.Error ?? "empty video";
                return;
            }
            if (!Video.EnsureFile()) {
                LastError = "video file is missing: " + Video.FilePath;
                return;
            }
            try {
                if (fnaPlayer == null) {
                    fnaPlayer = new FnaVideoPlayer();
                    owners.Add(this);
                }
                fnaVideo = OpenFnaVideo(Video.FilePath);
                fnaPlayer.IsLooped = loop;
                ApplyVolume(true);
                fnaPlayer.Play(fnaVideo);
                State = MediaState.Playing;
                active.Add(this);
            } catch (Exception ex) {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                VaultMod.LoggerError("VaultVideoPlayer.Play:" + Video.Name, $"[Video] failed to start {Video.Name}: {LastError}");
                StopPlayback();
            }
        }

        /// <summary>暂停</summary>
        public void Pause() {
            if (disposed || DeferToMainThread(Pause) || State != MediaState.Playing) {
                return;
            }
            userPaused = true;
            State = MediaState.Paused;
            SyncFnaPause();
        }

        /// <summary>从暂停处继续</summary>
        public void Resume() {
            if (disposed || DeferToMainThread(Resume) || State != MediaState.Paused) {
                return;
            }
            userPaused = false;
            State = MediaState.Playing;
            SyncFnaPause();
        }

        /// <summary>停止并清掉画面</summary>
        public void Stop() {
            if (disposed || DeferToMainThread(Stop)) {
                return;
            }
            StopPlayback();
            Texture = null;
            IsFinished = false;
        }

        /// <summary>停止并释放 FNA 播放器持有的渲染目标与着色器；之后不能再播放</summary>
        public void Dispose() {
            if (disposed || DeferToMainThread(Dispose)) {
                return;
            }
            DisposeNow();
        }

        #endregion

        #region 绘制

        /// <summary>把画面按比例放进 <paramref name="area"/> 后的目标矩形</summary>
        /// <param name="area">可用区域</param>
        /// <param name="cover">真：铺满区域（超出部分由 <see cref="DrawFit"/> 裁掉）；假：完整显示（留黑边）</param>
        public Rectangle Fit(Rectangle area, bool cover = false) {
            int w = HasFrame ? Texture.Width : Video.Width;
            int h = HasFrame ? Texture.Height : Video.Height;
            if (w <= 0 || h <= 0) {
                return area;
            }
            float sx = area.Width / (float)w;
            float sy = area.Height / (float)h;
            float scale = cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
            int dw = (int)MathF.Round(w * scale);
            int dh = (int)MathF.Round(h * scale);
            return new Rectangle(area.X + (area.Width - dw) / 2, area.Y + (area.Height - dh) / 2, dw, dh);
        }

        /// <summary>把画面拉伸画到目标矩形；没有画面时不画</summary>
        public void Draw(SpriteBatch spriteBatch, Rectangle destination, Color color) {
            if (HasFrame) {
                spriteBatch.Draw(Texture, destination, color);
            }
        }

        /// <summary>按比例把画面画进区域；没有画面时不画</summary>
        /// <param name="spriteBatch">已开始的批次</param>
        /// <param name="area">可用区域</param>
        /// <param name="color">染色与透明度</param>
        /// <param name="cover">真：铺满区域并裁掉超出部分；假：完整显示（留黑边）</param>
        public void DrawFit(SpriteBatch spriteBatch, Rectangle area, Color color, bool cover = false) {
            if (!HasFrame) {
                return;
            }
            if (!cover) {
                spriteBatch.Draw(Texture, Fit(area), color);
                return;
            }
            float scale = Math.Max(area.Width / (float)Texture.Width, area.Height / (float)Texture.Height);
            int sw = Math.Min(Texture.Width, (int)MathF.Round(area.Width / scale));
            int sh = Math.Min(Texture.Height, (int)MathF.Round(area.Height / scale));
            spriteBatch.Draw(Texture, area, new Rectangle((Texture.Width - sw) / 2, (Texture.Height - sh) / 2, sw, sh), color);
        }

        #endregion

        #region 帧循环（由 VideoSystem 驱动）

        //每帧在任何绘制之前调用：取帧里 FNA 会切一次渲染目标，放在这里不会落进别人的批次
        internal static void TickAll() {
            if (active.Count == 0) {
                return;
            }
            bool gamePaused = !Main.gameMenu && Main.gamePaused;
            bool unfocused = !Main.instance.IsActive;
            //倒序：播完的播放器会把自己移出列表，Finished 回调里也可能停掉别的播放器
            for (int i = active.Count - 1; i >= 0; i--) {
                if (i >= active.Count) {
                    continue;
                }
                VaultVideoPlayer player = active[i];
                try {
                    player.Tick(gamePaused, unfocused);
                } catch (Exception ex) {
                    player.LastError = $"{ex.GetType().Name}: {ex.Message}";
                    VaultMod.LoggerError("VaultVideoPlayer.Tick:" + player.Video.Name, $"[Video] playback of {player.Video.Name} failed and was stopped: {ex}");
                    player.StopPlayback();
                }
            }
        }

        //失焦事件在下一次绘制之前到达：立刻暂停，免得最小化期间计时器照走
        internal static void OnFocusLost() {
            bool gamePaused = !Main.gameMenu && Main.gamePaused;
            foreach (VaultVideoPlayer player in active) {
                player.UpdateAutoPause(gamePaused, true);
            }
        }

        internal static void StopForWorldExit() {
            for (int i = active.Count - 1; i >= 0; i--) {
                if (i < active.Count && active[i].StopOnWorldExit) {
                    active[i].Stop();
                }
            }
        }

        internal static void UnloadAll() {
            VaultVideoPlayer[] players = [.. owners];
            active.Clear();
            owners.Clear();
            if (Main.dedServ || players.Length == 0) {
                return;
            }
            Main.QueueMainThreadAction(() => {
                foreach (VaultVideoPlayer player in players) {
                    player.DisposeNow();
                }
            });
        }

        private void Tick(bool gamePaused, bool unfocused) {
            UpdateAutoPause(gamePaused, unfocused);
            ApplyVolume(false);
            //与 FNA 取帧时的判断同式：计时器越过下一帧的时刻才会解码
            int frameIndex = (int)(fnaPlayer.PlayPosition.TotalMilliseconds * Video.FramesPerSecond / 1000.0);
            if (frameIndex < lastFrameIndex) {
                lastFrameIndex = -1;
                Stats.Loops++;
            }
            bool decodes = frameIndex > lastFrameIndex && fnaPlayer.State == MediaState.Playing;
            long start = Stopwatch.GetTimestamp();
            Texture = fnaPlayer.GetTexture();
            Stats.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, decodes);
            if (decodes) {
                lastFrameIndex = frameIndex;
            }
            if (fnaPlayer.State == MediaState.Stopped) {
                //非循环播放自然结束：FNA 已经停下，这里关掉解码器，画面保留最后一帧
                StopPlayback();
                IsFinished = true;
                Finished?.Invoke(this);
            }
        }

        #endregion

        #region 内部

        private static bool DeferToMainThread(Action action) {
            if (Program.IsMainThread) {
                return false;
            }
            Main.QueueMainThreadAction(action);
            return true;
        }

        private void UpdateAutoPause(bool gamePaused, bool unfocused) {
            bool pause = (PauseWhenGamePaused && gamePaused) || (PauseWhenUnfocused && unfocused);
            if (pause != autoPaused) {
                autoPaused = pause;
                SyncFnaPause();
            }
        }

        private void SyncFnaPause() {
            if (fnaPlayer == null || fnaVideo == null) {
                return;
            }
            bool pause = userPaused || autoPaused;
            if (pause && fnaPlayer.State == MediaState.Playing) {
                fnaPlayer.Pause();
            }
            else if (!pause && fnaPlayer.State == MediaState.Paused) {
                fnaPlayer.Resume();
            }
        }

        private void ApplyVolume(bool force) {
            float channel = VolumeChannel switch {
                VideoVolumeChannel.Sound => Main.soundVolume,
                VideoVolumeChannel.Music => Main.musicVolume,
                VideoVolumeChannel.Ambient => Main.ambientVolume,
                _ => 1f,
            };
            float volume = MathHelper.Clamp(Volume * channel, 0f, 1f);
            if (force || volume != appliedVolume) {
                fnaPlayer.Volume = volume;
                appliedVolume = volume;
            }
            if (force || IsMuted != appliedMute) {
                fnaPlayer.IsMuted = IsMuted;
                appliedMute = IsMuted;
            }
        }

        //顺序不能反：FNA 的 Stop 会对当前解码器句柄调 tf_reset，句柄先关掉就是把空指针交给原生层
        private void StopPlayback() {
            active.Remove(this);
            userPaused = false;
            autoPaused = false;
            State = MediaState.Stopped;
            if (fnaPlayer != null && fnaPlayer.State != MediaState.Stopped) {
                fnaPlayer.Stop();
            }
            CloseFnaVideo();
        }

        private void DisposeNow() {
            if (disposed) {
                return;
            }
            disposed = true;
            StopPlayback();
            Texture = null;
            owners.Remove(this);
            fnaPlayer?.Dispose();
            fnaPlayer = null;
        }

        //直接走 FNA 的内部构造：FromUriEXT 要先把路径转成 Uri，路径里的 # 与 % 会被当成 URI 语法
        private static FnaVideo OpenFnaVideo(string path) {
            GraphicsDevice device = Main.instance.GraphicsDevice;
            try {
                return CreateFnaVideo(path, device);
            } catch (MissingMemberException) {
                return FnaVideo.FromUriEXT(new Uri(path), device);
            }
        }

        //FNA 的 Video 没有 Dispose，解码器与文件句柄要等终结器才关；这里在停止后立刻关掉，终结器见到空句柄会跳过
        private void CloseFnaVideo() {
            FnaVideo video = fnaVideo;
            fnaVideo = null;
            if (video == null) {
                return;
            }
            try {
                ref IntPtr handle = ref TheoraHandle(video);
                if (handle != IntPtr.Zero) {
                    Theorafile.tf_close(ref handle);
                }
            } catch (MissingMemberException) {
                //访问器失效：交给终结器
            }
        }

        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        private static extern FnaVideo CreateFnaVideo(string fileName, GraphicsDevice device);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "theora")]
        private static extern ref IntPtr TheoraHandle(FnaVideo video);

        #endregion
    }
}
