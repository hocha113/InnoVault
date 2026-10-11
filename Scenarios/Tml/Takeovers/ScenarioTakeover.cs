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

        //IL 回调可能被 MonoMod 重跑（同一方法上有新的 IL 钩子挂入时），状态以最近一次为准
        protected void Succeed() {
            State = ScenarioTakeoverState.Applied;
            Failure = null;
        }

        protected void Fail(string reason, ILContext il = null) {
            State = ScenarioTakeoverState.Failed;
            Failure = reason;
            VaultMod.Instance?.Logger.Warn($"[Scenarios] Takeover {Name} is disabled: {reason}. Checked against: {Source}");
            if (il != null) {
                try {
                    MonoModHooks.DumpIL(VaultMod.Instance, il);
                } catch (Exception ex) {
                    VaultMod.Instance?.Logger.Warn($"[Scenarios] Dumping IL for {Name} failed: {ex.Message}");
                }
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
            new TownNPCTakeover(),
            new WorldEventTakeover(),
            new SpawnPlacementTakeover(),
            new MapSettingsTakeover(),
            new TileDataTakeover(),
        ];

        internal static IReadOnlyList<ScenarioTakeover> All => _all;

        internal static void Load() {
            foreach (ScenarioTakeover takeover in _all) {
                takeover.Load();
            }
            string summary = string.Join(", ", _all.Select(t => $"{t.Name}={t.State}"));
            VaultMod.Instance?.Logger.Info($"[Scenarios] Takeovers: {summary}");
        }

        internal static void Unload() {
            foreach (ScenarioTakeover takeover in _all) {
                takeover.Unload();
            }
        }
    }
}
