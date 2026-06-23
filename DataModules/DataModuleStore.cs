using System;
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 数据模块容器。按作用域（每玩家 / 每世界 / 每模组全局）各持有一个实例，<br/>
    /// 聚合若干 <see cref="DataModule"/> 并提供版本化的 <see cref="SaveData"/> / <see cref="LoadData"/><br/>
    /// 本类<b>不绑定持久化位置</b>：消费者在自己的 <c>ModPlayer</c> / <c>ModSystem</c> / <c>SaveContent</c><br/>
    /// 的存档钩子里调用本类的存取方法即可。模块按需懒创建；读取时会依据注册表补齐已持久化但尚未创建的模块
    /// </summary>
    public sealed class DataModuleStore
    {
        private const string VersionKey = "__dmVersion";
        private const string ModuleVersionKey = "__v";
        private const int CurrentVersion = 1;

        private readonly Dictionary<Type, DataModule> _byType = [];
        private readonly Dictionary<string, DataModule> _byKey = new(StringComparer.Ordinal);

        /// <summary>当前持有的所有模块</summary>
        public IReadOnlyCollection<DataModule> Modules => _byType.Values;

        /// <summary>获取（必要时创建）指定类型的模块</summary>
        public T Get<T>() where T : DataModule, new() {
            if (_byType.TryGetValue(typeof(T), out DataModule module)) {
                return (T)module;
            }
            //未注册的模块（CanLoad() 返回 false 或未被自动加载）仍可临时创建，但它不会在读档时被自动补齐，
            //因此这里给出警告，避免静默绕过 VaultType 的注册 / CanLoad 约束
            if (!DataModule.TypeToInstance.ContainsKey(typeof(T))) {
                VaultMod.Instance.Logger.Warn($"DataModule '{typeof(T).FullName}' is used in a store but is not registered (CanLoad() returned false or it was not autoloaded); it will not be restored from save.");
            }
            T created = new();
            Add(created);
            return created;
        }

        /// <summary>尝试获取已存在的模块（不创建）</summary>
        public bool TryGet<T>(out T module) where T : DataModule {
            if (_byType.TryGetValue(typeof(T), out DataModule found)) {
                module = (T)found;
                return true;
            }
            module = null;
            return false;
        }

        /// <summary>按 SaveKey 获取已存在的模块</summary>
        public DataModule GetByKey(string key)
            => key != null && _byKey.TryGetValue(key, out DataModule module) ? module : null;

        /// <summary>显式加入一个模块实例（同 SaveKey 冲突会记录错误）</summary>
        public void Add(DataModule module) {
            if (module == null) {
                return;
            }
            if (_byKey.TryGetValue(module.SaveKey, out DataModule existing) && existing.GetType() != module.GetType()) {
                VaultMod.Instance.Logger.Error($"DataModuleStore SaveKey conflict: '{module.SaveKey}' between {existing.GetType().FullName} and {module.GetType().FullName}");
                return;
            }
            _byType[module.GetType()] = module;
            _byKey[module.SaveKey] = module;
        }

        /// <summary>写出全部模块</summary>
        public void SaveData(TagCompound tag) {
            tag[VersionKey] = CurrentVersion;
            foreach (DataModule module in _byType.Values) {
                TagCompound sub = [];
                try {
                    module.SaveData(sub);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"DataModule '{module.SaveKey}' SaveData threw: {ex}");
                    continue;
                }
                sub[ModuleVersionKey] = module.Version;
                tag[module.SaveKey] = sub;
            }
        }

        /// <summary>读入全部模块；会为已持久化但尚未创建的模块自动补齐实例</summary>
        public void LoadData(TagCompound tag) {
            //先重置现有模块，避免复用实例时残留上一份数据
            foreach (DataModule module in _byType.Values) {
                module.Reset();
            }

            foreach (KeyValuePair<string, object> entry in tag) {
                if (entry.Key == VersionKey || entry.Value is not TagCompound sub) {
                    continue;
                }

                DataModule module = GetByKey(entry.Key) ?? CreateRegistered(entry.Key);
                if (module == null) {
                    continue;
                }

                int loadedVersion = sub.TryGet(ModuleVersionKey, out int v) ? v : 0;
                try {
                    module.LoadData(sub, loadedVersion);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"DataModule '{entry.Key}' LoadData threw: {ex}");
                }
            }
        }

        private DataModule CreateRegistered(string key) {
            DataModule module = DataModuleRegistry.TryCreate(key);
            if (module != null) {
                Add(module);
            }
            return module;
        }

        /// <summary>深拷贝整个容器（用于 <c>ModPlayer.Clone</c> 等语义）</summary>
        public DataModuleStore Clone() {
            DataModuleStore clone = new();
            foreach (DataModule module in _byType.Values) {
                clone.Add(module.Clone());
            }
            return clone;
        }

        /// <summary>重置全部模块为默认值（保留实例）</summary>
        public void Reset() {
            foreach (DataModule module in _byType.Values) {
                module.Reset();
            }
        }

        /// <summary>清空全部模块实例</summary>
        public void Clear() {
            _byType.Clear();
            _byKey.Clear();
        }
    }
}
