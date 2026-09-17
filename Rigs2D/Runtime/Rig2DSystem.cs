using InnoVault.Debugs;
using InnoVault.Rigs2D.Solvers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// Rigs2D 子系统：资产登记、开发机热重载、调试叠层的实例名册
    /// <br/>热重载：<see cref="DebugSettings.Rig2DHotReload"/> 打开时，每半秒比对一次 ModSources 里源文件的修改时间，
    /// 变化即重解析并原地替换定义；只在开发机（源目录存在）生效，发布环境自然无源文件可看
    /// </summary>
    public sealed class Rig2DSystem : ModSystem
    {
        private sealed class Watch
        {
            public Vault2DRig Asset;
            public string File;
            public DateTime LastWrite;
        }

        private static readonly List<Watch> watches = [];
        private static readonly List<Rig2DInstance> stepped = [];
        private static readonly HashSet<Rig2DInstance> steppedSet = [];
        private static int pollTimer;
        private static string modSourcesRoot;

        /// <summary>
        /// 轮询间隔（帧）
        /// </summary>
        public static int PollIntervalFrames { get; set; } = 30;

        /// <summary>
        /// 本帧步进过且允许调试显示的实例（叠层读取）
        /// </summary>
        public static IReadOnlyList<Rig2DInstance> DebugInstances => stepped;

        /// <summary>
        /// 已登记的资产数量
        /// </summary>
        public static int WatchedCount => watches.Count;

        /// <summary>
        /// ModSources 根目录（开发机上存在；发布环境一般不存在）
        /// </summary>
        public static string ModSourcesRoot {
            get {
                if (modSourcesRoot != null) {
                    return modSourcesRoot;
                }
                try {
                    string shared = Program.SavePathShared;
                    string candidate = string.IsNullOrEmpty(shared) ? Path.Combine(Main.SavePath, "ModSources") : Path.Combine(shared, "ModSources");
                    modSourcesRoot = candidate;
                } catch {
                    modSourcesRoot = string.Empty;
                }
                return modSourcesRoot;
            }
        }

        /// <summary>
        /// 解析某模组文件在 ModSources 里的绝对路径；不存在返回 <see langword="null"/>
        /// </summary>
        public static string ResolveSourceFile(Mod mod, string relativePath) {
            if (mod == null || string.IsNullOrEmpty(relativePath)) {
                return null;
            }
            string root = ModSourcesRoot;
            if (string.IsNullOrEmpty(root)) {
                return null;
            }
            try {
                string file = Path.Combine(root, mod.Name, relativePath.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(file) ? file : null;
            } catch {
                return null;
            }
        }

        internal static void Register(Vault2DRig asset) {
            if (asset == null || asset.Mod == null || Main.dedServ) {
                return;
            }
            for (int i = 0; i < watches.Count; i++) {
                if (ReferenceEquals(watches[i].Asset, asset)) {
                    return;
                }
            }
            string file = ResolveSourceFile(asset.Mod, asset.SourcePath);
            Watch w = new() {
                Asset = asset,
                File = file,
                LastWrite = file != null ? SafeLastWrite(file) : DateTime.MinValue,
            };
            watches.Add(w);
        }

        internal static void NoteStepped(Rig2DInstance inst) {
            if (Main.dedServ || inst == null || !inst.DebugVisible) {
                return;
            }
            //主菜单等没有 PreUpdateEntities 的场合名册不会被清：封顶防止无限增长
            if (stepped.Count >= 256) {
                stepped.Clear();
                steppedSet.Clear();
            }
            if (steppedSet.Add(inst)) {
                stepped.Add(inst);
            }
        }

        /// <summary>
        /// 立刻重读全部有源文件的资产（忽略修改时间）
        /// </summary>
        public static int ReloadAll() {
            int count = 0;
            for (int i = 0; i < watches.Count; i++) {
                if (TryReload(watches[i], force: true)) {
                    count++;
                }
            }
            return count;
        }

        /// <inheritdoc/>
        public override void PreUpdateEntities() {
            stepped.Clear();
            steppedSet.Clear();
        }

        /// <inheritdoc/>
        public override void PostUpdateEverything() {
            if (Main.dedServ || !DebugSettings.Rig2DHotReload || watches.Count == 0) {
                return;
            }
            if (++pollTimer < Math.Max(PollIntervalFrames, 1)) {
                return;
            }
            pollTimer = 0;
            for (int i = 0; i < watches.Count; i++) {
                TryReload(watches[i], force: false);
            }
        }

        private static bool TryReload(Watch w, bool force) {
            if (w.File == null) {
                //登记时源文件还不存在：再探一次（可能是后来才落盘的导出）
                w.File = ResolveSourceFile(w.Asset.Mod, w.Asset.SourcePath);
                if (w.File == null) {
                    return false;
                }
                w.LastWrite = DateTime.MinValue;
            }
            DateTime now = SafeLastWrite(w.File);
            if (!force && now == w.LastWrite) {
                return false;
            }
            w.LastWrite = now;
            string text;
            try {
                text = File.ReadAllText(w.File, Encoding.UTF8);
            } catch (Exception ex) {
                //编辑器保存瞬间可能占用文件：下一轮再试
                VaultMod.LoggerError($"[Rig2D:{w.Asset.Name}]", $"hot reload read failed: {ex.Message}");
                w.LastWrite = DateTime.MinValue;
                return false;
            }
            bool ok = w.Asset.ApplyText(text);
            VaultMod.Instance?.Logger.Info(ok
                ? $"[Rig2D] hot reloaded '{w.Asset.Name}' (v{w.Asset.Version}) from {w.File}"
                : $"[Rig2D] hot reload of '{w.Asset.Name}' rejected: {w.Asset.LastError}");
            return ok;
        }

        private static DateTime SafeLastWrite(string file) {
            try {
                return File.GetLastWriteTimeUtc(file);
            } catch {
                return DateTime.MinValue;
            }
        }

        /// <inheritdoc/>
        public override void Unload() {
            watches.Clear();
            stepped.Clear();
            steppedSet.Clear();
            pollTimer = 0;
            modSourcesRoot = null;
            Rig2DSolverRegistry.ResetForUnload();
            Rig2DBinder.ClearCache();
        }
    }
}
