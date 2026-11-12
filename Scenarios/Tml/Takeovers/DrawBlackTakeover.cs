using MonoMod.Cil;
using System;
using System.Reflection;
using Terraria;

namespace InnoVault.Scenarios
{
    //小世界地下涂黑死窗。两处单点修改代替整体替换方法体：
    //①非 force 时的行域钳制改为"force 或场景要求整幅涂黑"；②循环里地狱行的亮度阈值比较值可由场景改写
    internal sealed class DrawBlackTakeover : ScenarioTakeover
    {
        private static readonly MethodInfo _showInvisibleWalls = typeof(Main).GetMethod(nameof(Main.ShouldShowInvisibleWalls), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo _underworldLayer = typeof(Main).GetProperty(nameof(Main.UnderworldLayer))?.GetGetMethod();

        internal override string Name => "DrawBlack";

        internal override string Source => "Terraria/Main.cs DrawBlack(bool force): 'if (!force)' row clamp, and 'i >= UnderworldLayer' after ShouldShowInvisibleWalls()";

        internal override bool ClientOnly => true;

        protected override void Install() => IL_Main.DrawBlack += Manipulate;

        protected override void Uninstall() => IL_Main.DrawBlack -= Manipulate;

        private void Manipulate(ILContext il) {
            try {
                ILCursor clamp = new(il);
                if (!clamp.TryGotoNext(MoveType.Before, i => i.MatchLdarg(1), i => i.MatchBrtrue(out _))) {
                    Fail("'if (!force)' branch not found", il);
                    return;
                }
                ILCursor hell = new(il);
                if (_showInvisibleWalls == null || _underworldLayer == null
                    || !hell.TryGotoNext(i => i.MatchCall(_showInvisibleWalls))
                    || !hell.TryGotoNext(MoveType.After, i => i.MatchCall(_underworldLayer))) {
                    Fail("'i >= UnderworldLayer' in the row loop not found", il);
                    return;
                }
                //两个锚点都找到之后才写入。栈：get_UnderworldLayer 之后栈顶是一个 int，换成场景给的行号
                hell.EmitDelegate(static (int row) => ScenarioDrawRules.HellRow(row));
                //栈：ldarg.1 之后栈顶是 force，换成"force 或场景要求整幅涂黑"
                clamp.Index++;
                clamp.EmitDelegate(static (bool force) => ScenarioDrawRules.SkipRowClamp(force));
                Succeed(il);
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }

    //绘制规则的取值。地狱层的四项视觉（涂黑阈值、背景黑底、背景画、环境光）各自按"单独设置 → 总开关 HideUnderworld → 自动规则"的顺序取值
    internal static class ScenarioDrawRules
    {
        //每个游戏帧在主线程算一次的结果：光照扫描逐格调用、还可能在别的线程上，不能在那里遍历各层
        private static volatile bool _hideUnderworldLight;
        private static volatile bool _hideUnderworldBackground;

        //整幅涂黑：环境组件指定为准；各层都没设置时，场景舞台的 worldSurface 低于舞台一半高度就开启（天空会从死窗透出）
        internal static bool FullRangeBlackout() {
            bool? setting = ScenarioRuntime.Environment.Pick(static e => e.FullRangeBlackout);
            return setting ?? (ScenarioRuntime.InScenarioStage && Main.worldSurface > StageRows() / 2);
        }

        //舞台内容占用的行数：没有地狱层的舞台不算垫在下面的行
        private static int StageRows()
            => ScenarioRuntime.Stage?.Stage is { BindsRootWorld: false } stage ? Math.Min(stage.Height, Main.maxTilesY) : Main.maxTilesY;

        internal static bool SkipRowClamp(bool force) => force || FullRangeBlackout();

        internal static int HellRow(int row) {
            ScenarioLayers<ScenarioEnvironment> environment = ScenarioRuntime.Environment;
            bool? setting = environment.Pick(static e => e.NoUnderworldBlackThreshold) ?? environment.Pick(static e => e.HideUnderworld);
            return (setting ?? FullRangeBlackout()) ? int.MaxValue : row;
        }

        //取消时推到舞台底部以下：原版随后还会按 worldSurface 对齐、再减几行，推远一些保证屏幕底边永远够不着
        internal static int UnderworldBackdropRow(int row) => BackdropCancelled(row) ? Main.maxTilesY + 600 : row;

        //row 是原版算出的地狱背景起始行（maxTilesY - 330）
        private static bool BackdropCancelled(int row) {
            ScenarioLayers<ScenarioEnvironment> environment = ScenarioRuntime.Environment;
            bool? setting = environment.Pick(static e => e.NoUnderworldBackdrop) ?? environment.Pick(static e => e.HideUnderworld);
            return setting ?? (ScenarioRuntime.InScenarioStage && row < Main.worldSurface + 100);
        }

        //地狱环境光此刻是否取消
        internal static bool HideUnderworldLight => _hideUnderworldLight;

        internal static bool HideUnderworldBackground => _hideUnderworldBackground;

        //每个游戏帧在主线程调用（只在客户端）：背景画与环境光没有单独设置、也没有总开关时，跟着背景黑底的结果走
        internal static void Refresh() {
            ScenarioLayers<ScenarioEnvironment> environment = ScenarioRuntime.Environment;
            if (!environment.Any && !ScenarioRuntime.InScenarioStage) {
                _hideUnderworldLight = false;
                _hideUnderworldBackground = false;
                return;
            }
            bool? master = environment.Pick(static e => e.HideUnderworld);
            bool backdrop = BackdropCancelled(Main.maxTilesY - 330);
            _hideUnderworldLight = environment.Pick(static e => e.NoUnderworldLight) ?? master ?? backdrop;
            _hideUnderworldBackground = environment.Pick(static e => e.NoUnderworldBackground) ?? master ?? backdrop;
        }

        internal static void Reset() {
            _hideUnderworldLight = false;
            _hideUnderworldBackground = false;
        }
    }
}
