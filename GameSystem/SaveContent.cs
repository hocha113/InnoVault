using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 提供对 NBT 数据的路径映射缓存，用于避免重复的磁盘 I/O 操作
    /// </summary>
    /// <remarks>
    /// 此类为内部工具类，仅供框架保存系统使用通过维护路径与 <see cref="TagCompound"/> 的映射，
    /// 可显著提升读取性能，尤其是在频繁访问相同存档文件时<br/>
    /// <br/>
    /// ⚠注意事项：<br/>
    /// 缓存内容为加载时的快照，不会自动同步磁盘变更；<br/>
    /// 调用 <see cref="Invalidate"/> 方法可手动清除指定路径的缓存；<br/>
    /// 所有通过 <see cref="SaveContent{T}.SaveTagToFile"/> 写入的内容应同时调用 <see cref="Set"/> 更新缓存；<br/>
    /// 读取路径应优先使用<see cref = "TryGet" /> 检查缓存是否存在，避免不必要的磁盘访问
    /// </remarks>
    public static class TagCache
    {
        /// <summary>
        /// 内部缓存字典，键为文件路径，值为对应的 <see cref="TagCompound"/> 数据快照
        /// </summary>
        private static readonly ConcurrentDictionary<string, TagCompound> _cache = [];
        private static readonly Queue<string> _order = new();//记录插入顺序
        private const int maxCapacity = 12;//最大缓存标签数量
        /// <summary>
        /// 将指定路径的数据写入缓存（如已存在则覆盖）
        /// </summary>
        /// <param name="path">对应的存储路径（通常为 NBT 文件路径）</param>
        /// <param name="tag">要缓存的 <see cref="TagCompound"/> 实例</param>
        public static void Set(string path, TagCompound tag) {
            if (_cache.ContainsKey(path)) {
                //已经存在，更新内容，不改变顺序
                _cache[path] = tag;
                return;
            }

            //新增缓存
            _cache[path] = tag;
            _order.Enqueue(path);

            //如果超过容量，移除最早缓存
            if (_cache.Count > maxCapacity) {
                string oldest = _order.Dequeue();
                _cache.TryRemove(oldest, out _);
            }
        }
        /// <summary>
        /// 尝试获取指定路径下的缓存内容
        /// </summary>
        /// <param name="path">存储路径</param>
        /// <param name="tag">若缓存存在，输出对应的 <see cref="TagCompound"/> 实例</param>
        /// <returns>若缓存存在，返回 <see langword="true"/>；否则返回 <see langword="false"/></returns>
        public static bool TryGet(string path, out TagCompound tag) => _cache.TryGetValue(path, out tag);
        /// <summary>
        /// 使指定路径的缓存失效，从字典中移除对应项
        /// </summary>
        /// <param name="path">需要清除缓存的文件路径</param>
        public static void Invalidate(string path) {
            if (!_cache.TryRemove(path, out _)) {
                return;
            }

            // 如果移除成功，需要同步从队列里删除
            // 由于队列不支持直接删除中间项，这里重建队列
            var newOrder = new Queue<string>(_order.Count);
            foreach (var p in _order) {
                if (p == path) {
                    continue;
                }
                newOrder.Enqueue(p);
            }

            while (_order.Count > 0) {
                _order.Dequeue();
            }
            foreach (var p in newOrder) {
                _order.Enqueue(p);
            }
        }
        /// <summary>
        /// 清理所有缓存
        /// </summary>
        public static void Clear() {
            _cache.Clear();
            _order.Clear();
        }
    }

    /// <summary>
    /// 一个基本的保存接口
    /// </summary>
    public interface ISaveContent
    {
        /// <summary>
        /// 启用强制刷新
        /// </summary>
        bool ForceReload => false;
        /// <summary>
        /// 内部装载名
        /// </summary>
        abstract string LoadenName { get; }
        /// <summary>
        /// 保存路径
        /// </summary>
        abstract string SavePath { get; }
        /// <summary>
        /// 保存数据，在<see cref="LoadData"/>中编写接收数据的逻辑
        /// </summary>
        /// <param name="tag"></param>
        abstract void SaveData(TagCompound tag);
        /// <summary>
        /// 加载数据，如果<see cref="SaveData"/>没有存入数据，该函数就不会被调用
        /// </summary>
        /// <param name="tag"></param>
        abstract void LoadData(TagCompound tag);
    }

    /// <summary>
    /// 用于基本保存内容的基类
    /// </summary>
    public abstract class SaveContent<T> : VaultType where T : SaveContent<T>
    {
        /// <summary>
        /// 所有实例以单例形式存储于此
        /// </summary>
        public static List<T> SaveContents { get; private set; } = [];
        /// <summary>
        /// 获取泛型参数T对应类型的单实例，仅当直接使用该泛型类型时有效，
        /// 若在子类或多级继承中使用，该属性可能返回基类的实例，
        /// 不一定是当前具体子类的实例
        /// </summary>
        public static T GenericInstance => TypeToInstance[typeof(T)];
        /// <summary>
        /// 从类型映射到对应的实例
        /// </summary>
        public static Dictionary<Type, T> TypeToInstance { get; private set; } = [];
        /// <summary>
        /// 从模组映射到对应的实例列表
        /// </summary>
        public static Dictionary<Mod, List<T>> ModToSaves { get; private set; } = [];
        /// <summary>
        /// 保存时用作标记的前缀，默认返回父类的名字
        /// </summary>
        public virtual string SavePrefix => GetType().BaseType?.Name?.Split('`')[0] ?? "Unknown";
        /// <summary>
        /// 内部装载名
        /// </summary>
        public virtual string LoadenName => $"{SavePrefix}:{Name}";
        /// <summary>
        /// 保存路径，默认为 VaultSave.RootPath + content_{nameof(T)}.nbt;
        /// </summary>
        public virtual string SavePath => Path.Combine(VaultSave.RootPath, $"content_{nameof(T)}.nbt");
        /// <summary>
        /// 检测目标存档文件是否已经存在
        /// </summary>
        public bool HasSave => File.Exists(SavePath);
        /// <inheritdoc/>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }
            SaveContents.Add((T)(object)this);
        }
        /// <inheritdoc/>
        public override void SetupContent() {
            if (!CanLoad()) {
                return;
            }
            ModToSaves.TryAdd(Mod, []);
            TypeToInstance.Add(GetType(), (T)(object)this);
            ModToSaves[Mod].Add((T)(object)this);
            SetStaticDefaults();
        }
        /// <inheritdoc/>
        public override void Unload() {
            SaveContents.Clear();
            ModToSaves.Clear();
            TypeToInstance.Clear();
        }
        /// <summary>
        /// 获取这个类型的单实例
        /// </summary>
        /// <returns></returns>
        public static TTarget GetInstance<TTarget>() where TTarget : SaveContent<T>
            => (TTarget)(object)TypeToInstance[typeof(TTarget)];
        /// <summary>
        /// 尝试从指定路径读取并反序列化出 <see cref="TagCompound"/> 数据
        /// 如果文件不存在则返回 <see langword="false"/> 并输出 <see langword="null"/>
        /// 若读取或反序列化失败则返回 <see langword="false"/> 并记录警告日志
        /// 使用 <see cref="TagIO.FromStream"/> 从压缩 NBT 文件中加载
        /// </summary>
        public static bool TryLoadRootTag(string path, out TagCompound tag, bool forceReload = false) {
            tag = null!;
            if (!File.Exists(path)) {
                TagCache.Invalidate(path);
                return false;
            }

            try {
                if (!forceReload && TagCache.TryGet(path, out tag)) {
                    return true;
                }

                using FileStream stream = File.OpenRead(path);
                tag = TagIO.FromStream(stream);
                TagCache.Set(path, tag);
                return true;
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[TryLoadRootTag] Failed to load NBT file at {path}: {ex}");
                return false;
            }
        }
        /// <summary>
        /// 将传入的 <see cref="TagCompound"/> 数据写入指定路径的 NBT 文件
        /// 如果路径所处的目录不存在则自动创建
        /// 使用 <see cref="TagIO.ToStream"/> 进行序列化并压缩写入
        /// 在写入过程中如出现异常会记录错误日志
        /// </summary>
        public static void SaveTagToFile(TagCompound tag, string path) {
            try {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }

                using FileStream stream = File.Create(path);
                TagIO.ToStream(tag, stream);

                TagCache.Set(path, tag);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[SaveTagToFile] Failed to save NBT: {ex}");
            }
        }
        /// <summary>
        /// 获取该存储对象所拥有的根标签，用于表达NBT存储内容
        /// </summary>
        /// <param name="rootTag"></param>
        /// <param name="forceReload"></param>
        /// <returns></returns>
        public static bool TryGetRootTag(out TagCompound rootTag, bool forceReload = false) {
            if (ModToSaves.Count == 0) {
                rootTag = null;
                return false;
            }
            return TryLoadRootTag(GenericInstance.SavePath, out rootTag, forceReload);
        }
        /// <summary>
        /// 统一执行所有保存任务
        /// </summary>
        public static void DoSave() {
            if (ModToSaves.Count == 0) {
                return;
            }

            TagCompound rootTag = [];

            foreach (var (mod, saves) in ModToSaves) {
                TagCompound modTag = [];

                foreach (var save in saves) {
                    TagCompound saveTag = [];
                    save.SaveData(saveTag);
                    if (saveTag.Count == 0) {
                        continue;
                    }

                    modTag[save.LoadenName] = saveTag;
                }

                if (modTag.Count == 0) {
                    continue;
                }

                rootTag[$"mod:{mod.Name}"] = modTag;
            }

            if (rootTag.Count == 0) {
                return;
            }

            SaveTagToFile(rootTag, GenericInstance.SavePath);
        }
        /// <summary>
        /// 统一执行所有加载任务
        /// </summary>
        public static void DoLoad(bool forceReload = false) {
            if (!TryGetRootTag(out var rootTag, forceReload)) {
                return;
            }

            foreach (var (mod, saves) in ModToSaves) {
                if (!rootTag.TryGet($"mod:{mod.Name}", out TagCompound modTag) || modTag.Count == 0) {
                    continue;
                }

                foreach (var save in saves) {
                    if (!modTag.TryGet(save.LoadenName, out TagCompound saveTag) || saveTag.Count == 0) {
                        continue;
                    }
                    save.LoadData(saveTag);
                }
            }
        }
        /// <summary>
        /// 执行指定类型的保存任务（避免覆盖其他数据）
        /// </summary>
        public static void DoSave<TTarget>(bool forceReload = false) where TTarget : SaveContent<T> {
            TTarget save = GetInstance<TTarget>();

            if (!TryLoadRootTag(save.SavePath, out TagCompound rootTag, forceReload)) {
                rootTag = [];
            }

            //尝试获取原有 modTag,避免全覆盖
            if (!rootTag.TryGet($"mod:{save.Mod.Name}", out TagCompound modTag)) {
                modTag = [];
            }

            TagCompound saveTag = [];
            save.SaveData(saveTag);
            if (saveTag.Count == 0) {
                return;
            }

            //更新当前实例的内容,不影响其他
            modTag[save.LoadenName] = saveTag;
            rootTag[$"mod:{save.Mod.Name}"] = modTag;

            SaveTagToFile(rootTag, save.SavePath);
        }
        /// <summary>
        /// 执行指定类型的加载任务
        /// </summary>
        public static void DoLoad<TTarget>(bool forceReload = false) where TTarget : SaveContent<T> {
            TTarget save = GetInstance<TTarget>();

            if (!TryLoadRootTag(save.SavePath, out TagCompound rootTag, forceReload) || rootTag.Count == 0) {
                return;
            }

            //尝试获取原有 modTag
            if (!rootTag.TryGet($"mod:{save.Mod.Name}", out TagCompound modTag) || modTag.Count == 0) {
                return;
            }

            if (!modTag.TryGet(save.LoadenName, out TagCompound saveTag) || saveTag.Count == 0) {
                return;
            }

            save.LoadData(saveTag);
        }
        /// <summary>
        /// 保存指定接口实例提供的数据
        /// </summary>
        /// <param name="saveContent"></param>
        public static void DoSave(ISaveContent saveContent) {
            if (!TryLoadRootTag(saveContent.SavePath, out TagCompound rootTag, saveContent.ForceReload)) {
                rootTag = [];
            }

            TagCompound saveTag = [];
            saveContent.SaveData(saveTag);
            if (saveTag.Count == 0) {
                return;
            }

            rootTag[saveContent.LoadenName] = saveTag;
            SaveTagToFile(rootTag, saveContent.SavePath);
        }
        /// <summary>
        /// 接收指定接口实例提供的数据
        /// </summary>
        /// <param name="saveContent"></param>
        public static void DoLoad(ISaveContent saveContent) {
            if (!TryLoadRootTag(saveContent.SavePath, out TagCompound rootTag, saveContent.ForceReload) || rootTag.Count == 0) {
                return;
            }

            if (rootTag.TryGet(saveContent.LoadenName, out TagCompound saveTag) || saveTag.Count == 0) {
                return;
            }

            saveContent.LoadData(saveTag);
        }
        /// <summary>
        /// 保存数据，在<see cref="LoadData"/>中编写接收数据的逻辑
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveData(TagCompound tag) {

        }
        /// <summary>
        /// 加载数据，如果<see cref="SaveData"/>没有存入数据，该函数就不会被调用
        /// </summary>
        /// <param name="tag"></param>
        public virtual void LoadData(TagCompound tag) {

        }
    }
}
