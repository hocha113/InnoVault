using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 实现InnoVault的基本类型
    /// </summary>
    public abstract class VaultType : ModType
    {
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
    }
}
