using InnoVault.Dimensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class TestItem : ModItem
    {
        public override string Texture => "InnoVault/icon";

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
        }

        public override bool? UseItem(Player player) {
            DimensionSystem.Enter<ExampleMirrorDimension>();
            //if (player.altFunctionUse == 2) {
            //    MySaveStructure.DoSave<MySaveStructure>();
            //}
            //else {
            //    MySaveStructure.DoLoad<MySaveStructure>();
            //}            
            return true;
        }
    }

    //internal class MySaveStructure : SaveStructure
    //{
    //    public override void SaveData(TagCompound tag) {
    //        SaveRegion(tag, Main.MouseWorld.ToTileCoordinates16().GetRectangle(120));
    //    }

    //    public override void LoadData(TagCompound tag) {
    //        LoadRegion(tag, Main.MouseWorld.ToTileCoordinates16());
    //    }
    //}
}
