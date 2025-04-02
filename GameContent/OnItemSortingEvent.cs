using System.Collections.Generic;
using System.Reflection;
using Terraria.ModLoader;
using Terraria.UI;

namespace InnoVault.GameContent
{
    //这个类的代码用于解决ItemSorting的加载不稳定问题，排序白名单的存在意义不大，所以暂时禁用掉他们
    internal class OnItemSortingEvent : IVaultLoader
    {
        public delegate void On_VoidFunc_Static_Dalegate();
        private static FieldInfo _layerWhiteListsField;
        void IVaultLoader.LoadData() {
            _layerWhiteListsField = typeof(ItemSorting).GetField("_layerWhiteLists", BindingFlags.Static | BindingFlags.NonPublic);
            MonoModHooks.Add(typeof(ItemSorting).GetMethod("SetupWhiteLists", BindingFlags.Static | BindingFlags.Public), SetupWhiteListsHook);
        }
        void IVaultLoader.UnLoadData() => _layerWhiteListsField = null;
        public static void SetupWhiteListsHook(On_VoidFunc_Static_Dalegate orig) => _layerWhiteListsField.SetValue(null, new Dictionary<string, List<int>>());
    }
}
