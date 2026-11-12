using InnoVault.GameSystem;
using System.IO;
using Terraria;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //场景框架的游戏驱动：进入舞台开始会话或完成换舞台，每帧推进，保存退出时结束会话，卸载时清空注册表
    internal sealed class ScenarioSystem : ModSystem
    {
        public override void Load() {
            //单人与客户端：由 Player.Spawn(SpawningIntoWorld) 经 EnterWorld 触发，舞台已经就绪、玩家已出生，在主线程；
            //服务器：读完一个世界时触发（开服时在读档线程上，那时还没有客户端、主循环不跑帧；换舞台时在主线程上）
            WorldGen.Hooks.OnWorldLoad += ScenarioDirector.NotifyEnteredStage;
            //WorldFile.LoadWorld 成功结束时触发：各模组 LoadWorldData 之后、玩家出生之前，单人在读档线程上。客户端不读世界文件，不会触发
            WorldFile.OnWorldLoad += OnStageFileLoaded;
            if (!Main.dedServ) {
                Netplay.OnDisconnect += OnDisconnected;
                Main.OnTickForThirdPartySoftwareOnly += OnClientTick;
            }
        }

        //接管放在加载流程的最后一步安装，不放在 Load 里。同一个方法上的 IL 钩子按挂入的先后依次改写方法体，其他模组大多在自己的 Load 里挂，
        //写法往往认定锚点前后还是原版的指令：我们排到最后，它们看到的就是原版，不会因为这边先动过而找错位置或加载失败；
        //这边的改写按"前面可能已经有人动过"来写，找不到锚点时只关掉那一项
        public override void PostSetupRecipes() => ScenarioTakeovers.Load();

        public override void Unload() {
            WorldGen.Hooks.OnWorldLoad -= ScenarioDirector.NotifyEnteredStage;
            WorldFile.OnWorldLoad -= OnStageFileLoaded;
            Netplay.OnDisconnect -= OnDisconnected;
            Main.OnTickForThirdPartySoftwareOnly -= OnClientTick;
            ScenarioTakeovers.Unload();
            SubworldLibraryInterop.Reset();
            ScenarioPlatform.StageBackend?.Reset();
            ScenarioDirector.Reset();
            ScenarioCameraPolicy.Reset();
            ScenarioMapPolicy.Reset();
            VaultScenario.ClearRegistry();

            VaultTypeRegistry<VaultScenario>.ClearRegisteredVaults();
            VaultType<VaultScenario>.Instances.Clear();
            VaultType<VaultScenario>.TypeToInstance.Clear();
            VaultType<VaultScenario>.TypeToMod.Clear();
            VaultType<VaultScenario>.ByID.Clear();
            VaultType<VaultScenario>.UniversalInstances.Clear();

            VaultTypeRegistry<GlobalScenario>.ClearRegisteredVaults();
            VaultType<GlobalScenario>.Instances.Clear();
            VaultType<GlobalScenario>.TypeToInstance.Clear();
            VaultType<GlobalScenario>.TypeToMod.Clear();
            VaultType<GlobalScenario>.ByID.Clear();
            VaultType<GlobalScenario>.UniversalInstances.Clear();
        }

        //先按还原日志补还原上次没来得及写回的区域，再激活新舞台场景：场景的还原区记录到的是原貌
        internal static void OnStageFileLoaded() {
            TmlRestoreBackend.RecoverJournals();
            ScenarioDirector.NotifyStageLoaded();
        }

        //（客户端）每帧在主线程调用，菜单期间也调用：换舞台的菜单阶段里 PostUpdateEverything 不会执行
        private static void OnClientTick() => (ScenarioPlatform.StageBackend as TmlStageBackend)?.WatchRejoin();

        //（客户端）与服务器的连接结束时在网络线程上触发，会话的收尾转到主线程上做：
        //掉线时原版在网络线程上、游戏还在跑帧的时候派发世界卸载（OnWorldUnload 那边跳过）；换舞台的菜单阶段里掉线时原版根本不派发。
        //那个阶段游戏停在没有按钮的状态文字页，原版断线时不会换页：换成带返回按钮的连接状态页，否则玩家会卡在加载画面上
        private static void OnDisconnected() {
            Main.QueueMainThreadAction(static () => {
                bool loading = ScenarioDirector.InBackendTravel;
                ScenarioDirector.NotifyDisconnected();
                ScenarioPlatform.StageBackend?.OnWorldUnloadedOutsideTravel();
                ScenarioMapPolicy.Reset();
                ScenarioCameraPolicy.Reset();
                if (loading && Main.gameMenu && Main.menuMode == 10) {
                    Main.statusText = Language.GetTextValue("Net.LostConnection");
                    Main.menuMode = 14;
                }
            });
        }

        public override void PostUpdateEverything() {
            if (Main.gameMenu) {
                return;
            }
            //没有地狱层的舞台：世界底边留在声明的高度上（激活时写过一次，这里防备别处按物块空间重算了它）
            TmlStageBackend.ApplyStageBounds(ScenarioRuntime.Stage?.Stage);
            ScenarioAreaTracker.Update();
            ScenarioDirector.Tick();
            if (!Main.dedServ) {
                ScenarioCameraPolicy.Tick();
                ScenarioMapPolicy.Update();
            }
        }

        //暂停菜单"保存并退出"在主线程、存盘之前调用这里，场景停用时舞台仍在、尚未保存。后端存盘、读档途中由导演层忽略。
        //原版只在 mapEnabled 为真时保存地图，存盘前换回玩家原来的地图开关
        public override void PreSaveAndQuit() {
            ScenarioDirector.EndSession();
            ScenarioMapPolicy.Reset();
        }

        //舞台存盘时（原版物块已经写完）到这里：已经写回的还原区此后不必再补还原。
        //tML 要求 SaveWorldData 与 LoadWorldData 成对重写，这里不往存档里写任何数据，读取也是空的
        public override void SaveWorldData(TagCompound tag) => TmlRestoreBackend.OnStageSaved();

        public override void LoadWorldData(TagCompound tag) { }

        //舞台场景的 Key 随世界数据下发：客户端进服与每次换舞台都会重新收一遍世界数据，出生之前就知道自己进的是哪块舞台，
        //不必等场景树快照（那要到出生之后才到）
        public override void NetSend(BinaryWriter writer)
            => writer.Write(ScenarioDirector.LoadedStage?.Template?.Key ?? string.Empty);

        public override void NetReceive(BinaryReader reader)
            => ScenarioDirector.ServerStageKey = reader.ReadString();

        //保存退出的存盘之后（存盘线程）到这里；绕过暂停菜单直接 SaveAndQuit 的路径也只走到这里，那时舞台已经存过盘。
        //换舞台自己也会派发 OnWorldUnload，那时会话仍在继续
        public override void OnWorldUnload() {
            if (ScenarioDirector.InBackendTravel) {
                return;
            }
            //客户端掉线：原版在网络线程上派发卸载，此刻主线程还在跑游戏帧（gameMenu 要等派发完才置真）。
            //在这里收尾会与主线程上的导演层并发，改由随后的断线回调在主线程上做
            if (ScenarioPlatform.IsClient && !Main.gameMenu) {
                return;
            }
            ScenarioDirector.EndSession(stageAlreadySaved: true);
            ScenarioPlatform.StageBackend?.OnWorldUnloadedOutsideTravel();
            ScenarioMapPolicy.Reset();
            ScenarioCameraPolicy.Reset();
        }
    }
}
