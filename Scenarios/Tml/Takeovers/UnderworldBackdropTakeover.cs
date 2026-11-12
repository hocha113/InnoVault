using MonoMod.Cil;
using System;
using System.Reflection;
using Terraria;

namespace InnoVault.Scenarios
{
    //地狱背景黑底：DrawBackground 开头的 maxTilesY - 330 决定地狱背景从哪一行开始，屏幕底边越过它就在物块后铺一整屏黑底。
    //矮世界里这一行紧贴地表，天空整片变黑（2026-10-10 实机：600 行世界、worldSurface 260 时这一行算出来是 261）
    internal sealed class UnderworldBackdropTakeover : ScenarioTakeover
    {
        private static readonly FieldInfo _maxTilesY = typeof(Main).GetField(nameof(Main.maxTilesY), BindingFlags.Public | BindingFlags.Static);

        internal override string Name => "UnderworldBackdrop";

        internal override string Source => "Terraria/Main.cs DrawBackground: 'double num2 = maxTilesY - 330' (first statement after the setup) and the full-screen black box drawn when num2 * 16 <= screen bottom";

        internal override bool ClientOnly => true;

        protected override void Install() => IL_Main.DrawBackground += Manipulate;

        protected override void Uninstall() => IL_Main.DrawBackground -= Manipulate;

        //从读 maxTilesY 到减法最多隔这么多条指令：原版是紧挨着的三条，余量留给排在前面的模组插进来的指令
        private const int AnchorGap = 12;

        private void Manipulate(ILContext il) {
            try {
                ILCursor cursor = new(il);
                //两段锚点分开找：排在前面的模组可能已经在读 maxTilesY 与常数之间插了自己的分支，只要求常数与减法还紧挨着
                if (_maxTilesY == null || !cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(_maxTilesY))) {
                    Fail("read of maxTilesY not found", il);
                    return;
                }
                int read = cursor.Index;
                if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(330), i => i.MatchSub()) || cursor.Index - read > AnchorGap) {
                    Fail("'maxTilesY - 330' not found", il);
                    return;
                }
                //栈：sub 之后栈顶是一个 int（地狱背景起始行），换成场景给的行号
                cursor.EmitDelegate(static (int row) => ScenarioDrawRules.UnderworldBackdropRow(row));
                Succeed(il);
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }
}
