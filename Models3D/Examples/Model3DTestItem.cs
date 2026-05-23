using InnoVault.Models3D.Wavefront;
using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.Models3D.Examples
{
#if DEBUG
    /// <summary>
    /// 仅 DEBUG 构建启用的 3D 渲染验证物品
    /// <br/>左键持有时在玩家附近实时绘制一个旋转的立方体，用于验证 OBJ/MTL/贴图加载与渲染管线
    /// </summary>
    internal sealed class Model3DTestItem : ModItem
    {
        /// <summary>
        /// 通过 <see cref="VaultLoadenAttribute"/> 自动加载示例 cube 模型
        /// </summary>
        [VaultLoaden("Assets/Models3D/cube")]
        public static VaultObjModel CubeModel { get; set; }

        public override string Texture => "InnoVault/icon";

        public override void SetDefaults() {
            Item.width = 32;
            Item.height = 32;
            Item.useAnimation = Item.useTime = 20;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.useTurn = false;
            Item.value = 1;
            Item.rare = ItemRarityID.Cyan;
            Item.UseSound = SoundID.Item1;
        }

        public override void HoldItem(Player player) {
            if (Main.dedServ || CubeModel == null || !CubeModel.IsValid) {
                return;
            }
            if (player.whoAmI != Main.myPlayer) {
                return;
            }

            float t = (float)Main.timeForVisualEffects * 0.02f;
            Model3DRenderer.Submit(new Model3DInstance(CubeModel) {
                Position = player.Center + new Vector2(0f, -120f),
                Rotation = new Vector3(t * 0.7f, t, t * 0.3f),
                Scale = Vector3.One,
                Tint = Color.White,
                Layer = Model3DLayer.AfterPlayers,
                DepthEnabled = true,
                CullBackface = false,
            });

            // 第二个立方体放在玩家身后用于验证不同层级
            Model3DRenderer.Submit(new Model3DInstance(CubeModel) {
                Position = player.Center + new Vector2(120f, 0f),
                Rotation = new Vector3(0f, t * 1.3f, 0f),
                Scale = new Vector3(0.5f),
                Tint = Color.LightSkyBlue,
                Layer = Model3DLayer.BeforePlayers,
                DepthEnabled = true,
                CullBackface = false,
            });
        }

        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor
            , ref float rotation, ref float scale, int whoAmI) {
            return true;
        }
    }
#endif
}
