using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.Scenarios
{
    //世界模拟规则的取值。液体、电路计时、物块实体是整块舞台的状态：只在拥有世界的一端（单人与服务器）推进，
    //单人下跟随玩家所在的层，服务器上只听作用于整块舞台的规则节点，与时间、物块更新这几项的取值方式一致
    internal static class ScenarioWorldRules
    {
        private static readonly Dictionary<int, TileEntity>.ValueCollection _noTileEntities = new Dictionary<int, TileEntity>().Values;

        //当前线程正在读入一块不要液体沉降的舞台：读档末尾的快速沉降与液体检查跳过，液体留在存档里的位置
        [ThreadStatic]
        private static bool _skipLoadSettle;

        internal static bool SkipLoadSettle {
            get => _skipLoadSettle;
            set => _skipLoadSettle = value;
        }

        internal static bool LiquidsFrozen => ScenarioRuntime.Rules.Any && ScenarioRuntime.Rules.Pick(static r => r.FreezeLiquids) == true;

        internal static bool WiringFrozen => ScenarioRuntime.Rules.Any && ScenarioRuntime.Rules.Pick(static r => r.FreezeWiring) == true;

        //停摆时让原版的逐实体更新循环遍历一个空集合
        internal static Dictionary<int, TileEntity>.ValueCollection TileEntitiesToUpdate(Dictionary<int, TileEntity>.ValueCollection values)
            => ScenarioRuntime.Rules.Any && ScenarioRuntime.Rules.Pick(static r => r.FreezeTileEntities) == true ? _noTileEntities : values;

        //这一格液体在地狱行里也不蒸发：它所在的规则取消了地狱高度区域。由改写后的 Liquid.Update 直接调用，只在地狱行里的液体上执行
        internal static bool KeepsUnderworldWater(Liquid liquid)
            => ScenarioRuntime.RulesByLayer.Length > 0 && ScenarioRuntime.PickRuleAt(liquid.x, liquid.y, static r => r.NoUnderworldZone) == true;

        //某个位置上的重力倍数，没有规则管到这里时为空
        internal static float? GravityAt(Vector2 worldPosition) {
            if (ScenarioRuntime.RulesByLayer.Length == 0) {
                return null;
            }
            Point tile = worldPosition.ToTileCoordinates();
            return ScenarioRuntime.PickRuleAt(tile.X, tile.Y, static r => r.Gravity);
        }
    }

    //液体停摆与读档沉降。原版每两帧推进一次液体（流动、沉降、地狱行的蒸发都在里面），读档末尾还会做一次全图的快速沉降：
    //按构造摆好的水面（没有底的海、悬空的水幕）经不起其中任何一次。规则要求停摆时整段不调用 orig，
    //这几个方法里都没有模组钩子的派发点；生成期间（WorldGen.gen）不拦，生成步骤自己的沉降照常工作
    internal sealed class LiquidFreezeTakeover : ScenarioTakeover
    {
        internal override string Name => "LiquidFreeze";

        internal override string Source => "Terraria/Liquid.cs UpdateLiquid (called every other frame from WorldGen.UpdateWorld_Inner and by the settle loop at the end of WorldFile.LoadWorld), "
            + "Liquid.QuickWater and Terraria/WorldGen.cs WaterCheck (both called by WorldFile.LoadWorld right before that loop)";

        protected override void Install() {
            On_Liquid.UpdateLiquid += UpdateLiquid;
            On_Liquid.QuickWater += QuickWater;
            On_WorldGen.WaterCheck += WaterCheck;
        }

        protected override void Uninstall() {
            On_Liquid.UpdateLiquid -= UpdateLiquid;
            On_Liquid.QuickWater -= QuickWater;
            On_WorldGen.WaterCheck -= WaterCheck;
        }

        private static void UpdateLiquid(On_Liquid.orig_UpdateLiquid orig) {
            if (!WorldGen.gen && ScenarioWorldRules.LiquidsFrozen) {
                return;
            }
            orig();
        }

        private static void QuickWater(On_Liquid.orig_QuickWater orig, int verbose, int minY, int maxY) {
            if (ScenarioWorldRules.SkipLoadSettle) {
                return;
            }
            orig(verbose, minY, maxY);
        }

        private static void WaterCheck(On_WorldGen.orig_WaterCheck orig) {
            if (ScenarioWorldRules.SkipLoadSettle) {
                return;
            }
            orig();
        }
    }

    //电路计时停摆：UpdateMech 只推进计时器与机关冷却，玩家触发的电路不经过它；方法里没有模组钩子的派发点
    internal sealed class WiringFreezeTakeover : ScenarioTakeover
    {
        internal override string Name => "WiringFreeze";

        internal override string Source => "Terraria/Wiring.cs UpdateMech (the first call in WorldGen.UpdateWorld_Inner; counts the mech timers down)";

        protected override void Install() => On_Wiring.UpdateMech += Hook;

        protected override void Uninstall() => On_Wiring.UpdateMech -= Hook;

        private static void Hook(On_Wiring.orig_UpdateMech orig) {
            if (ScenarioWorldRules.WiringFrozen) {
                return;
            }
            orig();
        }
    }

    //物块实体停摆：逐实体更新是 UpdateWorld_Inner 里的一个内联循环，没有可绕行的方法，改它遍历的集合
    internal sealed class TileEntityFreezeTakeover : ScenarioTakeover
    {
        private static readonly FieldInfo _byID = typeof(TileEntity).GetField(nameof(TileEntity.ByID), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo _getValues = typeof(Dictionary<int, TileEntity>).GetProperty(nameof(Dictionary<int, TileEntity>.Values))?.GetGetMethod();

        internal override string Name => "TileEntityFreeze";

        internal override string Source => "Terraria/WorldGen.cs UpdateWorld_Inner: 'foreach (TileEntity value in TileEntity.ByID.Values) value.Update();' between TileEntity.UpdateStart() and UpdateEnd()";

        protected override void Install() => IL_WorldGen.UpdateWorld_Inner += Manipulate;

        protected override void Uninstall() => IL_WorldGen.UpdateWorld_Inner -= Manipulate;

        private void Manipulate(ILContext il) {
            try {
                ILCursor cursor = new(il);
                if (_byID == null || _getValues == null
                    || !cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(_byID), i => i.MatchCallvirt(_getValues))) {
                    Fail("'TileEntity.ByID.Values' not found", il);
                    return;
                }
                //栈：get_Values 之后栈顶是要遍历的集合，停摆时换成一个空集合，一进一出
                cursor.EmitDelegate(static (Dictionary<int, TileEntity>.ValueCollection values) => ScenarioWorldRules.TileEntitiesToUpdate(values));
                Succeed(il);
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }

    //地狱行的蒸发：水落在最底下 200 行里会逐帧减少。规则取消地狱高度区域的地方跳过这一段。
    //原版那句行号比较（读行号、紧跟一条条件跳转）一条指令都不动，只在跳转之后补一道自己的判断：
    //别的模组也会改这一句，认的是它原来的样子，往读行号与跳转中间插指令会让它们找错位置
    internal sealed class UnderworldEvaporationTakeover : ScenarioTakeover
    {
        //从读行号到那条比较跳转最多隔这么多条指令：原版是紧挨着的，余量留给排在前面的模组插进来的指令
        private const int BranchSearchWindow = 24;

        private static readonly MethodInfo _underworldLayer = typeof(Main).GetProperty(nameof(Main.UnderworldLayer))?.GetGetMethod();
        private static readonly MethodInfo _keepsWater = typeof(ScenarioWorldRules).GetMethod(nameof(ScenarioWorldRules.KeepsUnderworldWater), BindingFlags.NonPublic | BindingFlags.Static);

        internal override string Name => "UnderworldEvaporation";

        internal override string Source => "Terraria/Liquid.cs Update: 'if (y > Main.UnderworldLayer && tile.liquidType() == 0 && tile.liquid > 0)' (the only UnderworldLayer read in the method), "
            + "compiled as 'ldarg.0, ldfld y, call get_UnderworldLayer, ble <first statement after the block>'";

        protected override void Install() => IL_Liquid.Update += Manipulate;

        protected override void Uninstall() => IL_Liquid.Update -= Manipulate;

        private void Manipulate(ILContext il) {
            try {
                ILCursor cursor = new(il);
                if (_underworldLayer == null || _keepsWater == null || !cursor.TryGotoNext(MoveType.After, i => i.MatchCall(_underworldLayer))) {
                    Fail("read of Main.UnderworldLayer not found", il);
                    return;
                }
                //行号比较不成立时原版跳去的地方就是蒸发那一段之后，这边的判断成立时跳去同一处
                Instruction branch = cursor.Next;
                ILLabel skip = null;
                for (int i = 0; i < BranchSearchWindow && branch != null; i++, branch = branch.Next) {
                    if ((branch.OpCode == OpCodes.Ble || branch.OpCode == OpCodes.Ble_S) && branch.Operand is ILLabel target) {
                        skip = target;
                        break;
                    }
                }
                if (skip == null) {
                    Fail("the branch of 'y > Main.UnderworldLayer' not found", il);
                    return;
                }
                cursor.Goto(branch, MoveType.After);
                //栈：比较跳转之后栈为空。压入 this 交给取值调用，留下一个 bool，为真就跳过蒸发
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Call, _keepsWater);
                cursor.Emit(OpCodes.Brtrue, skip);
                Succeed(il);
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }

    //NPC 的重力：原版每帧重算，其中一段按高度衰减（越靠近物块空间顶部越轻）。规则给了重力倍数的位置上这一段不再生效，
    //按类型的特例与液体里的重力照旧，最后乘上规则的倍数；始终调用 orig
    internal sealed class NPCGravityTakeover : ScenarioTakeover
    {
        internal override string Name => "NPCGravity";

        internal override string Source => "Terraria/NPC.cs UpdateNPC_UpdateGravity (private, called from UpdateNPC_Inner right after NPCLoader.ResetEffects; the 'if (!GravityIgnoresSpace)' block scales gravity by height)";

        protected override void Install() => On_NPC.UpdateNPC_UpdateGravity += Hook;

        protected override void Uninstall() => On_NPC.UpdateNPC_UpdateGravity -= Hook;

        private static void Hook(On_NPC.orig_UpdateNPC_UpdateGravity orig, NPC self) {
            if (ScenarioWorldRules.GravityAt(self.Center) is not float gravity) {
                orig(self);
                return;
            }
            bool ignoresSpace = self.GravityIgnoresSpace;
            self.GravityIgnoresSpace = true;
            try {
                orig(self);
            } finally {
                self.GravityIgnoresSpace = ignoresSpace;
            }
            self.GravityMultiplier *= gravity;
        }
    }
}
