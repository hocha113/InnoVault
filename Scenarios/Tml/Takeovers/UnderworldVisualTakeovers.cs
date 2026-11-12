using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;

namespace InnoVault.Scenarios
{
    //地狱环境光：光照扫描给最底下 200 行的每一格补一层暗红底光。矮舞台整张图都落在这些行里时，地表与室内都会泛着这层光。
    //这层光整个算在 ApplyHellLight 里（只算底光与几种玻璃墙的透光色，没有模组钩子的派发点），规则取消时不调用 orig。
    //绕行这个方法而不去改光照扫描里那句行号比较：那一句别的模组也会改，认的是它原来的样子。
    //逐格调用、还可能在别的线程上，回调只读一个每帧算好的标志
    internal sealed class UnderworldLightTakeover : ScenarioTakeover
    {
        internal override string Name => "UnderworldLight";

        internal override string Source => "Terraria.Graphics.Light/TileLightScanner.cs ApplyHellLight (private; called only from GetTileLight under 'else if (y > Main.UnderworldLayer)')";

        internal override bool ClientOnly => true;

        protected override void Install() => On_TileLightScanner.ApplyHellLight += Hook;

        protected override void Uninstall() => On_TileLightScanner.ApplyHellLight -= Hook;

        private static void Hook(On_TileLightScanner.orig_ApplyHellLight orig, TileLightScanner self, Tile tile, int x, int y, ref Vector3 lightColor) {
            if (ScenarioDrawRules.HideUnderworldLight) {
                return;
            }
            orig(self, tile, x, y, ref lightColor);
        }
    }

    //地狱背景画：屏幕底边靠近物块空间底部 220 行时，原版在物块后面画五层熔岩远景。规则取消时整段不调用 orig：
    //该方法只画这几层背景（连带推进自定义天空的深度绘制），里面没有模组钩子的派发点
    internal sealed class UnderworldBackgroundTakeover : ScenarioTakeover
    {
        internal override string Name => "UnderworldBackground";

        internal override string Source => "Terraria/Main.cs DrawUnderworldBackground(bool flat) (returns early unless the screen bottom is below (maxTilesY - 220) * 16; called from DoDraw and the map/capture path)";

        internal override bool ClientOnly => true;

        protected override void Install() => On_Main.DrawUnderworldBackground += Hook;

        protected override void Uninstall() => On_Main.DrawUnderworldBackground -= Hook;

        private static void Hook(On_Main.orig_DrawUnderworldBackground orig, Main self, bool flat) {
            if (ScenarioDrawRules.HideUnderworldBackground) {
                return;
            }
            orig(self, flat);
        }
    }
}
