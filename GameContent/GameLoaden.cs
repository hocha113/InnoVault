using InnoVault.GameContent.BaseEntity;
using System;
using System.Collections.Generic;

namespace InnoVault.GameContent
{
    internal class GameLoaden : IVaultLoader
    {
        /// <summary>
        /// 所有继承了<see cref="BaseHeldProj"/>的类的实例存储于此，通过Type进行映射
        /// </summary>
        public static Dictionary<Type, BaseHeldProj> BaseHeldProj_Type_To_Instances { get; private set; } = [];

        void IVaultLoader.SetupData() {
            var _list = VaultUtils.GetSubclassInstances<BaseHeldProj>();
            foreach (var typed in _list) {
                BaseHeldProj_Type_To_Instances[typed.GetType()] = typed;
            }
        }

        void IVaultLoader.UnLoadData() {
            BaseHeldProj_Type_To_Instances?.Clear();
        }
    }
}
