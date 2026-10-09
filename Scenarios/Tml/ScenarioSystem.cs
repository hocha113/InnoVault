using InnoVault.GameSystem;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //场景框架的游戏驱动：本地玩家进入世界时开始会话，每帧推进，保存退出时结束会话，卸载时清空注册表
    internal sealed class ScenarioSystem : ModSystem
    {
        public override void Load() {
            //单人下由 Player.Spawn(SpawningIntoWorld) 经 EnterWorld 触发，此时世界与各模组数据都已读入、玩家已出生，且在主线程
            WorldGen.Hooks.OnWorldLoad += ScenarioDirector.StartSession;
        }

        public override void Unload() {
            WorldGen.Hooks.OnWorldLoad -= ScenarioDirector.StartSession;
            ScenarioDirector.Reset();
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

        public override void PostUpdateEverything() {
            if (!Main.gameMenu) {
                ScenarioDirector.Tick();
            }
        }

        //暂停菜单"保存并退出"在主线程、存盘之前调用这里，场景停用时世界仍在、尚未保存
        public override void PreSaveAndQuit() => ScenarioDirector.EndSession();

        //绕过暂停菜单直接 SaveAndQuit 的路径只会走到这里（存盘线程、存盘之后）
        public override void OnWorldUnload() => ScenarioDirector.EndSession();
    }
}
