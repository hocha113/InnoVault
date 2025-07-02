using InnoVault.GameSystem;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultSystem : ModSystem
    {
        public override void PostAddRecipes() {
            foreach (var loader in VaultMod.Loaders) {
                loader.AddRecipesData();
            }

            //遍历所有配方，执行对应的配方修改，这个应该执行在最前，防止覆盖后续的修改操作
            for (int i = 0; i < Recipe.numRecipes; i++) {
                Recipe recipe = Main.recipe[i];

                if (!ItemOverride.TryFetchByID(recipe.createItem.type, out Dictionary<Type, ItemOverride> values)) {
                    continue;
                }

                foreach (var value in values.Values) {
                    value.ModifyRecipe(recipe);
                }
            }
        }

        public override void AddRecipes() {
            for (int i = 0; i < ItemLoader.ItemCount; i++) {
                if (!ItemOverride.TryFetchByID(i, out Dictionary<Type, ItemOverride> values)) {
                    continue;
                }

                foreach (var value in values.Values) {
                    value.AddRecipe();
                }
            }
        }
    }
}
