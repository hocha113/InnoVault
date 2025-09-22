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
        /// 所有已注册的实例
        /// </summary>
        public readonly static List<T> Instances = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, T> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public static Dictionary<int, Dictionary<Type, T>> ByID { get; internal set; } = [];
        /// <summary>
        /// 所有全局的实例集合
        /// </summary>
        public static List<T> UniversalInstances { get; internal set; } = [];
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
            VaultList<T>.Register((T)this);
            Instances.Add((T)this);
            TypeToInstance[GetType()] = (T)this;
            VaultRegister();
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void SetupContent() {
            if (!CanLoad()) {
                return;
            }
            VaultList<T>.FinishLoading();
            VaultSetup();
        }
        /// <summary>
        /// 如果继承了这个类，重写这个函数以进行内容加载
        /// </summary>
        protected virtual void VaultRegister() {

        }
        /// <summary>
        /// 如果继承了这个类，重写这个函数以进行内容加载
        /// </summary>
        public virtual void VaultSetup() {

        }
    }
}
