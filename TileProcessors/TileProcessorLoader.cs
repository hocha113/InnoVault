using InnoVault.Concurrent;
using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 关于TP系统的大部分逻辑与钩子挂载与此处
    /// </summary>
    public sealed class TileProcessorLoader : IVaultLoader
    {
        #region Data
        private static readonly string key_TPData_TagList = "TPData_TagList";
        private static readonly object lockObject = new object();
        /// <summary>
        /// 在世界中的TP实体的最大存在数量
        /// </summary>
        public const int MaxTPInWorldCount = 32767;
        /// <summary>
        /// 是否加载好了TP实体到世界中<br/>
        /// 该状态现已由 <see cref="VaultLoadingProgress"/> 统一管理，此处仅作兼容转发
        /// </summary>
        public static bool LoadenTP => VaultLoadingProgress.LocalTPLoaded;
        /// <summary>
        /// 当前TP实体的最大ID
        /// </summary>
        public static int TP_ID_Count { get; internal set; }
        /// <summary>
        /// 当前世界的数据
        /// </summary>
        public static TagCompound ActiveWorldTagData { get; internal set; }
        /// <summary>
        /// 所有TP实体的列表该列表在加载时初始化，并包含所有TP实体的实例
        /// </summary>
        public static List<TileProcessor> TP_Instances { get; private set; } = [];
        /// <summary>
        /// 当前世界中的TP实体列表，此列表在世界加载和操作时动态更新<br/>
        /// 访问该列表时应当谨慎，因为该列表可能在运行时遭到修改，避免使用foreach等方式直接遍历访问<br/>
        /// 安全起见，应当使用for配合倒序遍历，或者使用.ToList克隆该集合的快照副本进行访问
        /// </summary>
        public static List<TileProcessor> TP_InWorld { get; private set; } = [];
        /// <summary>
        /// 当使用<see cref="AddInWorld(int, Point16, Item)"/>函数时更新这个字典，用于快速查询对应的TP实体以节省性能
        /// </summary>
        public static Dictionary<(int, Point16), TileProcessor> TP_IDAndPoint_To_Instance { get; private set; } = [];
        /// <summary>
        /// 当使用<see cref="AddInWorld(int, Point16, Item)"/>函数时更新这个字典，用于快速查询对应的TP实体以节省性能
        /// </summary>
        public static Dictionary<(string, Point16), TileProcessor> TP_NameAndPoint_To_Instance { get; private set; } = [];
        /// <summary>
        /// 当使用<see cref="AddInWorld(int, Point16, Item)"/>函数时更新这个字典，用于快速查询对应的TP实体以节省性能
        /// </summary>
        public static Dictionary<Point16, TileProcessor> TP_Point_To_Instance { get; private set; } = [];
        /// <summary>
        /// 将TP实体的类型映射到其对应的ID的字典
        /// </summary>
        public static Dictionary<Type, int> TP_Type_To_ID { get; private set; } = [];
        /// <summary>
        /// 将TP实体的内部名映射到其对应的ID的字典
        /// </summary>
        public static Dictionary<string, int> TP_FullName_To_ID { get; private set; } = [];
        /// <summary>
        /// 将TP实体的类型映射到模块实例的字典
        /// </summary>
        public static Dictionary<Type, TileProcessor> TP_Type_To_Instance { get; private set; } = [];
        /// <summary>
        /// 记录当前世界中每个模块ID对应的TP实体数量
        /// </summary>
        public static Dictionary<int, int> TP_ID_To_InWorld_Count { get; internal set; } = [];
        /// <summary>
        /// 将模块ID映射到模块实例的字典
        /// </summary>
        public static Dictionary<int, TileProcessor> TP_ID_To_Instance { get; private set; } = [];
        /// <summary>
        /// 将目标Tile的ID映射到模块实例的字典
        /// </summary>
        public static Dictionary<int, List<TileProcessor>> TargetTile_To_TPInstance { get; private set; } = [];
        /// <summary>
        /// 关于目标物块键的哈希列表
        /// </summary>
        public static HashSet<int> TargetTileTypes { get; private set; } = [];
        /// <summary>
        /// 所有的<see cref="GlobalTileProcessor"/>实例在此处储存
        /// </summary>
        internal static List<GlobalTileProcessor> TPGlobalHooks { get; private set; } = [];
        /// <summary>
        /// 世界TP加载进度百分比，范围 0~100用于 UI 显示传输进度<br/>
        /// 该进度现已由 <see cref="VaultLoadingProgress"/> 统一管理，此处仅作兼容转发
        /// </summary>
        internal static float WorldLoadProgress => VaultLoadingProgress.LocalProgress * 100f;
        //反射KillMultiTile存储的方法对象
        private static MethodBase onTile_KillMultiTile_Method;
        //用于挂载KillMultiTile的委托类型
        private delegate void On_Tile_KillMultiTile_Dalegate(int i, int j, int frameX, int frameY, int type);
        //用于加载TryIsTopLeftPoint的委托类型
        internal delegate bool? DelegateTryIsTopLeftPoint(int x, int y, out Point16 position);
        //关于GTP钩子的缓存
        private static readonly List<VaultHookMethodCache<GlobalTileProcessor>> hooks = [];
        internal static VaultHookMethodCache<GlobalTileProcessor> HookInitialize;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPreUpdate;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPostUpdate;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPreSingleInstanceUpdate;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookSingleInstanceUpdate;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookGetTopLeftPoint;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookTryIsTopLeftPoint;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookGetTopLeftOrNull;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPreTileDrawEverything;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPreDrawEverything;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPreTileDraw;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPreDraw;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPostDraw;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookPostDrawEverything;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookIsDaed;
        internal static VaultHookMethodCache<GlobalTileProcessor> HookOnKill;
        #endregion

        private static VaultHookMethodCache<GlobalTileProcessor> AddHook<F>(Expression<Func<GlobalTileProcessor, F>> func) where F : Delegate {
            VaultHookMethodCache<GlobalTileProcessor> hook = VaultHookMethodCache<GlobalTileProcessor>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        void IVaultLoader.LoadData() {
            onTile_KillMultiTile_Method = typeof(TileLoader).GetMethod("KillMultiTile", BindingFlags.Public | BindingFlags.Static);
            VaultHook.Add(onTile_KillMultiTile_Method, OnKillMultiTileHook);

            WorldGen.Hooks.OnWorldLoad += LoadWorldTileProcessor;
        }

        void IVaultLoader.SetupData() {
            TargetTileTypes = [.. TargetTile_To_TPInstance.Keys];

            HookInitialize = AddHook<Action<TileProcessor>>(tp => tp.Initialize);
            HookPreUpdate = AddHook<Func<TileProcessor, bool>>(tp => tp.PreUpdate);
            HookPostUpdate = AddHook<Action<TileProcessor>>(tp => tp.PostUpdate);
            HookPreSingleInstanceUpdate = AddHook<Func<TileProcessor, bool>>(tp => tp.PreSingleInstanceUpdate);
            HookSingleInstanceUpdate = AddHook<Action<TileProcessor>>(tp => tp.SingleInstanceUpdate);
            HookGetTopLeftPoint = AddHook<Func<int, int, Point16?>>(tp => tp.GetTopLeftPoint);
            HookTryIsTopLeftPoint = AddHook<DelegateTryIsTopLeftPoint>(tp => tp.TryIsTopLeftPoint);
            HookGetTopLeftOrNull = AddHook<Func<Tile, int, int, Point16?>>(tp => tp.GetTopLeftOrNull);
            HookPreTileDrawEverything = AddHook<Func<SpriteBatch, bool>>(tp => tp.PreTileDrawEverything);
            HookPreDrawEverything = AddHook<Func<SpriteBatch, bool>>(tp => tp.PreDrawEverything);
            HookPreTileDraw = AddHook<Func<TileProcessor, SpriteBatch, bool>>(tp => tp.PreTileDraw);
            HookPreDraw = AddHook<Func<TileProcessor, SpriteBatch, bool>>(tp => tp.PreDraw);
            HookPostDraw = AddHook<Action<TileProcessor, SpriteBatch>>(tp => tp.PostDraw);
            HookPostDrawEverything = AddHook<Action<SpriteBatch>>(tp => tp.PostDrawEverything);
            HookIsDaed = AddHook<Func<TileProcessor, bool?>>(tp => tp.IsDaed);
            HookOnKill = AddHook<Action<TileProcessor>>(tp => tp.OnKill);
        }

        void IVaultLoader.UnLoadData() {
            foreach (var module in TP_Instances) {
                module.UnLoad();
            }

            TP_Instances?.Clear();
            TP_InWorld?.Clear();
            TP_IDAndPoint_To_Instance?.Clear();
            TP_NameAndPoint_To_Instance?.Clear();
            TP_Point_To_Instance?.Clear();
            TP_Type_To_ID?.Clear();
            TP_FullName_To_ID?.Clear();
            TP_Type_To_Instance?.Clear();
            TP_ID_To_Instance?.Clear();
            TP_ID_To_InWorld_Count?.Clear();
            TargetTile_To_TPInstance?.Clear();
            TPGlobalHooks?.Clear();
            onTile_KillMultiTile_Method = null;
            ActiveWorldTagData = null;

            WorldGen.Hooks.OnWorldLoad -= LoadWorldTileProcessor;
        }

        //集中管理所有KillMultiTileSet钩子
        private static void OnKillMultiTileHook(On_Tile_KillMultiTile_Dalegate orig, int i, int j, int frameX, int frameY, int type) {
            if (ByPositionGetTP(i, j, out var tileProcessor)) {
                tileProcessor.KillMultiTileSet(frameX, frameY);
            }

            orig.Invoke(i, j, frameX, frameY, type);
        }

        /// <summary>
        /// 是否在世界中运行TP实体的逻辑
        /// </summary>
        /// <returns></returns>
        public static bool CanRunByWorld() {
            if (VaultUtils.isClient && !TileProcessorNetWork.LoadenTPByNetWork) {
                return false;
            }
            return LoadenTP && TP_InWorld.Count > 0;
        }

        /// <summary>
        /// 在世界中基于指定的 <see cref="TileProcessor"/> ID，创建并注册一个新的TP实例<br/>
        /// </summary>
        /// <param name="tpID">要放置的TP实体的ID</param>
        /// <param name="position">在世界中的左上角位置（物块坐标）</param>
        /// <param name="item">用于追踪该实例来源的物品，可以为 <see langword="null"/></param>
        /// <remarks>
        /// 本方法只负责实体的创建与注册，不会发送网络同步<br/>
        /// 在多人游戏环境下，需要在合适时机调用<br/>
        /// <see cref="TileProcessorNetWork.PlaceInWorldNetSend"/> 主动同步<br/>
        /// 如果目标位置已经存在同类实例，本方法会覆盖旧实例<br/>
        /// 如果在一个 Tile 上挂载多个 TP 实例，可能会导致覆盖逻辑出错<br/>
        /// </remarks>
        /// <returns>
        /// 成功创建并注册的 <see cref="TileProcessor"/> 实例<br/>
        /// 如果超过 <see cref="MaxTPInWorldCount"/> 或创建失败，返回 <see langword="null"/><br/>
        /// </returns>
        public static TileProcessor NewTPInWorld(int tpID, Point16 position, Item item) {
            if (TP_InWorld.Count >= MaxTPInWorldCount) {
                return null;
            }

            //并行阶段禁止结构性修改世界列表/字典，延迟到Phase 2主线程执行
            if (TileProcessorParallel.InParallelPhase) {
                TileProcessorParallel.Defer(() => NewTPInWorld(tpID, position, item));
                return null;
            }

            TileProcessor reset = null;

            TileProcessor newProcessor = TP_ID_To_Instance[tpID].Clone();
            newProcessor.Position = position;
            newProcessor.TrackItem = item;
            newProcessor.Active = true;
            newProcessor.InitializePositionAndBounds();
            newProcessor.SetProperty();

            //Grouped型实体的加入会改变连通拓扑，标脏以便重建岛屿
            if (newProcessor.ParallelKind == ParallelExecutionKind.Grouped) {
                TileProcessorParallel.MarkTopologyDirty();
            }

            //在这里实体已经被设置好了，更新一下Map
            TP_IDAndPoint_To_Instance[(newProcessor.ID, newProcessor.Position)] = newProcessor;//如果实体重叠，那么就会进行覆盖
            TP_NameAndPoint_To_Instance[(newProcessor.FullName, newProcessor.Position)] = newProcessor;
            TP_Point_To_Instance[newProcessor.Position] = newProcessor;

            bool add = true;
            for (int i = 0; i < TP_InWorld.Count; i++) {
                if (TP_InWorld[i].Active) {
                    continue;//如果目标的实体槽位是活跃的就跳过
                }

                newProcessor.WhoAmI = TP_InWorld[i].WhoAmI;
                TP_InWorld[i] = newProcessor;
                add = false;
                reset = newProcessor;
                break;
            }

            if (!add) {//如果遍历至末尾也没能找到空闲槽位插入实体，就进行扩容
                return reset;
            }

            newProcessor.WhoAmI = TP_InWorld.Count;
            reset = newProcessor;
            TP_InWorld.Add(newProcessor);

            return reset;
        }

        /// <summary>
        /// 向世界中的TP实体列表添加一个新的 <see cref="TileProcessor"/><br/>
        /// 该方法并不发送实体的放置同步信息，需要在合适的情况下自行调用<see cref="TileProcessorNetWork.PlaceInWorldNetSend"/>
        /// </summary>
        /// <param name="tileID">要添加的 Tile 的 ID</param>
        /// <param name="position">该模块的左上角位置</param>
        /// <param name="item">用于跟踪该模块的物品，可以为 null</param>
        /// <remarks>
        /// 在绝大多数情况下，都应该只使用这个方法添加新的TP实体，以确保处理完善
        /// </remarks>
        public static TileProcessor AddInWorld(int tileID, Point16 position, Item item) {
            if (tileID == 0 || TP_InWorld.Count >= MaxTPInWorldCount) {//是的，我们拒绝泥土
                return null;
            }

            //并行阶段禁止结构性修改世界列表/字典，延迟到Phase 2主线程执行
            if (TileProcessorParallel.InParallelPhase) {
                TileProcessorParallel.Defer(() => AddInWorld(tileID, position, item));
                return null;
            }

            TileProcessor reset = null;

            lock (lockObject) {
                if (!TargetTile_To_TPInstance.TryGetValue(tileID, out List<TileProcessor> processorList)) {
                    return reset;
                }

                //TODO:这种处理方式虽然高性能，但在一个物块可能挂载有多个TP实体的情况下会出先问题
                bool checkOtherTP = ByPositionGetTP(position, out var otherTP);

                foreach (var processor in processorList) {
                    if (checkOtherTP && otherTP.FullName == processor.FullName) {
                        continue;
                    }

                    reset = NewTPInWorld(processor.ID, position, item);
                }
            }

            return reset;
        }

        internal static void InitializeWorldTP() {
            if (TP_InWorld == null) {
                TP_InWorld = [];
            }
            else {
                TP_InWorld.Clear();
            }

            if (TP_IDAndPoint_To_Instance == null) {
                TP_IDAndPoint_To_Instance = [];
            }
            else {
                TP_IDAndPoint_To_Instance.Clear();
            }

            if (TP_NameAndPoint_To_Instance == null) {
                TP_NameAndPoint_To_Instance = [];
            }
            else {
                TP_NameAndPoint_To_Instance.Clear();
            }

            if (TP_Point_To_Instance == null) {
                TP_Point_To_Instance = [];
            }
            else {
                TP_Point_To_Instance.Clear();
            }
        }

        /// <summary>
        /// 加载世界中的所有 <see cref="TileProcessor"/>，初始化和激活它们
        /// </summary>
        /// <remarks>
        /// 此方法会首先移除不再活跃的模块，然后扫描整个世界的每一个 Tile，
        /// 识别出多结构物块的左上角 Tile，并将其添加到世界中的模块列表中
        /// 最后，加载所有处于激活状态的模块
        /// </remarks>
        public static void LoadWorldTileProcessor() {
            InitializeWorldTP();

            VaultLoadingProgress.BeginLocalLoad();

            Task.Run(async () => {
                VaultLoadingProgress.LocalTPLoaded = false;
                try {
                    await VaultUtils.WaitUntilAsync(() => VaultSave.LoadenWorld, 50, 10000);//最多等10秒
                } catch (TaskCanceledException) {
                    VaultMod.Instance.Logger.Error("[LoadWorldTileProcessor] The waiting for VaultSave.LoadenWorld to complete has timed out.");
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[LoadWorldTileProcessor] An exception occurred while waiting for VaultSave.LoadenWorld: {ex.Message}");
                }

                VaultLoadingProgress.EnterPhase(LoadingPhase.ScanningWorld);
                VaultLoadingProgress.ReportLocal(VaultLoadingProgress.LocalWaitingWorldDataEnd);

                try {
                    LoadWorldTileProcessorInner();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[LoadWorldTileProcessor] An error occurred while executing: {ex.Message}");
                } finally {
                    VaultLoadingProgress.LocalTPLoaded = true;
                }
            });
        }

        private static void LoadWorldTileProcessorInner() {
            List<(ushort, ushort, ushort)> collectedDatas = new();

            for (ushort x = 0; x < Main.tile.Width; x++) {
                for (ushort y = 0; y < Main.tile.Height; y++) {
                    Tile tile = Main.tile[x, y];
                    if (tile == null || !tile.HasTile) {
                        continue;
                    }

                    if (!TargetTileTypes.Contains(tile.TileType)) {
                        continue;
                    }

                    collectedDatas.Add((x, y, tile.TileType));
                }
            }

            VaultLoadingProgress.EnterPhase(LoadingPhase.PlacingProcessors);
            VaultLoadingProgress.ReportLocal(VaultLoadingProgress.LocalScanningEnd);

            int count = collectedDatas.Count;
            for (int i = 0; i < count; i++) {
                var data = collectedDatas[i];
                if (TileProcessorIsTopLeft(data.Item1, data.Item2, out Point16 point)) {
                    AddInWorld(data.Item3, point, null);
                    VaultLoadingProgress.ReportPlacement(i, count);
                }
            }

            //需要再次明确一个论点，世界加载钩子会在客户端和服务端上被调用，而客户端并不需要加载存档数据
            if (!VaultUtils.isClient && ActiveWorldTagData?.Count > 0) {
                VaultLoadingProgress.EnterPhase(LoadingPhase.LoadingWorldData);
                try {
                    LoadWorldData(ActiveWorldTagData);
                } finally {
                    ActiveWorldTagData.Clear();//用完释放
                    ActiveWorldTagData = null;
                }
            }

            VaultLoadingProgress.ReportLocal(1f);

            for (int i = 0; i < TP_InWorld.Count; i++) {
                var tp = TP_InWorld[i];
                if (tp == null || !tp.Active) {
                    continue;
                }
                tp.LoadInWorld();
            }
        }

        /// <summary>
        /// 保存世界中所有TP实体的数据，在退出世界时调用
        /// </summary>
        /// <param name="tag"></param>
        internal static void SaveWorldData(TagCompound tag) {
            List<TagCompound> list = [];

            TagCompound saveData = [];
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp == null || !tp.Active) {
                    continue;
                }

                if (tp.FullName == UnknowTP.UnknowTag && tp is UnknowTP unknowTP) {
                    saveData = [];
                    list.Add(unknowTP.GetData());
                    continue;
                }

                try {
                    tp.SaveData(saveData);
                } catch (Exception ex) {
                    saveData = [];
                    VaultMod.LoggerError($"@SaveWorldData-{tp.ID}", $"SaveWorldData: " +
                        $"An error occurred while trying to save the TP {tp}: {ex.Message}");
                }

                if (saveData.Count == 0) {
                    saveData = [];
                    continue;
                }

                TagCompound thisTag = new() {
                    ["mod"] = tp?.Mod.Name,
                    ["name"] = tp?.GetType().Name,
                    ["X"] = tp.Position.X,
                    ["Y"] = tp.Position.Y,
                    ["data"] = saveData
                };
                saveData = [];

                list.Add(thisTag);
            }
            tag[key_TPData_TagList] = list;
        }

        /// <summary>
        /// 载入世界所有TP实体的存档数据
        /// </summary>
        /// <param name="tag"></param>
        internal static void LoadWorldData(TagCompound tag) {
            if (!tag.ContainsKey(key_TPData_TagList)) {
                return;
            }

            IList<TagCompound> list = tag.GetList<TagCompound>(key_TPData_TagList);

            //遍历标签列表并在字典中查找匹配的 TileProcessor
            foreach (TagCompound thisTag in list) {
                if (!thisTag.TryGet("data", out TagCompound data) || data.Count == 0) {
                    continue;
                }

                if (!thisTag.TryGet("unMod", out string mod)) {
                    mod = thisTag.GetString("mod");
                }
                if (!thisTag.TryGet("unType", out string name)) {
                    name = thisTag.GetString("name");
                }

                string fullName = VaultType<TileProcessor>.GetFullName(mod, name);
                Point16 point = new(thisTag.GetShort("X"), thisTag.GetShort("Y"));

                //从字典中查找匹配项
                if (TP_NameAndPoint_To_Instance.TryGetValue((fullName, point), out TileProcessor tp)) {
                    try {
                        tp.LoadData(data);
                    } catch (Exception ex) {
                        VaultMod.LoggerError($"@LoadWorldData-{tp.ID}", $"LoadWorldData: " +
                            $"An error occurred while trying to save the TP {tp}: {ex.Message}");
                    }
                }
                else {
                    try {
                        _ = UnknowTP.Place(point, data, mod, name);
                    } catch (Exception ex) {
                        VaultMod.LoggerError($"@LoadWorldData-{tp.ID}", $"LoadWorldData: " +
                            $"An error occurred while trying to save the TP {tp}: {ex.Message}");
                    }
                }
            }
        }

        #region Utils
        /// <summary>
        /// 移除对应TP实体在字典中的记录，目前仅在<see cref="TileProcessor.Kill"/>中被使用
        /// 如果你考虑自行调用，请保证清楚自己在做什么，在错误的时机中移除活跃实体的字典存据会导致多种问题
        /// </summary>
        /// <param name="tp"></param>
        public static void RemoveFromDictionaries(TileProcessor tp) {
            //并行阶段禁止结构性修改字典，延迟到Phase 2主线程执行
            if (TileProcessorParallel.InParallelPhase) {
                TileProcessorParallel.Defer(() => RemoveFromDictionaries(tp));
                return;
            }

            TP_IDAndPoint_To_Instance.Remove((tp.ID, tp.Position));
            TP_NameAndPoint_To_Instance.Remove((tp.FullName, tp.Position));
            //因为Point_To_Instance只考虑位置，所以在某些情况下可能出现实体顶替的情况，一个萝卜一个坑，移除时判断一下ID避免误杀
            if (TP_Point_To_Instance.TryGetValue(tp.Position, out var existing) && existing.ID == tp.ID) {
                TP_Point_To_Instance.Remove(tp.Position);
            }
        }

        //集中管理所有TryIsTopLeftPoint钩子
        internal static bool? TileProcessorPlaceInWorldTryIsTopLeftPoint(int i, int j, out Point16 position) {
            bool? reset = null;
            position = default;
            foreach (var tpGlobal in HookTryIsTopLeftPoint.Enumerate()) {
                bool? newReset = tpGlobal.TryIsTopLeftPoint(i, j, out var newPosition);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
                if (newPosition != default) {
                    position = newPosition;
                }
            }
            return reset;
        }

        /// <summary>
        /// 判断给定坐标是否为多结构物块的左上角位置，并输出左上角的坐标
        /// 会考虑到<see cref="TileProcessorPlaceInWorldTryIsTopLeftPoint"/>的修改
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <param name="point">输出的左上角坐标，如果不是左上角则为(0,0)</param>
        /// <returns>如果是左上角，返回true，否则返回<see langword="false"/></returns>
        public static bool TileProcessorIsTopLeft(int i, int j, out Point16 point) {
            bool flag = VaultUtils.IsTopLeft(i, j, out point);
            bool? gflag = TileProcessorPlaceInWorldTryIsTopLeftPoint(i, j, out Point16 gpoint);
            if (gflag.HasValue) {
                point = gpoint;
                flag = gflag.Value;
            }

            return flag;
        }

        //集中管理所有GetTopLeftPoint钩子
        internal static Point16? TileProcessorPlaceInWorldGetTopLeftPoint(int i, int j) {
            Point16? point16 = null;
            foreach (var tpGlobal in HookGetTopLeftPoint.Enumerate()) {
                Point16? newPoint16 = tpGlobal.GetTopLeftPoint(i, j);
                if (newPoint16.HasValue) {
                    point16 = newPoint16;
                }
            }
            return point16;
        }

        /// <summary>
        /// 安全的获取多结构物块左上角的位置，给定一个物块坐标，自动寻找到该坐标对应的左上原点位置输出
        /// 会考虑到<see cref="TileProcessorPlaceInWorldGetTopLeftPoint"/>的修改
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <param name="point">输出的左上角坐标</param>
        /// <returns>如果没能找到，则输出(0,0)，并返回<see langword="false"/></returns>
        public static bool TileProcessorSafeGetTopLeft(int i, int j, out Point16 point) {
            bool flag = VaultUtils.SafeGetTopLeft(i, j, out point);
            Point16? gpoint = TileProcessorPlaceInWorldGetTopLeftPoint(i, j);
            if (gpoint.HasValue) {
                point = gpoint.Value;
                flag = true;
            }

            return flag;
        }

        /// <summary>
        /// 将当前世界的<see cref="TP_InWorld"/>转化为一个字典列表
        /// </summary>
        /// <returns></returns>
        public static Dictionary<(string, Point16), TileProcessor> GetTileProcessorDictionaryByNameAndPosition() {
            Dictionary<(string, Point16), TileProcessor> tpDictionary = [];
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp == null) {
                    continue;
                }
                tpDictionary[(tp.FullName, tp.Position)] = tp;
            }
            return tpDictionary;
        }

        //按类型缓存模块ID避免每次泛型查询都走Dictionary<Type,int>哈希
        //模块ID在单次加载会话内恒定，且tModLoader重载会创建全新类型(静态字段自然重置)，故无需手动失效
        private static class ModuleIDCache<T> where T : TileProcessor
        {
            internal static int Value = -1;
        }

        /// <summary>
        /// 根据指定类型获取对应的模块ID
        /// </summary>
        /// <returns>返回该类型对应的模块ID</returns>
        public static int GetModuleID<T>() where T : TileProcessor {
            int id = ModuleIDCache<T>.Value;
            if (id < 0) {
                id = ModuleIDCache<T>.Value = TP_Type_To_ID[typeof(T)];
            }
            return id;
        }

        /// <summary>
        /// 根据指定类型获取对应的模块ID
        /// </summary>
        /// <param name="type">模块的类型</param>
        /// <returns>返回该类型对应的模块ID</returns>
        public static int GetModuleID(Type type) => TP_Type_To_ID[type];

        /// <summary>
        /// 根据指定内部名获取对应的模块ID
        /// </summary>
        /// <param name="name">模块的内部名</param>
        /// <returns>返回该类型对应的模块ID</returns>
        public static int GetModuleID(string name) => TP_FullName_To_ID[name];

        /// <summary>
        /// 根据指定内部名获取对应的模块ID
        /// </summary>
        /// <param name="name">模块的内部名</param>
        /// <param name="id">输出id</param>
        /// <returns>返回该类型对应的模块ID</returns>
        public static bool TryGetTpID(string name, out int id) => TP_FullName_To_ID.TryGetValue(name, out id);

        /// <summary>
        /// 输入任意一点，自动寻找该点物块的左上角并寻找到对应的TP实体，适合用于多结构物块的实体搜寻，
        /// 本质是 VaultUtils.SafeGetTopLeft 与 ByPositionGetTP 的简写情况
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="point"></param>
        /// <param name="tileProcessor"></param>
        /// <returns></returns>
        public static bool AutoPositionGetTP<T>(Point16 point, out T tileProcessor) where T : TileProcessor
            => AutoPositionGetTP(point.X, point.Y, out tileProcessor);

        /// <summary>
        /// 输入任意一点，自动寻找该点物块的左上角并寻找到对应的TP实体，适合用于多结构物块的实体搜寻，
        /// 本质是 VaultUtils.SafeGetTopLeft 与 ByPositionGetTP 的简写情况
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="tileProcessor"></param>
        /// <returns></returns>
        public static bool AutoPositionGetTP<T>(int i, int j, out T tileProcessor) where T : TileProcessor {
            tileProcessor = null;
            if (!VaultUtils.SafeGetTopLeft(i, j, out var point)) {
                return false;
            }
            return ByPositionGetTP<T>(point, out tileProcessor);
        }

        /// <summary>
        /// 根据点来寻找对应的TP实体实例
        /// </summary>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="tileProcessor"></param>
        /// <returns></returns>
        public static bool ByPositionGetTP<T>(int x, int y, out T tileProcessor) where T : TileProcessor {
            tileProcessor = FindModulePreciseSearch<T>(x, y);
            return tileProcessor != null;
        }

        /// <summary>
        /// 根据点来寻找对应的TP实体实例
        /// </summary>
        /// <param name="position"></param>
        /// <param name="tileProcessor"></param>
        /// <returns></returns>
        public static bool ByPositionGetTP<T>(Point16 position, out T tileProcessor) where T : TileProcessor {
            tileProcessor = FindModulePreciseSearch<T>(position.X, position.Y);
            return tileProcessor != null;
        }

        /// <summary>
        /// 根据点来寻找对应的TP实体实例，这个方法只适用于一个物块上只附着一个TP实体的情况
        /// </summary>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="tileProcessor">返回坐标对应的模块，如果未找到则返回<see langword="null"/></param>
        /// <returns></returns>
        public static bool ByPositionGetTP(int x, int y, out TileProcessor tileProcessor)
            => TP_Point_To_Instance.TryGetValue(new(x, y), out tileProcessor);

        /// <summary>
        /// 根据点来寻找对应的TP实体实例，这个方法只适用于一个物块上只附着一个TP实体的情况
        /// </summary>
        /// <param name="id">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="tileProcessor">返回坐标对应的模块，如果未找到则返回<see langword="null"/></param>
        /// <returns></returns>
        public static bool ByPositionGetTP(int id, int x, int y, out TileProcessor tileProcessor)
            => TP_IDAndPoint_To_Instance.TryGetValue((id, new Point16(x, y)), out tileProcessor);

        /// <summary>
        /// 根据点来寻找对应的TP实体实例，这个方法只适用于一个物块上只附着一个TP实体的情况
        /// </summary>
        /// <param name="point">要查找的模块的坐标</param>
        /// <param name="tileProcessor">返回与指定ID及坐标对应的模块，如果未找到则返回<see langword="null"/></param>
        /// <returns></returns>
        public static bool ByPositionGetTP(Point16 point, out TileProcessor tileProcessor)
            => TP_Point_To_Instance.TryGetValue(point, out tileProcessor);

        /// <summary>
        /// 根据点来寻找对应的TP实体实例，这个方法只适用于一个物块上只附着一个TP实体的情况
        /// </summary>
        /// <param name="id">要查找的模块的ID</param>
        /// <param name="point">要查找的模块的坐标</param>
        /// <param name="tileProcessor">返回坐标对应的模块，如果未找到则返回<see langword="null"/></param>
        /// <returns></returns>
        public static bool ByPositionGetTP(int id, Point16 point, out TileProcessor tileProcessor)
            => TP_IDAndPoint_To_Instance.TryGetValue((id, point), out tileProcessor);

        /// <summary>
        /// 根据点来寻找对应的TP实体实例，这个方法只适用于一个物块上只附着一个TP实体的情况
        /// </summary>
        /// <param name="loadenName">要查找的模块的内部名</param>
        /// <param name="point">要查找的模块的坐标</param>
        /// <param name="tileProcessor">返回坐标对应的模块，如果未找到则返回<see langword="null"/></param>
        /// <returns></returns>
        public static bool ByPositionGetTP(string loadenName, Point16 point, out TileProcessor tileProcessor)
            => TP_NameAndPoint_To_Instance.TryGetValue((loadenName, point), out tileProcessor);

        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <returns>返回与指定ID及坐标对应的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModulePreciseSearch<T>(int x, int y) where T : TileProcessor => FindModulePreciseSearch(GetModuleID<T>(), x, y) as T;

        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="point">要查找的模块的坐标</param>
        /// <returns>返回与指定ID及坐标对应的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModulePreciseSearch<T>(Point16 point) where T : TileProcessor => FindModulePreciseSearch(GetModuleID<T>(), point.X, point.Y) as T;

        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块
        /// </summary>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="point">要查找的模块的坐标</param>
        /// <returns>返回与指定ID及坐标对应的 <see cref="TileProcessor"/>，如果未找到则返回<see langword="null"/></returns>
        public static TileProcessor FindModulePreciseSearch(int ID, Point16 point) => FindModulePreciseSearch(ID, point.X, point.Y);

        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块
        /// </summary>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <returns>返回与指定ID及坐标对应的 <see cref="TileProcessor"/>，如果未找到则返回<see langword="null"/></returns>
        public static TileProcessor FindModulePreciseSearch(int ID, int x, int y) {
            //我们必须理解这个函数的调用环境是相当恐怖的，可以预料到该函数将被极高频率的调用，所以哈希优化是必要的
            //极高频快速路径：TP实体在注册时(NewTPInWorld/AddInWorld)其Position必然已被解析为左上角原点，
            //因此字典里的键天然只会落在左上角上，命中即代表(x,y)就是合法的左上角，等价于完整的IsTopLeft判定
            //如此便可省去昂贵的GetTopLeftOrNull(内部含Framing.GetTileSafely与TileObjectData.GetTileData)解析
            //仅当不存在TryIsTopLeftPoint全局钩子(它可能把任意坐标改写为左上角)时启用，确保行为与慢路径完全一致
            if (HookTryIsTopLeftPoint == null || HookTryIsTopLeftPoint.Enumerate().IsEmpty) {
                return TP_IDAndPoint_To_Instance.TryGetValue((ID, new Point16(x, y)), out var fast) ? fast : null;
            }

            //存在TryIsTopLeftPoint钩子时，可能需要把内部坐标改写为左上角，走完整解析路径以保持原有语义
            if (!TileProcessorIsTopLeft(x, y, out Point16 point)) {
                return null;
            }
            if (TP_IDAndPoint_To_Instance.TryGetValue((ID, point), out var tileProcessor)) {
                return tileProcessor;
            }

            return null;
        }

        /// <summary>
        /// 在指定范围内查找与指定ID和坐标最接近的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="maxFindLeng">搜索范围的最大距离</param>
        /// <returns>返回与指定ID及坐标最接近的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModuleRangeSearch<T>(int x, int y, int maxFindLeng) where T : TileProcessor => FindModuleRangeSearch(GetModuleID<T>(), x, y, maxFindLeng) as T;

        /// <summary>
        /// 在指定范围内查找与指定ID和坐标最接近的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="point">要查找的模块的坐标</param>
        /// <param name="maxFindLeng">搜索范围的最大距离</param>
        /// <returns>返回与指定ID及坐标最接近的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModuleRangeSearch<T>(Point16 point, int maxFindLeng) where T : TileProcessor => FindModuleRangeSearch(GetModuleID<T>(), point.X, point.Y, maxFindLeng) as T;

        /// <summary>
        /// 在指定范围内查找与指定ID和坐标最接近的模块
        /// </summary>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="maxFindLeng">搜索范围的最大距离，单位为物块图格距离</param>
        /// <returns>返回与指定ID及坐标最接近的 <see cref="TileProcessor"/>，如果未找到则返回<see langword="null"/></returns>
        public static TileProcessor FindModuleRangeSearch(int ID, int x, int y, int maxFindLeng) {
            TileProcessor module = null;
            float findSquaredValue = maxFindLeng * maxFindLeng * 256; //平方距离（像素）
            Vector2 position = new Vector2(x, y) * 16;

            foreach (KeyValuePair<(int, Point16), TileProcessor> kvp in TP_IDAndPoint_To_Instance) {
                if (kvp.Key.Item1 != ID) {
                    continue; //筛选ID
                }

                var inds = kvp.Value;
                //平方计算进行搜索
                float value = (position - inds.PosInWorld).LengthSquared();
                if (value > findSquaredValue) {
                    continue;
                }

                module = inds;
                findSquaredValue = value;
            }

            return module;
        }

        #endregion
    }
}