using System;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 反射驱动的数据模块：自动序列化公共可读写属性与公共可写字段（bool/int/long/float/double/string/枚举）。<br/>
    /// 适合"一组扁平标记 / 计数"的常见场景（任务标记、解锁位、变体计数等），零样板。<br/>
    /// 需要集合、嵌套结构或自定义迁移时，请直接继承 <see cref="DataModule"/> 手写序列化
    /// </summary>
    public abstract class AutoDataModule : DataModule
    {
        /// <inheritdoc/>
        public override void SaveData(TagCompound tag) => DataModuleReflector.Save(this, tag);

        /// <inheritdoc/>
        public override void LoadData(TagCompound tag, int loadedVersion) => DataModuleReflector.Load(this, tag);

        /// <inheritdoc/>
        public override void Reset() => DataModuleReflector.Reset(this);

        /// <inheritdoc/>
        public override DataModule Clone() {
            AutoDataModule clone = (AutoDataModule)Activator.CreateInstance(GetType());
            DataModuleReflector.Copy(this, clone);
            return clone;
        }
    }
}
