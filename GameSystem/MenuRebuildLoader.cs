using Microsoft.Xna.Framework;
using System.Reflection;
using Terraria;
using static InnoVault.GameSystem.MenuOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于菜单重制节点的钩子均挂载于此处
    /// </summary>
    public class MenuRebuildLoader : IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_DrawMenu_Dlelgate(Main main, GameTime gameTime);
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
        void IVaultLoader.LoadData() {
            VaultHook.Add(typeof(Main).GetMethod("DrawMenu", BindingFlags.Instance | BindingFlags.NonPublic), OnDrawMenuHook);
        }

        private static void OnDrawMenuHook(On_DrawMenu_Dlelgate orig, Main main, GameTime gameTime) {
            bool result = true;
            foreach (var inds in Instances) {
                if (inds.ignoreBug > 0) {
                    inds.ignoreBug--;
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    result = inds.DrawMenu(gameTime);
                } catch {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                }
            }

            if (result) {
                orig.Invoke(main, gameTime);
                return;
            }

            Main.spriteBatch.End();
        }
    }
}
