using System;
using System.Buffers.Binary;
using System.IO;

namespace InnoVault.Videos
{
    /// <summary>
    /// Ogg 容器的轻量探测：读 Theora / Vorbis 识别头拿到尺寸、帧率、色度格式与音频参数，扫文件尾部页头的 granulepos 算时长
    /// <br/>不解码任何数据，只做加载期校验与元数据；解码交给 FNA 自带的 Theorafile
    /// </summary>
    internal static class OggTheoraProbe
    {
        internal struct Result
        {
            public int Width;
            public int Height;
            public int FpsNumerator;
            public int FpsDenominator;
            /// <summary>与 FNA <c>Theorafile.th_pixel_fmt</c> 同值：0 = 4:2:0，2 = 4:2:2，3 = 4:4:4</summary>
            public int PixelFormat;
            public bool HasAudio;
            public int AudioChannels;
            public int AudioSampleRate;
            /// <summary>视频与音轨两者较长的一个；尾部找不到带时间戳的页时为 <see cref="TimeSpan.Zero"/></summary>
            public TimeSpan Duration;
        }

        private const int HeadScanBytes = 256 * 1024;
        private const int TailScanBytes = 512 * 1024;
        private static readonly uint[] crcTable = BuildCrcTable();

        internal static bool TryProbe(Stream stream, out Result result, out string error) {
            result = default;
            error = null;
            long length = stream.Length;
            byte[] head = ReadRange(stream, 0, (int)Math.Min(length, HeadScanBytes));
            if (head.Length < 27 || head[0] != 'O' || head[1] != 'g' || head[2] != 'g' || head[3] != 'S') {
                error = DescribeForeignFormat(head);
                return false;
            }

            uint theoraSerial = 0, vorbisSerial = 0;
            bool hasTheora = false, hasSetup = false;
            bool v321 = true;
            int kfgShift = 0;
            //识别头都在各流的起始页（BOS）里；再往后找 Theora 的 setup 头包，缺了它 FNA 打开文件时不检查返回值，原生层会读到未初始化的状态
            for (int pos = 0; !hasSetup;) {
                int len = PageLength(head, pos);
                if (len < 0) {
                    break;
                }
                bool bos = (head[pos + 5] & 0x02) != 0;
                if (!bos && !hasTheora) {
                    break;
                }
                uint serial = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(pos + 14));
                int segments = head[pos + 26];
                int bodyStart = pos + 27 + segments;
                ReadOnlySpan<byte> body = head.AsSpan(bodyStart, pos + len - bodyStart);
                if (!bos) {
                    if (serial == theoraSerial) {
                        //按分段表切包：分段值小于 255 处一个包结束，comment 与 setup 头包常挤在同一页
                        bool packetStart = (head[pos + 5] & 0x01) == 0;
                        for (int s = 0, offset = 0; s < segments && !hasSetup; s++) {
                            hasSetup = packetStart && IsTheoraHeader(body.Slice(offset), 0x82);
                            int lace = head[pos + 27 + s];
                            offset += lace;
                            packetStart = lace < 255;
                        }
                    }
                }
                else if (!hasTheora && body.Length >= 42 && IsTheoraHeader(body, 0x80)) {
                    hasTheora = true;
                    theoraSerial = serial;
                    v321 = body[7] > 3 || (body[7] == 3 && (body[8] > 2 || (body[8] == 2 && body[9] >= 1)));
                    result.Width = ReadUInt24BigEndian(body, 14);
                    result.Height = ReadUInt24BigEndian(body, 17);
                    result.FpsNumerator = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(22));
                    result.FpsDenominator = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(26));
                    int packed = (body[40] << 8) | body[41];
                    kfgShift = (packed >> 5) & 0x1F;
                    result.PixelFormat = (packed >> 3) & 0x3;
                }
                else if (!result.HasAudio && body.Length >= 16 && body[0] == 0x01 && body.Slice(1, 6).SequenceEqual("vorbis"u8)) {
                    result.HasAudio = true;
                    vorbisSerial = serial;
                    result.AudioChannels = body[11];
                    result.AudioSampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(12));
                }
                pos += len;
            }
            if (!hasTheora) {
                error = result.HasAudio
                    ? "Ogg file has a Vorbis audio stream but no Theora video stream (audio-only .ogg?)"
                    : "Ogg file has no Theora video stream";
                return false;
            }
            if (result.FpsNumerator <= 0 || result.FpsDenominator <= 0 || result.Width <= 0 || result.Height <= 0) {
                error = $"invalid Theora header: {result.Width}x{result.Height}, fps {result.FpsNumerator}/{result.FpsDenominator}";
                return false;
            }
            if (!hasSetup) {
                error = "Theora setup header not found near the start of the file (truncated or damaged file?)";
                return false;
            }

            //尾部逐字节找页头，CRC 通过才算数；同一流越靠后的页时间戳越新
            long tailStart = Math.Max(0, length - TailScanBytes);
            byte[] tail = tailStart == 0 && head.Length == length ? head : ReadRange(stream, tailStart, (int)(length - tailStart));
            long theoraGranule = -1, vorbisGranule = -1;
            for (int i = 0; i + 27 <= tail.Length;) {
                int len = PageLength(tail, i);
                if (len < 0 || !CrcMatches(tail.AsSpan(i, len))) {
                    i++;
                    continue;
                }
                uint serial = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 14));
                long granule = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(i + 6));
                if (granule != -1) {
                    if (serial == theoraSerial) {
                        theoraGranule = granule;
                    }
                    else if (result.HasAudio && serial == vorbisSerial) {
                        vorbisGranule = granule;
                    }
                }
                i += len;
            }

            double seconds = 0;
            if (theoraGranule > 0) {
                //3.2.1 起 granulepos 记的是帧数，更早的版本记的是帧序号
                long keyframe = theoraGranule >> kfgShift;
                long frames = keyframe + (theoraGranule - (keyframe << kfgShift)) + (v321 ? 0 : 1);
                seconds = frames * (double)result.FpsDenominator / result.FpsNumerator;
            }
            if (vorbisGranule > 0 && result.AudioSampleRate > 0) {
                seconds = Math.Max(seconds, vorbisGranule / (double)result.AudioSampleRate);
            }
            result.Duration = TimeSpan.FromSeconds(seconds);
            return true;
        }

        private static string DescribeForeignFormat(byte[] head) {
            if (head.Length >= 8 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p') {
                return "file is MP4/MOV, not Ogg Theora; convert it: ffmpeg -i in.mp4 -c:v libtheora -q:v 7 -c:a libvorbis -q:a 4 out.ogv";
            }
            if (head.Length >= 4 && head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) {
                return "file is WebM/Matroska, not Ogg Theora; convert it with -c:v libtheora -c:a libvorbis";
            }
            return "file is not an Ogg container";
        }

        //整页长度（头 + 分段表 + 数据）；不是完整的合法页返回 -1
        private static int PageLength(byte[] buf, int at) {
            if (at + 27 > buf.Length || buf[at] != 'O' || buf[at + 1] != 'g' || buf[at + 2] != 'g' || buf[at + 3] != 'S' || buf[at + 4] != 0) {
                return -1;
            }
            int segments = buf[at + 26];
            int total = 27 + segments;
            if (at + total > buf.Length) {
                return -1;
            }
            for (int i = 0; i < segments; i++) {
                total += buf[at + 27 + i];
            }
            return at + total <= buf.Length ? total : -1;
        }

        //Ogg 页校验：多项式 0x04C11DB7，初值 0，不反射，计算时校验字段（22..25）按 0 处理
        private static bool CrcMatches(ReadOnlySpan<byte> page) {
            uint stored = BinaryPrimitives.ReadUInt32LittleEndian(page.Slice(22));
            uint crc = 0;
            for (int i = 0; i < page.Length; i++) {
                byte b = i >= 22 && i < 26 ? (byte)0 : page[i];
                crc = (crc << 8) ^ crcTable[(crc >> 24) ^ b];
            }
            return crc == stored;
        }

        private static uint[] BuildCrcTable() {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++) {
                uint r = i << 24;
                for (int k = 0; k < 8; k++) {
                    r = (r & 0x80000000u) != 0 ? (r << 1) ^ 0x04C11DB7u : r << 1;
                }
                table[i] = r;
            }
            return table;
        }

        //Theora 头包：类型字节（0x80 识别 / 0x81 注释 / 0x82 setup）+ "theora"
        private static bool IsTheoraHeader(ReadOnlySpan<byte> packet, byte type) => packet.Length >= 7 && packet[0] == type && packet.Slice(1, 6).SequenceEqual("theora"u8);

        private static int ReadUInt24BigEndian(ReadOnlySpan<byte> span, int at) => (span[at] << 16) | (span[at + 1] << 8) | span[at + 2];

        private static byte[] ReadRange(Stream stream, long offset, int count) {
            byte[] buffer = new byte[count];
            stream.Position = offset;
            stream.ReadExactly(buffer, 0, count);
            return buffer;
        }
    }
}
