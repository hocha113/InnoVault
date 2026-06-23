using InnoVault.GameSystem;
using InnoVault.Narrative.Core;
using InnoVault.Narrative.History;
using InnoVault.Narrative.Portraits;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Presentation.Anchors;
using InnoVault.Narrative.Runtime;
using InnoVault.Narrative.Services;
using InnoVault.Narrative.Styling;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 叙事框架的驱动与生命周期中枢<br/>
    /// 在 UpdateUI（客户端、游戏内）以帧率无关的步长推进运行总控与调度器，<br/>
    /// 并在世界切换 / 模组卸载时清理所有静态状态，避免热重载残留
    /// </summary>
    public sealed class NarrativeSystem : ModSystem
    {
        /// <inheritdoc/>
        public override void UpdateUI(GameTime gameTime) {
            if (Main.dedServ) {
                return;
            }

            float frames = (float)(gameTime.ElapsedGameTime.TotalSeconds * 60.0);
            frames = MathHelper.Clamp(frames <= 0f ? 1f : frames, 0.05f, 5f);

            NarrativeRunner.Update(frames);
            NarrativeScheduler.Tick();
        }

        /// <inheritdoc/>
        public override void OnWorldLoad() {
            NarrativeRunner.Reset();
            if (!Main.dedServ) {
                NarrativeHistory.Load();
            }
        }

        /// <inheritdoc/>
        public override void OnWorldUnload() {
            if (!Main.dedServ) {
                NarrativeHistory.Save();
            }
            NarrativeRunner.Reset();
        }

        /// <inheritdoc/>
        public override void Unload() {
            NarrativeRunner.Reset();
            NarrativeScheduler.Reset();
            NarrativeViews.Clear();
            NarrativeHistory.Reset();
            PanelAnchorResolver.ClearProvider();
            NarrativeScenario.ClearRegistry();
            PortraitRegistry.Clear();
            StyleRegistry.Clear();
            NarrativeServices.ResetToDefaults();

            VaultTypeRegistry<NarrativeScenario>.ClearRegisteredVaults();
            VaultType<NarrativeScenario>.Instances.Clear();
            VaultType<NarrativeScenario>.TypeToInstance.Clear();
            VaultType<NarrativeScenario>.TypeToMod.Clear();
            VaultType<NarrativeScenario>.ByID.Clear();
            VaultType<NarrativeScenario>.UniversalInstances.Clear();
        }
    }
}
