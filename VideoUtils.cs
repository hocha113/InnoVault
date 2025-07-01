using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault
{
    public static class VideoUtils
    {
        public static Texture2D ImageDataToTexture2D(GraphicsDevice graphicsDevice, ImageData image) {
            int width = image.ImageSize.Width;
            int height = image.ImageSize.Height;

            byte[] rgbData = image.Data.ToArray();
            byte[] rgbaData = new byte[width * height * 4];

            int rgbLength = rgbData.Length;
            int rgbaLength = rgbaData.Length;

            int pixelCount = Math.Min(rgbLength / 3, rgbaLength / 4);

            for (int i = 0, j = 0, p = 0; p < pixelCount; p++, i += 3, j += 4) {
                rgbaData[j + 0] = rgbData[i + 0]; // R
                rgbaData[j + 1] = rgbData[i + 1]; // G
                rgbaData[j + 2] = rgbData[i + 2]; // B
                rgbaData[j + 3] = 255;            // A
            }

            var texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
            texture.SetData(rgbaData);
            return texture;
        }

        public static List<Texture2D> GetVideoFrameT2Ds(string path) {
            FFmpegLoader.FFmpegPath = Path.Combine(Main.SavePath, "VaultModFFmpeg");
            List<Texture2D> result = [];
            int i = 0;
            var file = MediaFile.Open(path);
            while (file.Video.TryGetNextFrame(out var imageData)) {
                result.Add(ImageDataToTexture2D(Main.instance.GraphicsDevice, imageData));
            }
            return result;
        }

        public static string WriteTempVideo(Stream videoStream, string fileName = "") {
            if (fileName == string.Empty) {
                fileName = videoStream.GetHashCode().ToString() + "_VideoFile";
            }
            string tempPath = Path.Combine(Main.SavePath, fileName);
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write)) {
                videoStream.CopyTo(fs);
            }
            return tempPath;
        }

        public static void PrepareFFmpegDlls() {
            string ffmpegPath = Path.Combine(Main.SavePath, "VaultModFFmpeg");
            Directory.CreateDirectory(ffmpegPath);
            // 假设你将 DLL 打包在 mod 中
            ExtractModFile("lib/FFMediaToolkit.dll", Path.Combine(ffmpegPath, "FFMediaToolkit.dll"));
            // 设置路径给 FFMediaToolkit
            FFmpegLoader.FFmpegPath = ffmpegPath;
        }

        public static void ExtractModFile(string modPath, string outputPath) {
            using var input = VaultMod.Instance.GetFileStream(modPath);
            using var output = File.Create(outputPath);
            input.CopyTo(output);
        }
    }
}
