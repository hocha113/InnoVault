using System;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 通用模块化保存单元。它是 InnoVault 的<b>独立数据系统</b>，不属于任何具体玩法子系统，<br/>
    /// 可服务叙事、任务、好感、科技树、解锁、世界事件、UI 数据等。<br/>
    /// 一个模块就是"一组带版本的可序列化字段"，由 <see cref="DataModuleStore"/> 聚合并持久化。<br/>
    /// 模块类型本身继承 <see cref="VaultType{T}"/>，因此可被 InnoVault 统一发现、注册和检查 Key 冲突；<br/>
    /// 但 <see cref="DataModuleStore"/> 中保存的是按作用域创建的独立数据实例，而不是注册期的 VaultType 模板单例。<br/>
    /// 默认实现会反射序列化公共可读写属性与公共可写字段；需要集合、嵌套结构或自定义迁移时，直接重写
    /// <see cref="SaveData"/> / <see cref="LoadData"/>，不调用 base 即可完全接管
    /// </summary>
    public abstract class DataModule : VaultType<DataModule>
    {
        /// <summary>
        /// 序列化键，默认使用 <c>ModName/TypeName</c>。<br/>
        /// DataModule 面向所有消费模组，不能只用短类名，否则不同模组中的同名模块会在同一个
        /// <see cref="DataModuleStore"/> 或持久化 Tag 中发生冲突
        /// </summary>
        public virtual string SaveKey {
            get {
                Type type = GetType();
                return TypeToMod.TryGetValue(type, out var mod)
                    ? GetFullName(mod.Name, type.Name)
                    : type.FullName;
            }
        }

        /// <summary>模块数据版本，递增以支持迁移</summary>
        public virtual int Version => 1;

        /// <summary>写出本模块字段</summary>
        public virtual void SaveData(TagCompound tag) => DataModuleReflector.Save(this, tag);

        /// <summary>读入本模块字段</summary>
        /// <param name="tag">本模块的子标签</param>
        /// <param name="loadedVersion">存档中记录的版本，用于迁移；旧档可能小于 <see cref="Version"/></param>
        public virtual void LoadData(TagCompound tag, int loadedVersion) => DataModuleReflector.Load(this, tag);

        /// <summary>重置为默认值（重新开始 / 清档时使用）</summary>
        public virtual void Reset() => DataModuleReflector.Reset(this);

        /// <summary>
        /// 深拷贝本模块。默认通过反射字段拷贝实现，子类可重写为更高效或更深层的拷贝
        /// </summary>
        public virtual DataModule Clone() {
            DataModule clone = (DataModule)Activator.CreateInstance(GetType());
            DataModuleReflector.Copy(this, clone);
            return clone;
        }
    }
}
