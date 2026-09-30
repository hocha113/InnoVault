using System;
using System.IO;
using System.Security.Cryptography;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Videos
{
    /// <summary>
    /// 一段可播放的 Ogg Theora 视频（<c>.ogv</c>，可带 Vorbis 音轨）：来源文件 + 加载时探测到的元数据
    /// <br/>加载：<c>[VaultLoaden("Assets/Videos/Intro")] static VaultVideo Intro;</c>（路径可省 <c>.ogv</c>，只在客户端加载）、
    /// <see cref="Load"/>，或用 <see cref="FromFile"/> 引用磁盘上的外部文件
    /// <br/>FNA 的解码器只能从磁盘路径打开文件，所以 mod 内的视频会按内容哈希解到 <see cref="CacheDirectory"/>，内容不变就不重复写盘
    /// <br/>本身不持有解码器，可被任意多个 <see cref="VaultVideoPlayer"/> 同时播放；加载失败得到 <see cref="IsEmpty"/> 为真的实例，原因见 <see cref="Error"/>
    /// <br/>MP4 / WebM 请先离线转码：<c>ffmpeg -i in.mp4 -vf scale=-2:720 -c:v libtheora -q:v 7 -pix_fmt yuv420p -c:a libvorbis -q:a 4 out.ogv</c>
    /// </summary>
    public sealed class VaultVideo
    {
        /// <summary>空视频：专用服务器上与加载失败时的默认值，交给播放器会被忽略</summary>
        public static readonly VaultVideo Empty = new("<empty>", null);

        /// <summary>mod 内视频的解包缓存目录（系统临时目录下，可随时删除，缺失时播放前会自动重解）</summary>
        public static string CacheDirectory => Path.Combine(Path.GetTempPath(), "InnoVault", "VideoCache");

        private readonly Mod sourceMod;
        private readonly string sourcePath;

        /// <summary>显示名：mod 内视频为 <c>Mod/路径</c>，外部文件为文件名</summary>
        public string Name { get; }
        /// <summary>解码器打开的磁盘路径；加载失败时为空</summary>
        public string FilePath { get; private set; }
        /// <summary>加载失败的原因；成功时为空</summary>
        public string Error { get; }
        /// <summary>画面宽度（像素）</summary>
        public int Width { get; }
        /// <summary>画面高度（像素）</summary>
        public int Height { get; }
        /// <summary>帧率</summary>
        public double FramesPerSecond { get; }
        /// <summary>时长（视频与音轨两者较长的一个）；文件尾部读不到时间戳时为 <see cref="TimeSpan.Zero"/></summary>
        public TimeSpan Duration { get; }
        /// <summary>是否带 Vorbis 音轨</summary>
        public bool HasAudio { get; }
        /// <summary>音轨声道数，没有音轨时为 0</summary>
        public int AudioChannels { get; }
        /// <summary>音轨采样率，没有音轨时为 0</summary>
        public int AudioSampleRate { get; }
        /// <summary>色度抽样：<c>4:2:0</c> / <c>4:2:2</c> / <c>4:4:4</c></summary>
        public string ChromaFormat { get; }
        /// <summary>是否不可播放（空视频或加载失败）</summary>
        public bool IsEmpty => FilePath == null;

        private VaultVideo(string name, string error) {
            Name = name;
            Error = error;
        }

        private VaultVideo(string name, string filePath, in OggTheoraProbe.Result info, Mod mod, string modPath) {
            Name = name;
            FilePath = filePath;
            Width = info.Width;
            Height = info.Height;
            FramesPerSecond = info.FpsNumerator / (double)info.FpsDenominator;
            Duration = info.Duration;
            HasAudio = info.HasAudio;
            AudioChannels = info.AudioChannels;
            AudioSampleRate = info.AudioSampleRate;
            ChromaFormat = info.PixelFormat switch { 2 => "4:2:2", 3 => "4:4:4", _ => "4:2:0" };
            sourceMod = mod;
            sourcePath = modPath;
        }

        /// <summary>
        /// 从 mod 文件加载 Theora 视频并解到缓存目录；专用服务器上直接返回 <see cref="Empty"/>
        /// </summary>
        /// <param name="mod">视频所在的模组</param>
        /// <param name="path">mod 内路径，可省 <c>.ogv</c> 扩展名</param>
        public static VaultVideo Load(Mod mod, string path) {
            if (Main.dedServ) {
                return Empty;
            }
            if (mod == null || string.IsNullOrEmpty(path)) {
                return Fail(path ?? "<null>", "mod or path is null");
            }
            string file = mod.FileExists(path) ? path : path + ".ogv";
            string name = $"{mod.Name}/{file}";
            if (!mod.FileExists(file)) {
                return Fail($"{mod.Name}/{path}", "file not found (tried the path as-is and with .ogv)");
            }
            try {
                byte[] bytes = mod.GetFileBytes(file);
                if (!OggTheoraProbe.TryProbe(new MemoryStream(bytes, false), out OggTheoraProbe.Result info, out string error)) {
                    return Fail(name, error);
                }
                string cached = WriteCache(mod.Name, file, bytes);
                return Loaded(new VaultVideo(name, cached, info, mod, file));
            } catch (Exception ex) {
                return Fail(name, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 引用磁盘上的 Theora 文件（不复制）；专用服务器上直接返回 <see cref="Empty"/>
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public static VaultVideo FromFile(string filePath) {
            if (Main.dedServ) {
                return Empty;
            }
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                return Fail(filePath ?? "<null>", "file not found");
            }
            string name = Path.GetFileName(filePath);
            try {
                using FileStream stream = File.OpenRead(filePath);
                if (!OggTheoraProbe.TryProbe(stream, out OggTheoraProbe.Result info, out string error)) {
                    return Fail(name, error);
                }
                return Loaded(new VaultVideo(name, Path.GetFullPath(filePath), info, null, null));
            } catch (Exception ex) {
                return Fail(name, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>播放前确认磁盘文件还在；mod 内视频的缓存被外部清掉时从 mod 重解一次</summary>
        internal bool EnsureFile() {
            if (FilePath == null) {
                return false;
            }
            if (File.Exists(FilePath)) {
                return true;
            }
            if (sourceMod == null) {
                VaultMod.LoggerError("VaultVideo.EnsureFile:" + Name, $"[Video] {Name}: file disappeared: {FilePath}");
                return false;
            }
            try {
                FilePath = WriteCache(sourceMod.Name, sourcePath, sourceMod.GetFileBytes(sourcePath));
                return true;
            } catch (Exception ex) {
                VaultMod.LoggerError("VaultVideo.EnsureFile:" + Name, $"[Video] {Name}: re-extracting the cache failed: {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => IsEmpty
            ? $"{Name} (empty{(Error == null ? "" : ": " + Error)})"
            : $"{Name} {Width}x{Height} @ {FramesPerSecond:0.##} fps, {Duration.TotalSeconds:0.0}s, {ChromaFormat}, audio {(HasAudio ? $"{AudioChannels}ch {AudioSampleRate}Hz" : "none")}";

        private static VaultVideo Loaded(VaultVideo video) {
            VaultMod.Instance?.Logger.Debug($"[Video] loaded {video}");
            return video;
        }

        private static VaultVideo Fail(string name, string error) {
            VaultMod.LoggerError("VaultVideo:" + name, $"[Video] {name}: {error}");
            return new VaultVideo(name, error);
        }

        //文件名 = 模组名.路径_内容哈希前 16 位：内容变了换新文件名，同一视频的旧版本随即删除
        private static string WriteCache(string modName, string modPath, byte[] bytes) {
            string directory = CacheDirectory;
            Directory.CreateDirectory(directory);
            string stem = SanitizeFileName(modName + "." + Path.ChangeExtension(modPath, null).Replace('/', '_').Replace('\\', '_'));
            string hash = Convert.ToHexString(SHA256.HashData(bytes), 0, 8).ToLowerInvariant();
            string target = Path.Combine(directory, $"{stem}_{hash}.ogv");
            if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length) {
                string temp = $"{target}.{Environment.ProcessId}.tmp";
                File.WriteAllBytes(temp, bytes);
                try {
                    File.Move(temp, target, true);
                } catch (IOException) {
                    //另一个游戏实例正开着同名文件：同名即同内容，长度对得上就直接用
                    TryDelete(temp);
                    if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length) {
                        throw;
                    }
                }
            }
            foreach (string other in Directory.GetFiles(directory, stem + "_*.ogv")) {
                string otherName = Path.GetFileName(other);
                if (otherName.Length == stem.Length + 21 && !other.Equals(target, StringComparison.OrdinalIgnoreCase)
                    && IsHex(otherName.AsSpan(stem.Length + 1, 16))) {
                    TryDelete(other);
                }
            }
            return target;
        }

        private static string SanitizeFileName(string name) {
            char[] chars = name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++) {
                if (Array.IndexOf(invalid, chars[i]) >= 0) {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        private static bool IsHex(ReadOnlySpan<char> span) {
            foreach (char c in span) {
                if (!char.IsAsciiHexDigit(c)) {
                    return false;
                }
            }
            return true;
        }

        //正在被别的实例播放的旧缓存删不掉，留到下次加载再删
        private static void TryDelete(string file) {
            try {
                File.Delete(file);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }
    }
}
