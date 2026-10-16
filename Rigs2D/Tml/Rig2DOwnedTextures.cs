using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 骨架贴图的持有方式
    /// </summary>
    public enum Rig2DTextureMode
    {
        /// <summary>
        /// 走模组资产系统（<c>Assets.Request</c>）：随资产加载即请求，tML 缓存到卸载为止
        /// </summary>
        Shared,
        /// <summary>
        /// 骨架自持：<see cref="Vault2DRig.AcquireTextures"/> 时才在后台解码、绘制线程上传，<see cref="Vault2DRig.ReleaseTextures"/> 释放显存。
        /// 只在某个界面里出现的大幅美术（主菜单主题图、过场木偶）用它，不必整局常驻显存
        /// </summary>
        Owned,
    }

    /// <summary>
    /// 自持贴图仓：按路径去重，后台线程顺序解码 <c>.rawimg</c>（tML 打包时由 PNG 转成的直通 alpha 原始像素）并预乘 alpha，
    /// 绘制线程取用时上传成 <see cref="Texture2D"/>；释放后在途的解码结果作废
    /// <br/>预乘是因为 SpriteBatch 的 <c>AlphaBlend</c> 是预乘混合，半透明边缘按直通数据画会发亮；
    /// 找不到 <c>.rawimg</c> 的路径退回资产系统的共享贴图（不可释放）
    /// </summary>
    internal sealed class Rig2DOwnedTextures
    {
        private sealed class Slot
        {
            public string Path;
            public Texture2D Texture;
            public Decoded Pending;
            public bool Queued;
            public bool Missing;
        }

        private sealed class Decoded
        {
            public byte[] Data;
            public int Width;
            public int Height;
        }

        private readonly Mod mod;
        private readonly string rigName;
        private readonly Dictionary<string, Slot> slots = new(StringComparer.Ordinal);
        private readonly Queue<Slot> decodeQueue = new();
        private readonly object sync = new();
        private bool acquired;
        private bool decoding;
        private int generation;

        public Rig2DOwnedTextures(Mod mod, string rigName) {
            this.mod = mod;
            this.rigName = rigName;
        }

        public bool Acquired => acquired;

        /// <summary>
        /// 取一张贴图：未持有时为空；解码好了就在调用线程（绘制线程）上传
        /// </summary>
        public Texture2D Get(string path) {
            if (!acquired || Main.dedServ || string.IsNullOrEmpty(path)) {
                return null;
            }
            Slot slot = Touch(path);
            if (slot.Texture != null) {
                return slot.Texture;
            }
            Upload(slot);
            if (slot.Texture == null && slot.Missing) {
                //没有原始像素可读：退回共享资产（tML 缓存，不随释放回收）
                return Vault2DRig.ResolveTexture(mod, path, rigName)?.Value;
            }
            return slot.Texture;
        }

        /// <summary>
        /// 在调用线程（绘制线程）上传至多 <paramref name="maxUploads"/> 张已解码的贴图，返回仍待上传的张数
        /// </summary>
        public int Pump(int maxUploads) {
            if (!acquired || Main.dedServ) {
                return 0;
            }
            List<Slot> ready = [];
            int waiting = 0;
            lock (sync) {
                foreach (Slot s in slots.Values) {
                    if (s.Texture == null && s.Pending != null) {
                        if (ready.Count < maxUploads) {
                            ready.Add(s);
                        }
                        else {
                            waiting++;
                        }
                    }
                }
            }
            foreach (Slot s in ready) {
                Upload(s);
            }
            return waiting;
        }

        private void Upload(Slot slot) {
            Decoded data;
            lock (sync) {
                data = slot.Pending;
                slot.Pending = null;
            }
            if (data == null) {
                return;
            }
            try {
                Texture2D tex = new(Main.instance.GraphicsDevice, data.Width, data.Height);
                tex.SetData(data.Data);
                slot.Texture = tex;
            } catch (Exception ex) {
                Rig2DPlatform.LogError($"[Rig2D:{rigName}:tex]", $"owned texture upload failed for '{slot.Path}': {ex.Message}");
                slot.Missing = true;
            }
        }

        /// <summary>
        /// 开始持有：给定路径全部排队解码（已排过的不重复）
        /// </summary>
        public void Acquire(IEnumerable<string> paths) {
            if (Main.dedServ) {
                return;
            }
            acquired = true;
            foreach (string p in paths) {
                if (!string.IsNullOrEmpty(p)) {
                    Touch(p);
                }
            }
        }

        /// <summary>
        /// 释放全部显存；在途解码作废
        /// </summary>
        public void Release() {
            acquired = false;
            List<Texture2D> dispose = [];
            lock (sync) {
                generation++;
                decodeQueue.Clear();
                foreach (Slot s in slots.Values) {
                    if (s.Texture != null) {
                        dispose.Add(s.Texture);
                    }
                }
                slots.Clear();
            }
            foreach (Texture2D t in dispose) {
                Rig2DPlatform.QueueMainThread(t.Dispose);
            }
        }

        /// <summary>
        /// 给定路径里已经可用（已上传或已退回共享资产）的张数；解码完还没上传的不算
        /// </summary>
        public int CountReady(IEnumerable<string> paths, out int total) {
            total = 0;
            int ready = 0;
            HashSet<string> seen = new(StringComparer.Ordinal);
            lock (sync) {
                foreach (string p in paths) {
                    if (string.IsNullOrEmpty(p) || !seen.Add(p)) {
                        continue;
                    }
                    total++;
                    if (slots.TryGetValue(p, out Slot s) && (s.Texture != null || s.Missing)) {
                        ready++;
                    }
                }
            }
            return ready;
        }

        private Slot Touch(string path) {
            bool start = false;
            Slot slot;
            lock (sync) {
                if (!slots.TryGetValue(path, out slot)) {
                    slot = new Slot { Path = path };
                    slots[path] = slot;
                }
                if (!slot.Queued) {
                    slot.Queued = true;
                    decodeQueue.Enqueue(slot);
                    if (!decoding) {
                        decoding = true;
                        start = true;
                    }
                }
            }
            if (start) {
                int gen = generation;
                Task.Run(() => DecodeLoop(gen));
            }
            return slot;
        }

        //后台顺序解码：一张接一张，避免同时解压一堆大图挤占内存与磁盘
        private void DecodeLoop(int gen) {
            while (true) {
                Slot slot;
                lock (sync) {
                    if (gen != generation || decodeQueue.Count == 0) {
                        decoding = false;
                        //释放后又有新请求：换新一代重启循环
                        if (decodeQueue.Count > 0) {
                            decoding = true;
                            int next = generation;
                            Task.Run(() => DecodeLoop(next));
                        }
                        return;
                    }
                    slot = decodeQueue.Dequeue();
                }
                Decoded data = Decode(slot.Path);
                lock (sync) {
                    if (gen != generation) {
                        continue;
                    }
                    if (data == null) {
                        slot.Missing = true;
                    }
                    else {
                        slot.Pending = data;
                    }
                }
            }
        }

        private Decoded Decode(string texturePath) {
            try {
                if (!ResolveFile(texturePath, out Mod target, out string file)) {
                    return null;
                }
                byte[] pixels;
                int width;
                int height;
                using (Stream stream = target.GetFileStream(file, newFileStream: true)) {
                    pixels = ImageIO.ReadRaw(stream, out width, out height);
                }
                Premultiply(pixels);
                return new Decoded { Data = pixels, Width = width, Height = height };
            } catch (Exception ex) {
                Rig2DPlatform.LogError($"[Rig2D:{rigName}:tex]", $"owned texture decode failed for '{texturePath}': {ex.Message}");
                return null;
            }
        }

        //路径规则同 Vault2DRig.ResolveTexture：@其他模组/路径 跨模组，允许带本模组名前缀
        private bool ResolveFile(string texturePath, out Mod target, out string file) {
            string path = texturePath.Replace('\\', '/');
            target = mod;
            if (path.StartsWith('@')) {
                int slash = path.IndexOf('/');
                string modName = slash > 0 ? path[1..slash] : path[1..];
                path = slash > 0 ? path[(slash + 1)..] : string.Empty;
                if (!ModLoader.TryGetMod(modName, out target)) {
                    file = null;
                    return false;
                }
            }
            else if (target != null) {
                string prefix = target.Name + "/";
                if (path.StartsWith(prefix, StringComparison.Ordinal)) {
                    path = path[prefix.Length..];
                }
            }
            file = path + ".rawimg";
            return target != null && target.FileExists(file);
        }

        private static void Premultiply(byte[] rgba) {
            for (int i = 0; i + 3 < rgba.Length; i += 4) {
                int a = rgba[i + 3];
                if (a == 255) {
                    continue;
                }
                rgba[i] = (byte)((rgba[i] * a + 127) / 255);
                rgba[i + 1] = (byte)((rgba[i + 1] * a + 127) / 255);
                rgba[i + 2] = (byte)((rgba[i + 2] * a + 127) / 255);
            }
        }
    }
}
