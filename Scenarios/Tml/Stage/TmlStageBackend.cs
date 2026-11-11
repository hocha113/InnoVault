using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.Graphics.Capture;
using Terraria.ID;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    //换舞台后端。舞台在磁盘上就是一个原版世界文件；对照 tML 源码：WorldGen.SaveAndQuitCallBack（离开）、
    //WorldFile.CreateMetadata 与 WorldGenerator.GenerateWorld（生成）、WorldGen.playWorldCallBack（单人进入）、
    //WorldGen.serverLoadWorldCallBack（服务器读档）、Netplay.InnerClientLoop 与 MessageBuffer.GetData 的 3/6/7/8/12/49/129 号包（客户端进服）；升级 tML 后逐个核对。
    //单人：后台线程存盘、换文件、生成、读档。服务器：同样的步骤在主线程上同步做完。客户端：没有世界文件，卸载本地世界后在原连接上重新接收服务器的新舞台
    internal sealed class TmlStageBackend : IScenarioStageBackend
    {
        //与 WorldFile.CreateMetadata 相同；不小于 777389080577 时地图文件按世界 GUID 命名
        private const ulong GeneratorVersion = 1198295875585uL;
        //BackupIO 是 internal 类型：为真时 tML 保存世界不再往 Worlds/Backups 归档
        private static readonly FieldInfo _archiveLock = typeof(Main).Assembly
            .GetType("Terraria.ModLoader.BackupIO")?.GetField("archiveLock", BindingFlags.Public | BindingFlags.Static);
        //WorldFile.IOLock 是 private：原版存世界时拿不到它就直接跳过这次存盘
        private static readonly FieldInfo _worldIOLock = typeof(WorldFile).GetField("IOLock", BindingFlags.NonPublic | BindingFlags.Static);
        //Player._temporaryItemSlots 是 internal：读角色文件那一刻光标、旅途研究槽、向导槽、重铸槽里的物品
        private static readonly FieldInfo _temporaryItemSlots = typeof(Player).GetField("_temporaryItemSlots", BindingFlags.NonPublic | BindingFlags.Instance);

        //客户端等服务器下发新舞台的最长时间，超过后把加载画面换成带取消按钮的连接状态页
        private static readonly TimeSpan RejoinPatience = TimeSpan.FromSeconds(60);

        private WorldFileData _root;
        private Guid _rootId;
        private bool _rootArchiveLock;
        //这一端是否拥有世界文件（单人与服务器）。在会话开始时记下：客户端掉线后原版立刻把 netMode 改回单人，那之后才轮到会话收尾
        private bool _ownsWorldFiles;
        private Vector2? _returnAnchor;
        private volatile bool _busy;
        private volatile bool _showingGenerationProgress;
        //客户端：这次换舞台的菜单阶段开始的时刻，不在该阶段时为空
        private DateTime? _rejoinStarted;

        //客户端：服务器迟迟没有下发新舞台，加载画面已经让位给原版的连接状态页（带取消按钮）
        internal static bool RejoinStalled { get; private set; }

        private string ScenarioDirectory => Path.Combine(VaultSave.RootPath, "Scenarios", _rootId.ToString("N"));
        private string QuitRecordPath => Path.Combine(ScenarioDirectory, "quit.txt");

        public string CheckReady(ScenarioTransition transition) {
            if (_busy) {
                return "a previous travel is still running";
            }
            if (_root == null) {
                return "root world identity is unknown";
            }
            if (Main.dedServ) {
                //服务器没有本地玩家；玩家是否适合被带走由场景的否决钩子与裁决钩子决定
                return SubworldLibraryInterop.AnySubworldActive() ? "a Subworld Library subworld is active" : null;
            }
            if (Main.gameMenu) {
                return "the game is on the menu";
            }
            Player player = Main.LocalPlayer;
            if (player == null || !player.active) {
                return "the local player is not active";
            }
            if (player.dead) {
                return "the local player is dead";
            }
            if (SubworldLibraryInterop.AnySubworldActive()) {
                return "a Subworld Library subworld is active";
            }
            return null;
        }

        public void Begin(ScenarioTransition transition) {
            if (Main.netMode == NetmodeID.Server) {
                BeginServer(transition);
                return;
            }
            if (Main.netMode == NetmodeID.MultiplayerClient) {
                BeginClient(transition);
                return;
            }
            _busy = true;
            //PreSaveAndQuit 会派发到场景系统的 EndSession，它按阶段认出这是换舞台途中而忽略
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Saving;
            if (transition.From is RootScenario) {
                _returnAnchor = Main.LocalPlayer.position;
            }
            try {
                SystemLoader.PreSaveAndQuit();
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.PreSaveAndQuit", $"[Scenarios] PreSaveAndQuit threw during travel: {ex}");
            }
            Main.menuMode = 10;
            Main.gameMenu = true;
            Main.statusText = ScenarioText.Saving.Value;
            //切换通常在一帧更新的中途发起；主线程动作队列在本帧 DoUpdate 结束后才执行，那时再开线程，
            //存盘不会与本帧剩余的实体更新并发，下一帧的 DoUpdate 也已经被 gameMenu 停住
            Main.QueueMainThreadAction(() => ThreadPool.QueueUserWorkItem(_ => Run(transition)));
        }

        //（单人）落点：请求的锚点、入口锚点；回到主世界时没有请求锚点就回到离开前的位置
        public void OnPlayerEntered(ScenarioTransition transition) {
            Player player = Main.LocalPlayer;
            bool home = transition.To is RootScenario;
            if (transition.Anchor != null) {
                if (ScenarioDirector.TryFindAnchor(transition.Anchor, out Point requested)) {
                    ScenarioPlacement.PlaceAtTile(player, requested);
                    _returnAnchor = null;
                    return;
                }
                ScenarioPlatform.Info($"[Scenarios] Anchor '{transition.Anchor}' was not found in {transition.To?.Key}; using the default entry");
            }
            if (home) {
                if (_returnAnchor is Vector2 anchor) {
                    ScenarioPlacement.PlaceAt(player, anchor);
                }
                _returnAnchor = null;
            }
            else if (ScenarioDirector.TryFindAnchor(ScenarioAnchors.Entry, out Point entry)) {
                ScenarioPlacement.PlaceAtTile(player, entry);
            }
        }

        //客户端上 Main.ActiveWorldFileData 是按服务器发来的世界信息填的空壳（含世界 GUID），这里只用它认身份，不读写任何舞台文件
        public void OnSessionStarted() {
            _root = Main.ActiveWorldFileData;
            _rootId = _root.UniqueId != Guid.Empty
                ? _root.UniqueId
                : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(_root.Path ?? _root.Name ?? string.Empty)));
            _rootArchiveLock = GetArchiveLock();
            _ownsWorldFiles = Main.netMode != NetmodeID.MultiplayerClient;
            _returnAnchor = null;
        }

        //退出时所在的舞台记在拥有世界文件的一端：单人与服务器
        public void OnSessionEnding(VaultScenario stage) {
            if (_root == null || !_ownsWorldFiles) {
                return;
            }
            try {
                if (stage == null || stage is RootScenario) {
                    TryDelete(QuitRecordPath);
                    return;
                }
                Directory.CreateDirectory(ScenarioDirectory);
                File.WriteAllText(QuitRecordPath, stage.Template.Key);
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.QuitRecord", $"[Scenarios] Failed to record the quit location: {ex.Message}");
            }
        }

        public string ConsumeQuitLocation() {
            if (_root == null || !_ownsWorldFiles || !File.Exists(QuitRecordPath)) {
                return null;
            }
            try {
                string key = File.ReadAllText(QuitRecordPath).Trim();
                TryDelete(QuitRecordPath);
                return key.Length == 0 ? null : key;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.QuitRecord", $"[Scenarios] Failed to read the quit location: {ex.Message}");
                return null;
            }
        }

        public void OnWorldUnloadedOutsideTravel() {
            if (_root != null && _ownsWorldFiles) {
                SetArchiveLock(_rootArchiveLock);
            }
            _root = null;
            _returnAnchor = null;
            _rejoinStarted = null;
            RejoinStalled = false;
        }

        public void Reset() {
            _root = null;
            _returnAnchor = null;
            _busy = false;
            _showingGenerationProgress = false;
            _rejoinStarted = null;
            RejoinStalled = false;
        }

        #region 单人

        //线程池上执行：离开、进入；任何一步失败都退回根世界，根世界也进不去就结束会话
        private void Run(ScenarioTransition transition) {
            try {
                Leave(transition.From);
                if (!Enter(transition)) {
                    FallBackHome(transition, "the target stage failed to load");
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Travel.Run", $"[Scenarios] Travel to {transition.To?.Key} failed: {ex}");
                FallBackHome(transition, ex.Message);
            } finally {
                _showingGenerationProgress = false;
                _busy = false;
            }
        }

        //WorldGen.SaveAndQuitCallBack 去掉回主菜单的部分；一次性舞台不存盘，离开后删除其文件
        private void Leave(VaultScenario from) {
            ScenarioStage fromStage = from?.Stage;
            bool transient = fromStage is { BindsRootWorld: false, Persistence: StagePersistence.Transient };
            string fromPath = Main.ActiveWorldFileData?.Path;
            Guid fromId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;

            SoundEngine.StopTrackedSounds();
            CaptureInterface.ResetFocus();
            Main.ActivePlayerFileData.StopPlayTimer();
            //原版只在 mapEnabled 为真时随玩家一起保存地图；地图策略关掉它期间探索过的部分不能丢
            ScenarioMapPolicy.Reset();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Player.ClearPlayerTempInfo();
            Rain.ClearRain();
            if (!transient) {
                WorldFile.CacheSaveTime();
                WorldFile.SaveWorld();
            }
            //先等 InnoVault 的后台存盘写完再派发卸载：它在执行时才读路径与各模组状态，卸载会清空后者
            VaultSave.WaitForPendingSave();
            SystemLoader.OnWorldUnload();
            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();
            if (transient) {
                DeleteStageFiles(fromPath, fromId);
            }
        }

        private bool Enter(ScenarioTransition transition) {
            WorldFileData target = PrepareTarget(transition);
            target.SetAsActive();
            //原版菜单在主线程读取玩家；模组的 ModPlayer.LoadData 可能碰图形资源，离开主线程会出错
            RunOnMainThread(ReloadPlayer);
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Loading;
            WorldGen.playWorldCallBack(null);
            return !WorldGen.loadFailed && WorldGen.loadSuccess;
        }

        #endregion

        #region 服务器

        //在主线程上同步换世界。此时所有客户端都已置为在途，正盖着幕布卸载各自的旧舞台；换完之前服务器不处理任何网络包
        private void BeginServer(ScenarioTransition transition) {
            _busy = true;
            object ioLock = _worldIOLock?.GetValue(null);
            bool locked = false;
            try {
                //持有原版的世界存盘锁直到换完：正在进行的后台存盘（黎明自动存盘）先写完；排在后面才开始的拿不到锁会跳过，
                //不会把换到一半的世界写进文件。锁可重入，我们自己调用的 WorldFile.SaveWorld 不受影响
                if (ioLock != null) {
                    Monitor.Enter(ioLock, ref locked);
                }
                try {
                    LeaveServer(transition.From);
                    if (!EnterServer(transition)) {
                        FallBackHome(transition, "the target stage failed to load");
                    }
                } catch (Exception ex) {
                    ScenarioPlatform.Error("Scenario.Travel.Run", $"[Scenarios] Travel to {transition.To?.Key} failed: {ex}");
                    FallBackHome(transition, ex.Message);
                }
            } finally {
                if (locked) {
                    Monitor.Exit(ioLock);
                }
                _showingGenerationProgress = false;
                _busy = false;
            }
        }

        //与服务器自己的退出路径相同（Main.DedServ 末尾）：存世界、派发卸载；一次性舞台不存盘，离开后删除其文件
        private static void LeaveServer(VaultScenario from) {
            ScenarioStage fromStage = from?.Stage;
            bool transient = fromStage is { BindsRootWorld: false, Persistence: StagePersistence.Transient };
            string fromPath = Main.ActiveWorldFileData?.Path;
            Guid fromId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;

            Main.statusText = ScenarioText.Saving.Value;
            Rain.ClearRain();
            if (!transient) {
                WorldFile.SaveWorld();
            }
            VaultSave.WaitForPendingSave();
            SystemLoader.OnWorldUnload();
            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();
            if (transient) {
                DeleteStageFiles(fromPath, fromId);
            }
        }

        private bool EnterServer(ScenarioTransition transition) {
            WorldFileData target = PrepareTarget(transition);
            target.SetAsActive();
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Loading;
            return LoadOnServer();
        }

        //WorldGen.serverLoadWorldCallBack 去掉备份恢复的部分：读档失败由调用方退回根世界。
        //WorldFile.LoadWorld 末尾的 OnWorldLoad 事件会激活新舞台场景；Hooks.WorldLoaded 让导演层开始等客户端到场
        private static bool LoadOnServer() {
            //生成舞台时 Main.rand 被换成了按种子构造的随机数，与原版读档一样在这里重新按时间播种
            Main.rand = new UnifiedRandom((int)DateTime.Now.Ticks);
            WorldFile.LoadWorld(false);
            if (WorldGen.loadFailed || !WorldGen.loadSuccess) {
                return false;
            }
            WorldFile.SetOngoingToTemps();
            WorldGen.Hooks.WorldLoaded();
            return true;
        }

        public void OnTravelCommitted(IReadOnlyCollection<int> clients) {
            //与正在进服的客户端同样的状态：连接还在，但不在世界里。广播开关关掉后，原版与其他模组的世界同步都不再发给它；
            //角色离场后服务器不再按它的旧位置发区块、刷怪。连接状态保持在 10：低于 10 时客户端发来的游戏包会让它被踢掉
            foreach (int client in clients) {
                NetMessage.buffer[client].broadcast = false;
                Main.player[client].active = false;
            }
            //正在进服、已经拿到旧舞台世界信息的客户端跟不上这次切换，请它重新进服
            for (int i = 0; i < Main.maxPlayers; i++) {
                RemoteClient remote = Netplay.Clients[i];
                if (remote != null && remote.IsConnected() && remote.State is >= 2 and < 10) {
                    NetMessage.BootPlayer(i, ScenarioText.StageChanged.ToNetworkText());
                }
            }
        }

        public void AdmitClient(int client) {
            RemoteClient remote = Netplay.Clients[client];
            if (remote == null || !remote.IsConnected()) {
                return;
            }
            //客户端已经把连接拨回“等待世界信息”：收到这个包它会清世界、请求出生点附近的区块，原版的 8 号包处理随后下发区块与实体
            remote.ResetSections();
            NetMessage.SendData(MessageID.WorldData, client);
        }

        public void OnClientEntered(int client, bool traveler) {
            RemoteClient remote = Netplay.Clients[client];
            if (!traveler || remote == null || !remote.IsConnected()) {
                return;
            }
            //原版只在连接状态从 3 升到 10 时做这几件事（MessageBuffer 12 号包），在途客户端的状态一直是 10，要自己补上
            NetMessage.buffer[client].broadcast = true;
            NetMessage.SyncConnectedPlayer(client);
            //clearWorld 会把所有人的房主标记清掉
            bool host = NetMessage.DoesPlayerSlotCountAsAHost(client);
            Main.countsAsHostForGameplay[client] = host;
            if (host) {
                NetMessage.TrySendData(MessageID.SetCountsAsHostForGameplay, client, -1, null, client, 1f);
            }
        }

        //落点：请求的锚点、入口锚点。回主世界又没有请求锚点时不给落点，各客户端回到自己离开前的位置
        public bool TryResolveLanding(ScenarioTransition transition, out Point tile) {
            tile = default;
            if (transition == null) {
                return false;
            }
            if (transition.Anchor != null) {
                if (ScenarioDirector.TryFindAnchor(transition.Anchor, out tile)) {
                    return true;
                }
                ScenarioPlatform.Info($"[Scenarios] Anchor '{transition.Anchor}' was not found in {transition.To?.Key}; using the default entry");
            }
            return transition.To is not RootScenario && ScenarioDirector.TryFindAnchor(ScenarioAnchors.Entry, out tile);
        }

        #endregion

        #region 客户端

        private void BeginClient(ScenarioTransition transition) {
            _busy = true;
            if (transition.From is RootScenario) {
                _returnAnchor = Main.LocalPlayer.position;
            }
            try {
                SystemLoader.PreSaveAndQuit();
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.PreSaveAndQuit", $"[Scenarios] PreSaveAndQuit threw during travel: {ex}");
            }
            //与原版进服时一样停在菜单状态：世界不更新也不绘制，网络包照常处理。用没有按钮的状态文字页（加载画面盖在上面，按钮看不见却点得到）；
            //连接断开、或者服务器迟迟不下发新舞台时，再换成带取消按钮的连接状态页
            Main.menuMode = 10;
            Main.gameMenu = true;
            Main.statusText = (transition.To is RootScenario ? ScenarioText.ReturningHome : ScenarioText.Entering).Value;
            RejoinStalled = false;
            _rejoinStarted = DateTime.UtcNow;
            Main.QueueMainThreadAction(() => ThreadPool.QueueUserWorkItem(_ => RunClient(transition)));
        }

        //（客户端）每帧在主线程调用，菜单期间也调用。换舞台的菜单阶段没有取消按钮：服务器迟迟不下发新舞台时，
        //让加载画面让位给原版的连接状态页，玩家可以自己取消连接，不至于只能强退游戏
        internal void WatchRejoin() {
            if (_rejoinStarted is not DateTime started) {
                return;
            }
            if (!Main.gameMenu || Main.netMode != NetmodeID.MultiplayerClient || !ScenarioDirector.InBackendTravel) {
                _rejoinStarted = null;
                return;
            }
            if (DateTime.UtcNow - started < RejoinPatience) {
                return;
            }
            _rejoinStarted = null;
            RejoinStalled = true;
            ScenarioPlatform.Info($"[Scenarios] The server has not delivered the new stage after {RejoinPatience.TotalSeconds:0} seconds; showing the connection screen so the player can cancel");
            if (Main.menuMode == 10) {
                Main.menuMode = 14;
            }
        }

        //线程池上执行：卸载本地的旧舞台，然后回主线程把连接拨回“等待世界信息”并通知服务器
        private void RunClient(ScenarioTransition transition) {
            try {
                LeaveClient(transition.From);
                RunOnMainThread(RejoinStage);
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Travel.Client", $"[Scenarios] Leaving the stage on this client failed; disconnecting from the server: {ex}");
                Main.QueueMainThreadAction(static () => {
                    Main.statusText = Language.GetTextValue("Net.LostConnection");
                    Netplay.Disconnect = true;
                });
            } finally {
                _busy = false;
            }
        }

        //WorldGen.SaveAndQuitCallBack 的联机分支去掉断开连接的部分：存角色与这块舞台的地图，派发卸载。客户端没有世界文件可存。
        //光标上的物品留在原处（不调用 ClearPlayerTempInfo），重新出生前刷新快照即可
        private static void LeaveClient(VaultScenario from) {
            ScenarioStage fromStage = from?.Stage;
            bool transient = fromStage is { BindsRootWorld: false, Persistence: StagePersistence.Transient };
            Guid fromId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;

            SoundEngine.StopTrackedSounds();
            CaptureInterface.ResetFocus();
            Main.ActivePlayerFileData.StopPlayTimer();
            ScenarioMapPolicy.Reset();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Rain.ClearRain();
            SystemLoader.OnWorldUnload();
            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();
            if (transient) {
                DeleteMap(fromId);
            }
        }

        //原版客户端循环在连接存续期间一直按 Connection.State 驱动进服握手：状态 3 收到世界信息后进入 4（清世界、OnWorldLoad、读地图），
        //再请求出生点附近的区块，收到 49 号包后以 SpawningIntoWorld 出生。把状态拨回 3，整套流程就在原连接上重走一遍
        private static void RejoinStage() {
            Player local = Main.LocalPlayer;
            RefreshOutOfClassSnapshot(local);
            //打开着的箱子、告示牌、对话中的 NPC 记的都是旧舞台里的编号。单人换舞台会重新读取角色，这些自然清空；这里角色对象不换，要自己清。
            //抵达后原版的逐帧比对（Main.TrySyncingMyPlayer）会把这些变化告诉服务器
            local.chest = -1;
            local.editedChestName = false;
            local.sign = -1;
            local.SetTalkNPC(-1);
            //与刚进服时一样从“没有其他玩家”开始，谁在新舞台里由服务器重新告知（出生后的 SyncConnectedPlayer）。
            //不这样做的话，还没抵达的玩家会停在旧舞台里的坐标上，换舞台途中掉线的玩家则会一直留在画面里（它的离开通知发不到在途的客户端）
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (i != Main.myPlayer) {
                    Main.player[i].active = false;
                }
            }
            RemoteServer connection = Netplay.Connection;
            connection.StatusText = string.Empty;
            connection.StatusCount = 0;
            connection.StatusMax = 0;
            Main.statusText = Language.GetTextValue("Net.RequestingWorldInformation");
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Loading;
            connection.State = 3;
            ScenarioDirector.NotifyLeftStage();
        }

        //Spawn(SpawningIntoWorld) 会按读取角色文件那一刻的快照覆盖光标、旅途研究槽、向导槽、重铸槽里的物品（Player.SetPlayerDataToOutOfClassFields）。
        //单人换舞台靠从磁盘重新读取角色来刷新它；联机时不能中途换掉角色对象（其他端持有它的同步状态），改为直接把快照换成当前的内容
        private static void RefreshOutOfClassSnapshot(Player player) {
            if (_temporaryItemSlots?.GetValue(player) is not Item[] { Length: >= 4 } slots) {
                ScenarioPlatform.Error("Scenario.TemporarySlots", "[Scenarios] Player._temporaryItemSlots was not found; an item held on the cursor may be lost when the stage changes in multiplayer");
                return;
            }
            slots[0] = CloneOrNull(Main.mouseItem);
            slots[1] = CloneOrNull(Main.CreativeMenu.GetItemByIndex(0));
            slots[2] = CloneOrNull(Main.guideItem);
            slots[3] = CloneOrNull(Main.reforgeItem);
        }

        private static Item CloneOrNull(Item item) => item == null || item.IsAir ? null : item.Clone();

        public void OnLocalPlayerEntered() {
            _rejoinStarted = null;
            RejoinStalled = false;
            //原版等服务器的 129 号包才结束“等待世界数据”并离开菜单状态，而那个包只在连接状态从 3 升到 10 时发；这里照它的处理自己做
            Main.FixUIScale();
            Main.TrySetPreparationState(Main.WorldPreparationState.ProcessingData);
        }

        public void PlaceLocalPlayer(ScenarioTransition transition, Point? landing) {
            Player player = Main.LocalPlayer;
            if (landing is Point tile) {
                ScenarioPlacement.PlaceAtTile(player, tile);
                _returnAnchor = null;
            }
            else if (ScenarioDirector.Stage is RootScenario) {
                if (_returnAnchor is Vector2 anchor) {
                    ScenarioPlacement.PlaceAt(player, anchor);
                }
                _returnAnchor = null;
            }
            //立刻把落点告诉服务器：它按玩家位置下发区块，其他客户端也据此看到这个玩家
            NetMessage.SendData(MessageID.PlayerControls, -1, -1, null, Main.myPlayer);
        }

        public bool IsLocalPlayerLoaded() {
            Point tile = Main.LocalPlayer.Center.ToTileCoordinates();
            return Main.sectionManager != null
                && Main.sectionManager.SectionLoaded(Netplay.GetSectionX(tile.X), Netplay.GetSectionY(tile.Y));
        }

        #endregion

        //目标舞台的世界文件：根场景用菜单选中的那个；场景舞台按需生成，否则读回上次的文件与生成期间写下的锚点。在拥有世界文件的一端调用
        private WorldFileData PrepareTarget(ScenarioTransition transition) {
            VaultScenario to = transition.To;
            ScenarioStage stage = to.Stage;
            if (stage.BindsRootWorld) {
                SetArchiveLock(_rootArchiveLock);
                Main.statusText = ScenarioText.ReturningHome.Value;
                return _root;
            }
            SetArchiveLock(true);
            string path = GetStagePath(to);
            TagCompound meta = ReadMeta(path);
            bool regenerate = stage.Persistence == StagePersistence.Transient
                || !File.Exists(path)
                || meta == null
                || meta.GetInt("version") != stage.Version;
            WorldFileData target;
            if (regenerate) {
                target = Generate(to, stage, path);
            }
            else {
                target = WorldFile.GetAllMetadata(path, false);
                ApplyPersistedAnchors(to, meta);
            }
            Main.statusText = ScenarioText.Entering.Value;
            return target;
        }

        //对照 WorldFile.CreateMetadata 建元数据，对照 WorldGenerator.GenerateWorld 跑生成步骤（自己处理异常，不走原版的致命错误页）
        private WorldFileData Generate(VaultScenario scenario, ScenarioStage stage, string path) {
            Main.statusText = ScenarioText.Generating.Value;
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Generating;
            Guid id = DeriveGuid(scenario.Key);
            DeleteStageFiles(path, id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            int seed = stage.Seed ?? (BitConverter.ToInt32(id.ToByteArray(), 4) & int.MaxValue);
            WorldFileData data = new(path, false) {
                Name = scenario.Name,
                GameMode = Main.GameMode,
                CreationTime = DateTime.Now,
                Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                WorldGeneratorVersion = GeneratorVersion,
                UniqueId = id,
            };
            data.SetSeed(seed.ToString());
            data.SetWorldSize(stage.Width, stage.Height);
            data.SetAsActive();

            ScenarioAnchors anchors = scenario.GetComponent<ScenarioAnchors>();
            Dictionary<string, Point> declared = anchors?.Snapshot();
            WorldGen.generatingWorld = true;
            WorldGen.gen = true;
            try {
                Main.maxTilesX = stage.Width;
                Main.maxTilesY = stage.Height;
                Main.worldName = data.Name;
                Main.worldID = DeriveWorldId(id);
                WorldGen.clearWorld();
                Main.worldSurface = stage.ResolveSurfaceLevel();
                Main.rockLayer = stage.ResolveRockLevel();
                Point spawn = anchors != null && anchors.TryGet(ScenarioAnchors.Entry, out Point entry)
                    ? entry
                    : new Point(stage.Width / 2, (int)Main.worldSurface);
                Main.spawnTileX = spawn.X;
                Main.spawnTileY = spawn.Y;
                WorldGen._genRand = new UnifiedRandom(seed);
                Main.rand = new UnifiedRandom(seed);
                GenVars.structures = new StructureMap();
                WorldGen.RandomizeBackgrounds(WorldGen.genRand);
                WorldGen.RandomizeTreeStyle();
                WorldGen.RandomizeCaveBackgrounds();
                WorldGen.RandomizeMoonState(WorldGen.genRand);
                SystemLoader.PreWorldGen();
                RunPasses(stage, seed);
            } finally {
                WorldGenerator.CurrentGenerationProgress = null;
                _showingGenerationProgress = false;
                WorldGen.gen = false;
                WorldGen.generatingWorld = false;
            }
            Main.menuMode = 10;
            Main.statusText = ScenarioText.Saving.Value;
            WorldFile.SaveWorld(false, resetTime: true);
            WriteMeta(path, stage.Version, CollectGeneratedAnchors(anchors, declared));
            return data;
        }

        private void RunPasses(ScenarioStage stage, int seed) {
            List<GenPass> passes = [];
            stage.CollectPasses(passes);
            passes.RemoveAll(static pass => pass == null || !pass.Enabled);
            if (passes.Count == 0) {
                return;
            }
            WorldGenConfiguration configuration = WorldGenConfiguration.FromEmbeddedPath("Terraria.GameContent.WorldBuilding.Configuration.json");
            GenerationProgress progress = new();
            foreach (GenPass pass in passes) {
                progress.TotalWeight += pass.Weight;
            }
            WorldGenerator.CurrentGenerationProgress = progress;
            if (!Main.dedServ) {
                _showingGenerationProgress = true;
                //原版生成进度界面只在主线程切换；生成很快结束时这次切换就不做了
                Main.QueueMainThreadAction(() => {
                    if (_showingGenerationProgress) {
                        Main.MenuUI.SetState(new UIWorldLoad());
                        Main.menuMode = 888;
                    }
                });
            }
            foreach (GenPass pass in passes) {
                WorldGen._genRand = new UnifiedRandom(seed);
                Main.rand = new UnifiedRandom(seed);
                progress.Start(pass.Weight);
                pass.Apply(progress, configuration.GetPassConfiguration(pass.Name));
                progress.End();
            }
        }

        //生成期间新写入或改动过的锚点；代码里写死的锚点不持久化，随代码更新
        private static Dictionary<string, Point> CollectGeneratedAnchors(ScenarioAnchors anchors, Dictionary<string, Point> declared) {
            Dictionary<string, Point> generated = new(StringComparer.OrdinalIgnoreCase);
            if (anchors == null) {
                return generated;
            }
            foreach (var pair in anchors.Points) {
                if (declared == null || !declared.TryGetValue(pair.Key, out Point before) || before != pair.Value) {
                    generated[pair.Key] = pair.Value;
                }
            }
            return generated;
        }

        private static void ApplyPersistedAnchors(VaultScenario scenario, TagCompound meta) {
            if (scenario.GetComponent<ScenarioAnchors>() is not ScenarioAnchors anchors || !meta.TryGet("anchors", out TagCompound saved)) {
                return;
            }
            foreach (var pair in saved) {
                if (pair.Value is int[] { Length: 2 } xy) {
                    anchors.Set(pair.Key, new Point(xy[0], xy[1]));
                }
            }
        }

        //（单人）从磁盘重新读取玩家：原版出生时用读取玩家文件那一刻的快照恢复光标、向导、重铸槽物品，复用内存里的玩家会复制或丢失它们
        private static void ReloadPlayer() {
            PlayerFileData current = Main.ActivePlayerFileData;
            try {
                PlayerFileData reloaded = Player.LoadPlayer(current.Path, current.IsCloudSave);
                if (reloaded?.Player == null || reloaded.Player.loadStatus != 0) {
                    ScenarioPlatform.Error("Scenario.ReloadPlayer", $"[Scenarios] Reloading the player from {current.Path} failed; keeping the in-memory player");
                    return;
                }
                reloaded.SetAsActive();
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.ReloadPlayer", $"[Scenarios] Reloading the player threw; keeping the in-memory player: {ex}");
            }
        }

        //从线程池把动作交给主线程并等它执行完。菜单期间主线程每帧都会清空动作队列
        private static void RunOnMainThread(Action action) {
            ManualResetEventSlim done = new(false);
            Exception error = null;
            Main.QueueMainThreadAction(() => {
                try {
                    action();
                } catch (Exception ex) {
                    error = ex;
                } finally {
                    done.Set();
                }
            });
            if (!done.Wait(TimeSpan.FromSeconds(30))) {
                throw new TimeoutException("the main thread did not run a queued travel step within 30 seconds");
            }
            if (error != null) {
                throw new InvalidOperationException("a travel step on the main thread failed", error);
            }
        }

        //（单人与服务器）目标舞台进不去：隔离坏文件，改回根世界
        private void FallBackHome(ScenarioTransition failed, string reason) {
            ScenarioPlatform.Error("Scenario.FallBack", $"[Scenarios] Travel to {failed.To?.Key} failed ({reason}); returning to the root world");
            if (failed.To is RootScenario || _root == null) {
                Abort();
                return;
            }
            if (failed.To?.Stage is { BindsRootWorld: false } stage) {
                QuarantineStage(failed.To, stage);
            }
            ScenarioTransition home = ScenarioDirector.RedirectTravelHome(failed);
            if (home == null) {
                Abort();
                return;
            }
            try {
                if (!Main.dedServ) {
                    Main.menuMode = 10;
                    Main.gameMenu = true;
                }
                SetArchiveLock(_rootArchiveLock);
                Main.statusText = ScenarioText.ReturningHome.Value;
                ScenarioDirector.TravelPhase = ScenarioTravelPhase.Loading;
                _root.SetAsActive();
                bool loaded;
                if (Main.dedServ) {
                    loaded = LoadOnServer();
                }
                else {
                    WorldGen.playWorldCallBack(null);
                    loaded = !WorldGen.loadFailed && WorldGen.loadSuccess;
                }
                if (!loaded) {
                    Abort();
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.FallBack", $"[Scenarios] Returning to the root world failed: {ex}");
                Abort();
            }
        }

        //根世界也进不去：结束会话。单人下原版读档失败时会自己切到错误页，这里只在还停在加载画面时回标题；
        //服务器没有可以继续运行的世界，直接停服，并且不存盘（内存里是一个读到一半的世界）
        private static void Abort() {
            ScenarioDirector.AbortTravel();
            if (Main.dedServ) {
                VaultMod.Instance?.Logger.Fatal("[Scenarios] The root world could not be loaded back after a failed stage travel; shutting the server down without saving");
                Netplay.SaveOnServerExit = false;
                Netplay.Disconnect = true;
                return;
            }
            Main.gameMenu = true;
            if (Main.menuMode is 10 or 888) {
                Main.menuMode = 0;
            }
        }

        //读不进来的舞台：一次性的直接删掉；长期保存的改名为 .bad 留作排查，下次进入时重新生成
        private void QuarantineStage(VaultScenario scenario, ScenarioStage stage) {
            string path = GetStagePath(scenario);
            if (stage.Persistence == StagePersistence.Transient) {
                DeleteStageFiles(path, DeriveGuid(scenario.Key));
                return;
            }
            try {
                if (File.Exists(path)) {
                    File.Move(path, path + ".bad", overwrite: true);
                }
                TryDelete(MetaPath(path));
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Quarantine", $"[Scenarios] Failed to quarantine {path}: {ex.Message}");
            }
        }

        private string GetStagePath(VaultScenario scenario)
            => Path.Combine(ScenarioDirectory, $"{_rootId:N}_{SafeFileName(scenario.Key)}.wld");

        internal static string SafeFileName(string key) {
            StringBuilder builder = new(key.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in key) {
                builder.Append(c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString();
        }

        //舞台元数据：生成版本号与生成期间写入的锚点
        private static string MetaPath(string stagePath) => Path.ChangeExtension(stagePath, ".meta");

        private static TagCompound ReadMeta(string stagePath) {
            try {
                return File.Exists(MetaPath(stagePath)) ? TagIO.FromFile(MetaPath(stagePath)) : null;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Meta", $"[Scenarios] Failed to read {MetaPath(stagePath)}; the stage will be regenerated: {ex.Message}");
                return null;
            }
        }

        private static void WriteMeta(string stagePath, int version, Dictionary<string, Point> anchors) {
            try {
                TagCompound saved = [];
                foreach (var pair in anchors) {
                    saved[pair.Key] = new[] { pair.Value.X, pair.Value.Y };
                }
                TagIO.ToFile(new TagCompound { ["version"] = version, ["anchors"] = saved }, MetaPath(stagePath));
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Meta", $"[Scenarios] Failed to write {MetaPath(stagePath)}: {ex.Message}");
            }
        }

        //舞台的 GUID 由根世界 GUID 与场景 Key 确定性派生：同一场景始终复用同一份地图文件
        private Guid DeriveGuid(string key) {
            byte[] root = _rootId.ToByteArray();
            byte[] name = Encoding.UTF8.GetBytes(key);
            byte[] buffer = new byte[root.Length + name.Length];
            root.CopyTo(buffer, 0);
            name.CopyTo(buffer, root.Length);
            return new Guid(MD5.HashData(buffer));
        }

        //玩家的床位出生点表按世界名 + worldID 记录（上限 200 条），worldID 固定才不会每次重新生成都多占一条
        private static int DeriveWorldId(Guid id) {
            int value = BitConverter.ToInt32(id.ToByteArray(), 0) & int.MaxValue;
            return value == 0 ? 1 : value;
        }

        private static void DeleteStageFiles(string path, Guid id) {
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            string twld = Path.ChangeExtension(path, ".twld");
            TryDelete(path);
            TryDelete(path + ".bak");
            TryDelete(twld);
            TryDelete(twld + ".bak");
            TryDelete(MetaPath(path));
            SaveWorld.DeleteWorldData(Path.GetFileNameWithoutExtension(path));
            DeleteMap(id);
            ScenarioRestoreJournal.DeleteFor(id);
        }

        //地图按世界 GUID 存在角色目录下，只有客户端与单人有；服务器上没有角色文件，这里什么都不做
        private static void DeleteMap(Guid id) {
            PlayerFileData player = Main.ActivePlayerFileData;
            if (id == Guid.Empty || string.IsNullOrEmpty(player?.Path) || player.IsCloudSave) {
                return;
            }
            string map = Path.Combine(player.Path[..^4], id + ".map");
            TryDelete(map);
            TryDelete(map + ".bak");
            TryDelete(Path.ChangeExtension(map, ".tmap"));
            TryDelete(Path.ChangeExtension(map, ".tmap") + ".bak");
        }

        private static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Delete.{path}", $"[Scenarios] Failed to delete {path}: {ex.Message}");
            }
        }

        private static bool GetArchiveLock() => _archiveLock?.GetValue(null) is true;

        private static void SetArchiveLock(bool value) => _archiveLock?.SetValue(null, value);
    }
}
