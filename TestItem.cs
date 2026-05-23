using InnoVault.Actors;
using InnoVault.Models3D.Runtime;
using InnoVault.Models3D.Wavefront;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault
{
#if DEBUG
    internal class TestItem : ModItem
    {
        public override string Texture => "InnoVault/icon";

        /// <summary>
        /// 通过 <see cref="VaultLoadenAttribute"/> 自动加载示例 cube 模型
        /// </summary>
        [VaultLoaden("Assets/Models3D/cube")]
        public static VaultObjModel CubeModel { get; set; }

        public override bool IsLoadingEnabled(Mod mod) {
            return true;
        }

        public override void SetDefaults() {
            Item.width = 80;
            Item.height = 80;
            Item.damage = 9999;
            Item.DamageType = DamageClass.Default;
            Item.useAnimation = Item.useTime = 13;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 2.25f;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.shootSpeed = 8f;
            Item.shoot = ProjectileID.PurificationPowder;
            Item.value = 7;
            Item.rare = ItemRarityID.Yellow;
        }

        public override void UpdateInventory(Player player) {

        }

        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI) {
            return false;
        }

        public override bool AltFunctionUse(Player player) {
            return true;
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

        public override bool? UseItem(Player player) {
            if (player.whoAmI != Main.myPlayer) {
                return true;
            }

            ////左键：进入石头维度
            //if (player.altFunctionUse != 2) {
            //    if (!DimensionLoader.AnyActive()) {
            //        //当前在主世界，进入石头维度
            //        if (DimensionLoader.Enter<StoneDimension>()) {
            //            VaultUtils.Text("正在进入石头维度...", Color.Cyan);
            //        }
            //        else {
            //            VaultUtils.Text("无法进入石头维度！", Color.Red);
            //        }
            //    }
            //    else {
            //        VaultUtils.Text("你已经在维度中了！右键退出", Color.Yellow);
            //    }
            //}
            ////右键：退出维度
            //else {
            //    if (DimensionLoader.AnyActive()) {
            //        DimensionLoader.Exit();
            //        VaultUtils.Text("正在退出维度...", Color.Cyan);
            //    }
            //    else {
            //        VaultUtils.Text("你已经在主世界了！", Color.Yellow);
            //    }
            //}

            return true;
        }
    }

    internal class TestProj : ModProjectile
    {
        public override string Texture => "InnoVault/icon";
        public override void SetDefaults() {
            Projectile.width = Projectile.height = 32;
            Projectile.friendly = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 60;
        }

        public override void AI() {
            if (Projectile.ai[0] == 0 && !VaultUtils.isClient) {
                ActorLoader.NewActor<TestActor>(Projectile.Center);
            }
            Projectile.ai[0]++;
        }
    }

    internal class TestActor : Actor
    {
        [SyncVar]
        public int NumValue;
        public override void OnSpawn(params object[] args) {
            Width = 100;
            Height = 100;
            NetUpdate = true;
        }
        public override void AI() {
            if (HitBox.Intersects(Main.MouseWorld.GetRectangle(1))
                && Main.mouseLeft) {
                NumValue++;
                NetUpdate = true;
            }
        }
        public override bool PreDraw(SpriteBatch spriteBatch, ref Color drawColor) {
            Main.spriteBatch.Draw(TextureAssets.Projectile[ModContent.ProjectileType<TestProj>()].Value
                , Center - Main.screenPosition, null, Color.Red, 0, Velocity, 1f, SpriteEffects.None, 0);
            Utils.DrawBorderString(spriteBatch, $"NumValue: {NumValue}", Center - Main.screenPosition + new Vector2(0, 40), Color.White);
            return false;
        }
    }
#endif
}

