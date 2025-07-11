using System;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
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
        /// <summary>
        /// 获取这个类型的单实例
        /// </summary>
        /// <returns></returns>
        public static TargetType GetInstance<TargetType>()
            => (TargetType)(object)TypeToInstance[typeof(TargetType)]; 
        /// <summary>
        /// 尝试从指定路径读取并反序列化出 <see cref="TagCompound"/> 数据
        /// 如果文件不存在则返回 <see langword="false"/> 并输出 <see langword="null"/>
        /// 若读取或反序列化失败则返回 <see langword="false"/> 并记录警告日志
        /// 使用 <see cref="TagIO.FromStream"/> 从压缩 NBT 文件中加载
        /// </summary>
        public static bool TryLoadRootTag(string path, out TagCompound tag) {
            tag = null!;
            if (!File.Exists(path)) {
                return false;
            }

            try {
                using FileStream stream = File.OpenRead(path);
                tag = TagIO.FromStream(stream);
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
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[SaveTagToFile] Failed to save NBT: {ex}");
            }
        }
        /// <summary>
        /// 获取该存储对象所拥有的根标签，用于表达NBT存储内容
        /// </summary>
        /// <param name="rootTag"></param>
        /// <returns></returns>
        public static bool TryGetRootTag(out TagCompound rootTag) {
            if (ModToSaves.Count == 0) {
                rootTag = null;
                return false;
            }
            return TryLoadRootTag(Instance.SavePath, out rootTag);
        }
        /// <summary>
        /// 统一执行所有保存任务
        /// </summary>
        public static void DoSave() {
            if (ModToSaves.Count == 0) {
                return;
            }

            TagCompound rootTag = [];

            string prefix = Instance.SavePrefix;
            foreach (var (mod, saves) in ModToSaves) {
                TagCompound modTag = [];

                foreach (var save in saves) {
                    TagCompound saveTag = [];
                    save.SaveData(saveTag);
                    modTag[$"{prefix}:{save.Name}"] = saveTag;
                }

                rootTag[$"mod:{mod.Name}"] = modTag;
            }

            SaveTagToFile(rootTag, Instance.SavePath);
        }
        /// <summary>
        /// 统一执行所有加载任务
        /// </summary>
        public static void DoLoad() {
            if (!TryGetRootTag(out var rootTag)) {
                return;
            }

            string prefix = Instance.SavePrefix;
            foreach (var (mod, saves) in ModToSaves) {
                if (!rootTag.TryGet($"mod:{mod.Name}", out TagCompound modTag)) {
                    continue;
                }

                foreach (var save in saves) {
                    if (modTag.TryGet($"{prefix}:{save.Name}", out TagCompound saveTag)) {
                        save.LoadData(saveTag);
                    }
                }
            }
        }
        /// <summary>
        /// 保存数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveData(TagCompound tag) {

        }
        /// <summary>
        /// 加载数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void LoadData(TagCompound tag) {

        }
    }
}
