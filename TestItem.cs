using InnoVault.Actors;
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
        }

        public override bool? UseItem(Player player) {
            if (player.whoAmI == Main.myPlayer) {
                Projectile.NewProjectile(player.FromObjectGetParent(), player.Center, Vector2.Zero, ModContent.ProjectileType<TestProj>(), 0, 0, player.whoAmI);
            }
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
