using InnoVault.GameSystem;
using Terraria;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //场景框架的游戏驱动：进入舞台开始会话或完成换舞台，每帧推进，保存退出时结束会话，卸载时清空注册表
    internal sealed class ScenarioSystem : ModSystem
    {
        public override void Load() {
            //单人下由 Player.Spawn(SpawningIntoWorld) 经 EnterWorld 触发：舞台与各模组数据都已读入、玩家已出生，且在主线程
            WorldGen.Hooks.OnWorldLoad += ScenarioDirector.NotifyEnteredStage;
            //WorldFile.LoadWorld 成功结束时触发：各模组 LoadWorldData 之后、玩家出生之前，在读档线程上
            WorldFile.OnWorldLoad += OnStageFileLoaded;
            ScenarioTakeovers.Load();
        }

        public override void Unload() {
            WorldGen.Hooks.OnWorldLoad -= ScenarioDirector.NotifyEnteredStage;
            WorldFile.OnWorldLoad -= OnStageFileLoaded;
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
        private static void OnStageFileLoaded() {
            TmlRestoreBackend.RecoverJournals();
            ScenarioDirector.NotifyStageLoaded();
        }

        public override void PostUpdateEverything() {
            if (Main.gameMenu) {
                return;
            }
            ScenarioAreaTracker.Update();
            ScenarioDirector.Tick();
            ScenarioCameraPolicy.Tick();
            ScenarioMapPolicy.Update();
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

        //保存退出的存盘之后（存盘线程）到这里；绕过暂停菜单直接 SaveAndQuit 的路径也只走到这里，那时舞台已经存过盘。
        //换舞台自己也会派发 OnWorldUnload，那时会话仍在继续
        public override void OnWorldUnload() {
            if (ScenarioDirector.InBackendTravel) {
                return;
            }
            ScenarioDirector.EndSession(stageAlreadySaved: true);
            ScenarioPlatform.StageBackend?.OnWorldUnloadedOutsideTravel();
            ScenarioMapPolicy.Reset();
            ScenarioCameraPolicy.Reset();
        }
    }
}
