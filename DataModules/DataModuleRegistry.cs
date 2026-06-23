using InnoVault.GameSystem;
using System;
using System.Collections.Generic;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 数据模块类型注册表。在 InnoVault 加载完成后读取 <see cref="VaultTypeRegistry{TVault}"/> 中的具体 <see cref="DataModule"/> 模板，<br/>
    /// 建立 SaveKey 到类型的映射并检测冲突；供 <see cref="DataModuleStore"/> 在读档时按 SaveKey 补齐模块实例<br/>
    /// 要求模块类型具有公共无参构造函数
    /// </summary>
    public static class DataModuleRegistry
    {
        private static bool _built;
        private static readonly Dictionary<string, Type> _keyToType = new(StringComparer.Ordinal);
        private static readonly List<Type> _types = [];

        /// <summary>所有已发现的模块类型</summary>
        public static IReadOnlyList<Type> Types {
            get {
                EnsureBuilt();
                return _types;
            }
        }

        /// <summary>若尚未构建则立即构建（懒触发兜底）</summary>
        public static void EnsureBuilt() {
            if (!_built) {
                Build();
            }
        }

        /// <summary>基于 VaultType 注册结果构建类型映射（由加载器在 PostSetupContent 调用）</summary>
        public static void Build() {
            _built = true;
            _keyToType.Clear();
            _types.Clear();

            foreach (DataModule template in VaultTypeRegistry<DataModule>.RegisteredVaults) {
                Type type = template.GetType();

                string key = template.SaveKey;
                if (_keyToType.TryGetValue(key, out Type other) && other != type) {
                    VaultMod.Instance.Logger.Error($"DataModule SaveKey conflict: '{key}' used by both {other.FullName} and {type.FullName}");
                    continue;
                }
                _keyToType[key] = type;
                _types.Add(type);
            }
        }

        /// <summary>按 SaveKey 创建一个新的模块实例，未知 Key 返回 <see langword="null"/></summary>
        public static DataModule TryCreate(string key) {
            EnsureBuilt();
            if (key != null && _keyToType.TryGetValue(key, out Type type)) {
                try {
                    return (DataModule)Activator.CreateInstance(type);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"DataModule create failed for key '{key}': {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>清空注册表（卸载时调用）</summary>
        public static void Clear() {
            _built = false;
            _keyToType.Clear();
            _types.Clear();
            DataModuleReflector.ClearCache();
        }
    }

    /// <summary>
    /// 把 <see cref="DataModuleRegistry"/> 接入 InnoVault 的加载生命周期：<br/>
    /// 在 PostSetupContent 构建类型映射（便于尽早暴露冲突），在卸载时清理 DataModule 自身与 VaultType 注册状态
    /// </summary>
    internal sealed class DataModuleLoader : IVaultLoader
    {
        void IVaultLoader.SetupData() => DataModuleRegistry.Build();
        void IVaultLoader.UnLoadData() {
            DataModuleRegistry.Clear();
            VaultTypeRegistry<DataModule>.ClearRegisteredVaults();
            VaultType<DataModule>.Instances.Clear();
            VaultType<DataModule>.TypeToInstance.Clear();
            VaultType<DataModule>.TypeToMod.Clear();
            VaultType<DataModule>.ByID.Clear();
            VaultType<DataModule>.UniversalInstances.Clear();
        }
    }
}
