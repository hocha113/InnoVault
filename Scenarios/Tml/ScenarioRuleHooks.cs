using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.Graphics.Effects;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //ScenarioRules 与 ScenarioEnvironment 里不需要接管原版的部分，全部走 tML 公开钩子。没有生效的组件时什么都不做
    internal sealed class ScenarioRuleSystem : ModSystem
    {
        private static string _activeSky;

        //UpdateTimeRate 每帧在 UpdateTime 里调用，这里的结果每帧重新生效
        public override void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate) {
            ScenarioLayers<ScenarioRules> rules = ScenarioRuntime.Rules;
            if (!rules.Any) {
                return;
            }
            if (rules.Pick(static r => r.TimeRate) is double rate) {
                timeRate *= rate;
            }
            if (rules.Pick(static r => r.FreezeTiles) == true) {
                tileUpdateRate = 0;
            }
            if (rules.Pick(static r => r.FreezeEventTimers) == true) {
                eventUpdateRate = 0;
            }
        }

        public override void PostUpdateTime() {
            ScenarioLayers<ScenarioRules> rules = ScenarioRuntime.Rules;
            if (!rules.Any) {
                return;
            }
            if (rules.Pick(static r => r.FixedTime) is double time) {
                Main.time = time;
            }
            if (rules.Pick(static r => r.FixedDayTime) is bool dayTime) {
                Main.dayTime = dayTime;
            }
            if (rules.Pick(static r => r.NoWeather) == true) {
                if (Main.raining) {
                    Main.StopRain();
                }
                Sandstorm.Happening = false;
                Sandstorm.TimeLeft = 0;
            }
            if (rules.Pick(static r => r.NoEvents) == true) {
                ClearEvents();
            }
        }

        private static void ClearEvents() {
            Main.bloodMoon = false;
            Main.eclipse = false;
            Main.pumpkinMoon = false;
            Main.snowMoon = false;
            WorldGen.spawnMeteor = false;
            if (Main.slimeRain) {
                Main.StopSlimeRain(false);
            }
            if (Main.invasionType != 0) {
                Main.invasionType = 0;
                Main.invasionSize = 0;
            }
            BirthdayParty.GenuineParty = false;
            BirthdayParty.ManualParty = false;
            LanternNight.GenuineLanterns = false;
            LanternNight.ManualLanterns = false;
        }

        //各层从下到上依次调整
        public override void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor) {
            ScenarioLayers<ScenarioEnvironment> environment = ScenarioRuntime.Environment;
            for (int i = 0; i < environment.Count; i++) {
                try {
                    environment[i].ModifySunLight(ref tileColor, ref backgroundColor);
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.ModifySunLight.{environment[i].Scenario?.Key}", $"[Scenarios] {environment[i].Scenario?.Key} ModifySunLight threw: {ex}");
                }
            }
        }

        public override void ModifyLightingBrightness(ref float scale) {
            if (ScenarioRuntime.Environment.Pick(static e => e.LightingBrightness) is float brightness) {
                scale *= brightness;
            }
        }

        public override void PostUpdateEverything() {
            if (!Main.dedServ) {
                SyncSky(ScenarioRuntime.Environment.PickRef(static e => e.Sky));
                ScenarioDrawRules.Refresh();
            }
        }

        public override void OnWorldUnload() {
            if (!Main.dedServ) {
                SyncSky(null);
                ScenarioDrawRules.Reset();
            }
        }

        public override void Unload() => _activeSky = null;

        //只停用自己激活的天空，不碰别处激活的
        private static void SyncSky(string desired) {
            if (desired == _activeSky) {
                return;
            }
            if (_activeSky != null && SkyManager.Instance[_activeSky] != null) {
                SkyManager.Instance.Deactivate(_activeSky);
            }
            _activeSky = null;
            if (desired != null && SkyManager.Instance[desired] != null) {
                SkyManager.Instance.Activate(desired);
                _activeSky = desired;
            }
        }
    }

    internal sealed class ScenarioRulePlayer : ModPlayer
    {
        //PreUpdateMovement 在原版重力与高空低重力系数之后、移动之前。每一端都会更新所有玩家，按这个玩家自己所在的层取值，其他端的推算才与它本机一致
        public override void PreUpdateMovement() {
            if (ScenarioRuntime.RulesFor(Player.whoAmI).Pick(static r => r.Gravity) is float gravity) {
                Player.gravity = Terraria.Player.defaultGravity * gravity;
            }
        }

        //单人：复活落点规则是“回主世界”或“换到另一块舞台”时，复活倒计时走完就直接换舞台，角色到了那边再复活，不在原地先站起来一次。
        //这个钩子在原版递减倒计时之前执行：换舞台开始后把倒计时停住，角色保持倒下直到被带走；发起不了时不拦，原版照常复活，落点规则在复活之后再试一次。
        //多人下换舞台要经服务器裁决，仍然是原地复活之后由落点规则发请求
        public override void UpdateDead() {
            if (Main.netMode != NetmodeID.SinglePlayer || Player.whoAmI != Main.myPlayer || Player.respawnTimer > 2
                || Player.difficulty == PlayerDifficultyID.Hardcore || !ScenarioDirector.InSession) {
                return;
            }
            ScenarioLayers<ScenarioRules> rules = ScenarioRuntime.Rules;
            if (!rules.Any) {
                return;
            }
            Type destination = null;
            switch (rules.Pick(static r => r.RespawnBehavior) ?? ScenarioSpawnBehavior.Vanilla) {
                case ScenarioSpawnBehavior.ReturnHome:
                    if (!ScenarioRuntime.InScenarioStage) {
                        return;
                    }
                    break;
                case ScenarioSpawnBehavior.Travel:
                    destination = rules.PickRef(static r => r.RespawnDestination);
                    if (destination == null) {
                        return;
                    }
                    break;
                default:
                    return;
            }
            if (!ScenarioDirector.IsTraveling && !ScenarioPlacement.StartTravel(destination, rules.PickRef(static r => r.RespawnAnchor)).Accepted) {
                return;
            }
            Player.respawnTimer = 2;
        }
    }

    //掉落物的重力：按掉落物所在的位置取规则的重力倍数（NPC 的在 NPCGravity 接管里）
    internal sealed class ScenarioRuleItem : GlobalItem
    {
        public override void Update(Item item, ref float gravity, ref float maxFallSpeed) {
            if (ScenarioWorldRules.GravityAt(item.Center) is float scale) {
                gravity *= scale;
            }
        }
    }

    internal sealed class ScenarioRuleNPC : GlobalNPC
    {
        public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns) {
            //服务器换舞台期间（含等客户端到齐的那一段）不自然刷怪：已经到场的玩家还盖着幕布
            if (Main.dedServ && ScenarioDirector.IsTraveling) {
                maxSpawns = 0;
            }
            //原版按玩家逐个计算刷怪，按这个玩家所在的层取值
            ScenarioLayers<ScenarioRules> rules = ScenarioRuntime.RulesFor(player.whoAmI);
            if (!rules.Any) {
                return;
            }
            if (rules.Pick(static r => r.NoNaturalSpawns) == true) {
                maxSpawns = 0;
            }
            for (int i = 0; i < rules.Count; i++) {
                try {
                    rules[i].EditSpawnRate(player, ref spawnRate, ref maxSpawns);
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.EditSpawnRate.{rules[i].Scenario?.Key}", $"[Scenarios] {rules[i].Scenario?.Key} EditSpawnRate threw: {ex}");
                }
            }
        }

        public override void EditSpawnPool(IDictionary<int, float> pool, NPCSpawnInfo spawnInfo) {
            ScenarioLayers<ScenarioRules> rules = spawnInfo.Player != null ? ScenarioRuntime.RulesFor(spawnInfo.Player.whoAmI) : ScenarioRuntime.Rules;
            for (int i = 0; i < rules.Count; i++) {
                try {
                    rules[i].EditSpawnPool(pool, spawnInfo);
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.EditSpawnPool.{rules[i].Scenario?.Key}", $"[Scenarios] {rules[i].Scenario?.Key} EditSpawnPool threw: {ex}");
                }
            }
        }
    }

    //音乐、背景、水样式：一个场景效果把各层合成后的属性转发出去
    internal sealed class ScenarioSceneEffect : ModSceneEffect
    {
        public override int Music => ScenarioRuntime.Environment.Pick(static e => e.Music) ?? -1;

        public override ModSurfaceBackgroundStyle SurfaceBackgroundStyle => ScenarioRuntime.Environment.PickRef(static e => e.SurfaceBackground);

        public override ModUndergroundBackgroundStyle UndergroundBackgroundStyle => ScenarioRuntime.Environment.PickRef(static e => e.UndergroundBackground);

        public override ModWaterStyle WaterStyle => ScenarioRuntime.Environment.PickRef(static e => e.WaterStyle);

        public override SceneEffectPriority Priority
            => ScenarioRuntime.Environment.TopWhere(static e => e.SetsSceneEffect)?.Priority ?? SceneEffectPriority.None;

        public override bool IsSceneEffectActive(Player player) => ScenarioRuntime.Environment.TopWhere(static e => e.SetsSceneEffect) != null;
    }
}
