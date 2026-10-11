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

        private void Manipulate(ILContext il) {
            try {
                ILCursor cursor = new(il);
                if (_maxTilesY == null || !cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(_maxTilesY), i => i.MatchLdcI4(330), i => i.MatchSub())) {
                    Fail("'maxTilesY - 330' not found", il);
                    return;
                }
                //栈：sub 之后栈顶是一个 int（地狱背景起始行），换成场景给的行号
                cursor.EmitDelegate(static (int row) => ScenarioDrawRules.UnderworldBackdropRow(row));
                Succeed();
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }
}
