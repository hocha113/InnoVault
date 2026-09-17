using InnoVault.Debugs;
using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Rigs2D.Debug
{
    /// <summary>
    /// 骨架调试叠层：把本帧步进过的每个 <see cref="Rig2DInstance"/> 画成骨线 + 关节点 + 贴图锚点，
    /// 并调用各求解器的 <see cref="Rig2DSolver.DebugDraw"/>（IK 目标、可达圈、足端状态）
    /// <br/>受 <see cref="DebugSettings.Rig2DShowOverlay"/> 控制，<c>/vaultdebug</c> 面板 Rig2D 页开关；
    /// 叠层在 UI 层绘制，世界坐标经缩放矩阵换算到界面坐标
    /// </summary>
    internal sealed class Rig2DDebugOverlay : UIHandle
    {
        private static readonly Color boneColor = new(120, 220, 255);
        private static readonly Color drivenColor = new(255, 200, 90);
        private static readonly Color jointColor = new(255, 255, 255);
        private static readonly Color pieceColor = new(255, 110, 200);
        private static readonly Color ribbonColor = new(140, 255, 170);
        private static readonly List<Vector2> ribbonPath = new(128);

        public override bool Active => DebugSettings.Rig2DShowOverlay && !Main.gameMenu;

        public override void Draw(SpriteBatch spriteBatch) {
            IReadOnlyList<Rig2DInstance> instances = Rig2DSystem.DebugInstances;
            if (instances.Count == 0) {
                return;
            }
            Func<Vector2, Vector2> toScreen = WorldToUI;
            for (int i = 0; i < instances.Count; i++) {
                DrawInstance(spriteBatch, instances[i], toScreen);
            }
        }

        /// <summary>
        /// 世界坐标 → 界面坐标（UIScaleMatrix 空间）
        /// </summary>
        internal static Vector2 WorldToUI(Vector2 world) {
            Vector2 screen = Vector2.Transform(world - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);
            return screen / Main.UIScale;
        }

        private static void DrawInstance(SpriteBatch sb, Rig2DInstance rig, Func<Vector2, Vector2> toScreen) {
            Rig2DDefinition def = rig.Definition;
            if (def == null || rig.Bones.Length == 0) {
                return;
            }
            //本帧没步进的实例（离屏 / 冻结）跳过，防止陈旧位姿误导
            if (Main.GameUpdateCount - rig.LastStepTick > 2u) {
                return;
            }

            for (int b = 0; b < rig.Bones.Length; b++) {
                ref Bone2D bone = ref rig.Bones[b];
                Vector2 a = toScreen(bone.Pos);
                Vector2 t = toScreen(bone.Tip);
                bool zeroLen = bone.Length < 0.5f;
                Color c = IsSolverDriven(rig, b) ? drivenColor : boneColor;
                if (!zeroLen) {
                    Rig2DDebugDraw.Line(sb, a, t, c * 0.85f, 2f);
                    //尖端小箭：读出轴向
                    Vector2 d = t - a;
                    if (d.LengthSquared() > 1f) {
                        d.Normalize();
                        Vector2 n = new(-d.Y, d.X);
                        Rig2DDebugDraw.Line(sb, t, t - d * 6f + n * 3f, c, 1f);
                        Rig2DDebugDraw.Line(sb, t, t - d * 6f - n * 3f, c, 1f);
                    }
                }
                Rig2DDebugDraw.Dot(sb, a, zeroLen ? 5f : 4f, jointColor);
            }

            //贴图锚点：件挂在骨骼近端 + 位置偏移
            for (int p = 0; p < rig.Pieces.Length; p++) {
                Piece2DDef pd = def.Pieces[p];
                if (pd.BoneIndex < 0 || !rig.Pieces[p].Visible) {
                    continue;
                }
                Vector2 pos = rig.Bones[pd.BoneIndex].Pos + rig.Pieces[p].PositionOffset;
                Rig2DDebugDraw.Dot(sb, toScreen(pos), 3f, pieceColor);
            }

            //带状件中心线：与渲染器同一条路径（含细分），读出条带实际走向
            for (int r = 0; r < rig.Ribbons.Length; r++) {
                if (!rig.Ribbons[r].Visible) {
                    continue;
                }
                int count = Rig2DRibbonRenderer.BuildPath(rig, def.Ribbons[r], rig.Bones, ribbonPath);
                for (int k = 1; k < count; k++) {
                    Rig2DDebugDraw.Line(sb, toScreen(ribbonPath[k - 1]), toScreen(ribbonPath[k]), ribbonColor * 0.9f, 1f);
                }
            }

            for (int s = 0; s < rig.Solvers.Length; s++) {
                Rig2DSolver solver = rig.Solvers[s];
                if (solver == null || !solver.Enabled) {
                    continue;
                }
                solver.DebugDraw(sb, toScreen);
            }

            Vector2 label = toScreen(rig.RootPosition) + new Vector2(8f, -22f);
            string mirrorTag = rig.Mirrored ? "  mirrored" : string.Empty;
            Rig2DDebugDraw.Text(sb, $"{rig.Name}  bones {rig.Bones.Length}  x{rig.Scale:F2}{mirrorTag}", label, new Color(200, 235, 255), 0.6f);
            if (rig.BindErrors.Count > 0) {
                //声明式绑定没对上：把第一条问题贴在标签下面，其余条数提示去看日志
                string first = rig.BindErrors[0];
                string more = rig.BindErrors.Count > 1 ? $"  (+{rig.BindErrors.Count - 1} more, see log)" : string.Empty;
                Rig2DDebugDraw.Text(sb, $"bind: {first}{more}", label + new Vector2(0f, 12f), new Color(255, 120, 100), 0.6f);
            }
        }

        private static bool IsSolverDriven(Rig2DInstance rig, int bone) {
            for (int s = 0; s < rig.Solvers.Length; s++) {
                Rig2DSolver solver = rig.Solvers[s];
                if (solver == null || !solver.Enabled) {
                    continue;
                }
                ReadOnlySpan<int> driven = solver.DrivenBones;
                for (int k = 0; k < driven.Length; k++) {
                    if (driven[k] == bone) {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
