using System;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 通用模块化保存单元。它是 InnoVault 的<b>独立数据系统</b>，不属于任何具体玩法子系统，<br/>
    /// 可服务叙事、任务、好感、科技树、解锁、世界事件、UI 数据等。<br/>
    /// 一个模块就是"一组带版本的可序列化字段"，由 <see cref="DataModuleStore"/> 聚合并持久化。<br/>
    /// 需要零样板时改继承 <see cref="AutoDataModule"/>，需要完全手控时直接继承本类
    /// </summary>
    public abstract class DataModule
    {
        /// <summary>序列化键，默认取类名；同一 <see cref="DataModuleStore"/> 内必须唯一</summary>
        public virtual string SaveKey => GetType().Name;

        /// <summary>模块数据版本，递增以支持迁移</summary>
        public virtual int Version => 1;

        /// <summary>写出本模块字段</summary>
        public abstract void SaveData(TagCompound tag);

        /// <summary>读入本模块字段</summary>
        /// <param name="tag">本模块的子标签</param>
        /// <param name="loadedVersion">存档中记录的版本，用于迁移；旧档可能小于 <see cref="Version"/></param>
        public abstract void LoadData(TagCompound tag, int loadedVersion);

        /// <summary>重置为默认值（重新开始 / 清档时使用）</summary>
        public virtual void Reset() { }

        /// <summary>
        /// 深拷贝本模块。默认通过一次"序列化—反序列化"往返实现，子类可重写为更高效的字段拷贝
        /// </summary>
        public virtual DataModule Clone() {
            DataModule clone = (DataModule)Activator.CreateInstance(GetType());
            TagCompound tag = [];
            SaveData(tag);
            clone.LoadData(tag, Version);
            return clone;
        }
    }
}
