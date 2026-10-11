using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Scenarios
{
    //地图策略的执行者。原版 Main.mapEnabled 同时管大地图、小地图与地图记录，但它是玩家设置：
    //生效期间每帧压成假，失效时换回玩家原值；保存设置时由 MapSettings 接管换回原值再写
    internal static class ScenarioMapPolicy
    {
        private static bool _applied;
        private static bool _userMapEnabled;

        internal static bool Applied => _applied;

        internal static bool UserMapEnabled => _userMapEnabled;

        internal static void Update() {
            bool noMap = ScenarioDirector.InSession && ScenarioRuntime.Map.Pick(static m => m.NoMap) == true;
            if (noMap) {
                if (!_applied) {
                    _userMapEnabled = Main.mapEnabled;
                    _applied = true;
                }
                Main.mapEnabled = false;
                Main.mapFullscreen = false;
            }
            else if (_applied) {
                Restore();
            }
        }

        internal static void Reset() {
            if (_applied) {
                Restore();
            }
        }

        private static void Restore() {
            Main.mapEnabled = _userMapEnabled;
            _applied = false;
        }

        //揭开场景区域外接框（没有区域时整块舞台）内的地图
        internal static void Reveal(ScenarioMap map) {
            if (Main.dedServ || Main.Map == null) {
                return;
            }
            Rectangle rect = map.Scenario?.Area?.Bounds ?? new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY);
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
            for (int x = rect.Left; x < rect.Right; x++) {
                for (int y = rect.Top; y < rect.Bottom; y++) {
                    Main.Map.Update(x, y, 255);
                }
            }
            Main.refreshMap = true;
        }
    }

    //保存设置时写玩家原来的地图开关，不把场景临时关掉的值写进 config.json
    internal sealed class MapSettingsTakeover : ScenarioTakeover
    {
        internal override string Name => "MapSettings";

        internal override string Source => "Terraria/Main.cs SaveSettings: configuration.Put(\"MapEnabled\", mapEnabled)";

        internal override bool ClientOnly => true;

        protected override void Install() => On_Main.SaveSettings += Hook;

        protected override void Uninstall() => On_Main.SaveSettings -= Hook;

        private static bool Hook(On_Main.orig_SaveSettings orig) {
            if (!ScenarioMapPolicy.Applied) {
                return orig();
            }
            bool forced = Main.mapEnabled;
            Main.mapEnabled = ScenarioMapPolicy.UserMapEnabled;
            try {
                return orig();
            } finally {
                Main.mapEnabled = forced;
            }
        }
    }
}
