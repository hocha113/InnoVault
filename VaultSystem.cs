using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultSystem : ModSystem
    {
        public override void PostAddRecipes() {
            foreach (var loader in VaultMod.Loaders) {
                loader.AddRecipesData();
            }
        }
    }
}
