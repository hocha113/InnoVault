using InnoVault.BehaviorTrees;
using InnoVault.StateMachines;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Debugs
{
    /// <summary>
    /// 半透明状态机 / 行为树调试叠层<br/>
    /// 受<see cref="DebugSettings.StateMachineShowOverlay"/>与<see cref="DebugSettings.BehaviorTreeShowOverlay"/>控制<br/>
    /// 通过<c>/vaultdebug</c>面板的"StateMachine"页打开/关闭；<br/>
    /// 渲染内容来自<see cref="StateMachineDebugger.FormatActiveOverview"/>与<see cref="BehaviorTreeDebugger.FormatActiveOverview"/>
    /// </summary>
    internal class StateMachineDebugOverlay : UIHandle
    {
        private const float Scale = 0.6f;
        private const float LineHeight = 16f;
        private const float PaddingX = 8f;
        private const float PaddingY = 8f;
        private const float MaxWidth = 600f;

        public override bool Active => DebugSettings.StateMachineShowOverlay || DebugSettings.BehaviorTreeShowOverlay;

        public override void OnEnterWorld() {
            DrawPosition = new Vector2(20f, 100f);
        }

        public override void Draw(SpriteBatch spriteBatch) {
            //StringBuilder 在 Debugger 静态方法里临时分配——overlay 仅调试时打开，性能不是首要考量
            List<string> lines = [];
            if (DebugSettings.StateMachineShowOverlay) {
                string fsm = StateMachineDebugger.FormatActiveOverview();
                if (!string.IsNullOrEmpty(fsm)) {
                    lines.Add("== State Machines ==");
                    AppendSplit(fsm, lines);
                }
            }
            if (DebugSettings.BehaviorTreeShowOverlay) {
                string bt = BehaviorTreeDebugger.FormatActiveOverview();
                if (!string.IsNullOrEmpty(bt)) {
                    lines.Add("== Behavior Trees ==");
                    AppendSplit(bt, lines);
                }
            }
            if (lines.Count == 0) {
                return;
            }

            float width = Math.Min(MaxWidth, MeasureMaxWidth(lines));
            float height = lines.Count * LineHeight;
            Rectangle bg = new((int)DrawPosition.X, (int)DrawPosition.Y, (int)(width + PaddingX * 2f), (int)(height + PaddingY * 2f));
            Texture2D px = VaultAsset.placeholder2.Value;
            spriteBatch.Draw(px, bg, new Rectangle(0, 0, 1, 1), new Color(10, 18, 30) * 0.78f);
            spriteBatch.Draw(px, new Rectangle(bg.X, bg.Y, bg.Width, 2), new Rectangle(0, 0, 1, 1), new Color(60, 140, 220) * 0.85f);
            spriteBatch.Draw(px, new Rectangle(bg.X, bg.Bottom - 2, bg.Width, 2), new Rectangle(0, 0, 1, 1), new Color(60, 140, 220) * 0.55f);

            float y = bg.Y + PaddingY;
            float x = bg.X + PaddingX;
            for (int i = 0; i < lines.Count; i++) {
                string line = lines[i];
                Color color = line.StartsWith("==") ? new Color(150, 220, 255) : new Color(200, 220, 240);
                Utils.DrawBorderString(spriteBatch, line, new Vector2(x, y), color, Scale);
                y += LineHeight;
            }
        }

        private static void AppendSplit(string text, List<string> sink) {
            //避免引入 StringSplitOptions.RemoveEmptyEntries——空行可以提示分隔不同探针
            string[] parts = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++) {
                if (parts[i].Length == 0 && i == parts.Length - 1) {
                    //跳过 AppendLine 末尾的一行空行，避免视觉脏数据
                    continue;
                }
                sink.Add(parts[i]);
            }
        }

        private static float MeasureMaxWidth(List<string> lines) {
            float max = 0f;
            for (int i = 0; i < lines.Count; i++) {
                float w = FontAssets.MouseText.Value.MeasureString(lines[i]).X * Scale;
                if (w > max) {
                    max = w;
                }
            }
            return max;
        }
    }
}
