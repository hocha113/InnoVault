using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault
{
    public static class VideoUtils
    {
        public static Texture2D ImageDataToTexture2D(GraphicsDevice graphicsDevice, ImageData image) {
            int width = image.ImageSize.Width;
            int height = image.ImageSize.Height;

            // FFMediaToolkit 默认输出为 PixelFormat.Rgb24（R, G, B）
            byte[] rgbData = image.Data.ToArray();

            // 转为 XNA 格式需要 RGBA32，每像素 4 字节
            byte[] rgbaData = new byte[width * height * 4];

            for (int i = 0, j = 0; i < rgbData.Length; i += 3, j += 4) {
                rgbaData[j + 0] = rgbData[i + 0]; // R
                rgbaData[j + 1] = rgbData[i + 1]; // G
                rgbaData[j + 2] = rgbData[i + 2]; // B
                rgbaData[j + 3] = 255;            // A
            }

            // 创建 Texture2D
            Texture2D texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
            texture.SetData(rgbaData);
            return texture;
        }

        public static List<Texture2D> GetModVideoFrameT2Ds(Mod mod, string path) {
            List<Texture2D> result = [];
            int i = 0;
            using var stream = mod.GetFileStream(path, true);
            var file = MediaFile.Open(stream);
            while (file.Video.TryGetNextFrame(out var imageData)) {
                result.Add(ImageDataToTexture2D(Main.instance.GraphicsDevice, imageData));
            }
            return result;
        }
    }
}
