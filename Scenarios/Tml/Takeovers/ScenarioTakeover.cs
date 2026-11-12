using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    internal enum ScenarioTakeoverState
    {
        Pending,
        Applied,
        Failed,
        Skipped,
    }

    //一处原版接管（IL 或绕行）。失败只关闭这一项：记录警告、转储 IL，依赖它的规则退化为原版行为，
    //不抛异常——IL 回调里的异常会被 tML 包成 ILPatchFailureException，让 InnoVault 与所有依赖它的模组一起加载失败
    internal abstract class ScenarioTakeover
    {
        internal abstract string Name { get; }

        //对照的 tML 源码位置，升级 tML 后逐个核对
        internal abstract string Source { get; }

        internal virtual bool ClientOnly => false;

        internal ScenarioTakeoverState State { get; private set; }

        internal string Failure { get; private set; }

        internal void Load() {
            if (ClientOnly && Main.dedServ) {
                State = ScenarioTakeoverState.Skipped;
                return;
            }
            State = ScenarioTakeoverState.Pending;
            try {
                Install();
                if (State == ScenarioTakeoverState.Pending) {
                    State = ScenarioTakeoverState.Applied;
                }
            } catch (Exception ex) {
                Fail($"install threw: {ex.Message}");
            }
        }

        internal void Unload() {
            if (State == ScenarioTakeoverState.Skipped) {
                return;
            }
            try {
                Uninstall();
            } catch (Exception ex) {
                VaultMod.Instance?.Logger.Warn($"[Scenarios] Takeover {Name} failed to uninstall: {ex.Message}");
            }
            State = ScenarioTakeoverState.Pending;
        }

        protected abstract void Install();

        protected abstract void Uninstall();

        //调试用（/vaultdebug scenario takeovers dump）：为真时改写成功也转储 IL，用来核对与其他模组的改写叠加之后的最终指令
        internal static bool DumpApplied { get; set; }

        //IL 回调可能被 MonoMod 重跑（同一方法上有新的 IL 钩子挂入时），状态以最近一次为准
        protected void Succeed(ILContext il = null) {
            State = ScenarioTakeoverState.Applied;
            Failure = null;
            if (il != null && DumpApplied) {
                Dump(il);
            }
        }

        protected void Fail(string reason, ILContext il = null) {
            State = ScenarioTakeoverState.Failed;
            Failure = reason;
            VaultMod.Instance?.Logger.Warn($"[Scenarios] Takeover {Name} is disabled: {reason}. Checked against: {Source}");
            if (il != null) {
                Dump(il);
            }
        }

        //转储到日志目录的 ILDumps/InnoVault 下
        private void Dump(ILContext il) {
            try {
                MonoModHooks.DumpIL(VaultMod.Instance, il);
            } catch (Exception ex) {
                VaultMod.Instance?.Logger.Warn($"[Scenarios] Dumping IL for {Name} failed: {ex.Message}");
            }
        }
    }

    internal static class ScenarioTakeovers
    {
        private static readonly ScenarioTakeover[] _all = [
            new CameraClampTakeover(),
            new ZoneTakeover(),
            new OceanQueryTakeover(),
            new DrawBlackTakeover(),
            new UnderworldBackdropTakeover(),
            new UnderworldBackgroundTakeover(),
            new UnderworldLightTakeover(),
            new UnderworldEvaporationTakeover(),
            new LiquidFreezeTakeover(),
            new WiringFreezeTakeover(),
            new TileEntityFreezeTakeover(),
            new NPCGravityTakeover(),
            new TownNPCTakeover(),
            new WorldEventTakeover(),
            new SpawnPlacementTakeover(),
            new MapSettingsTakeover(),
            new TileDataTakeover(),
            new TileSpaceTakeover(),
        ];

        private static bool _loaded;

        internal static IReadOnlyList<ScenarioTakeover> All => _all;

        //在加载流程的最后一步调用（见 ScenarioSystem.PostSetupRecipes）
        internal static void Load() {
            if (_loaded) {
                return;
            }
            _loaded = true;
            foreach (ScenarioTakeover takeover in _all) {
                takeover.Load();
            }
            string summary = string.Join(", ", _all.Select(t => $"{t.Name}={t.State}"));
            VaultMod.Instance?.Logger.Info($"[Scenarios] Takeovers: {summary}");
        }

        //调试用：全部卸下重装一遍，重装期间每一项 IL 改写都转储最终指令。返回安装过没有
        internal static bool ReinstallWithDump() {
            if (!_loaded) {
                return false;
            }
            Unload();
            ScenarioTakeover.DumpApplied = true;
            try {
                Load();
            } finally {
                ScenarioTakeover.DumpApplied = false;
            }
            return true;
        }

        //加载中途失败时可能还没安装过
        internal static void Unload() {
            if (!_loaded) {
                return;
            }
            _loaded = false;
            foreach (ScenarioTakeover takeover in _all) {
                takeover.Unload();
            }
        }
    }
}
