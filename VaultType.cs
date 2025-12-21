using InnoVault.GameSystem;
using System;
using System.Collections.Generic;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 实现InnoVault的基本类型
    /// </summary>
    public abstract class VaultType<T> : ModType where T : VaultType<T>
    {
        /// <summary>
        /// 是否自动加载<see cref="TypeToMod"/>,默认返回<see langword="true"/>
        /// </summary>
        protected virtual bool AutoMapMod => true;
        /// <summary>
        /// 是否自动加载<see cref="AutoMapID"/>和<see cref="TypeToID"/>,默认返回<see langword="true"/>
        /// </summary>
        protected virtual bool AutoMapID => true;
        /// <summary>
        /// 是否自动在<see cref="ModType.Register"/>中调用<see cref="VaultTypeRegistry{T}.Register(T)"/>,默认返回<see langword="true"/>
        /// </summary>
        protected virtual bool AutoVaultRegistryRegister => true;
        /// <summary>
        /// 是否自动在<see cref="ModType.SetupContent"/>中调用<see cref="VaultTypeRegistry{T}.CompleteLoading"/>,默认返回<see langword="true"/>
        /// </summary>
        protected virtual bool AutoVaultRegistryFinishLoading => true;
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<T> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, T> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<int, T> IDToInstance { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, int> TypeToID { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到Mod实例
        /// </summary>
        public static Dictionary<Type, Mod> TypeToMod { get; internal set; } = [];
        /// <summary>
        /// 按ID和类型分类的实例集合
        /// </summary>
        public static Dictionary<int, Dictionary<Type, T>> ByID { get; internal set; } = [];
        /// <summary>
        /// 所有的通用实例集合
        /// </summary>
        public static List<T> UniversalInstances { get; internal set; } = [];
        /// <summary>
        /// 该实例的全局唯一ID
        /// </summary>
        public int ID;
        /// <summary>
        /// 程序进行防御性处理时会用到的值，如果该实例内部发生错误，则会将该值设置为大于0的值，期间不会再自动调用该实例
        /// 这个值应当每帧减一，直到不再大于0
        /// </summary>
        public int ignoreBug = -1;
        /// <summary>
        /// 记录发生错误的次数，不要自行设置它
        /// </summary>
        public int errorCount;
        /// <summary>
        /// 是否加载这个实例，默认返回<see langword="true"/>
        /// </summary>
        /// <returns></returns>
        public virtual bool CanLoad() { return true; }
        /// <summary>
        /// 是否进行覆盖
        /// </summary>
        /// <returns></returns>
        public virtual bool CanOverride() {
            return true;
        }
        /// <summary>
        /// 返回该TP实体的填充名
        /// </summary>
        /// <param name="modName"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetFullName(string modName, string name) => modName + "/" + name;//设置这个函数是为了防止其他地方硬编码拼接内部名
        /// <summary>
        /// 注册这个实例到列表中
        /// </summary>
        protected sealed override void Register() {
            if (!CanLoad()) {
                return;
            }
            if (AutoMapMod) {
                TypeToMod[GetType()] = Mod;
            }
            if (AutoMapID) {
                ID = IDToInstance.Count;
                IDToInstance[ID] = (T)this;
                TypeToID[GetType()] = ID;
            }
            if (AutoVaultRegistryRegister) {
                VaultTypeRegistry<T>.Register((T)this);
            }
            VaultRegister();
        }
        /// <summary>
        /// 如果继承了这个类，请重写这个函数以进行内容加载
        /// </summary>
        protected virtual void VaultRegister() {

        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void SetupContent() {
            if (!CanLoad()) {
                return;
            }
            if (AutoVaultRegistryFinishLoading) {
                VaultTypeRegistry<T>.CompleteLoading();
            }
            VaultSetup();
        }
        /// <summary>
        /// 如果继承了这个类，请重写这个函数以进行内容加载
        /// </summary>
        public virtual void VaultSetup() {

        }
    }
}
