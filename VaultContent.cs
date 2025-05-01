using InnoVault.GameContent;
using InnoVault.GameContent.BaseEntity;
using InnoVault.PRT;
using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 一个额外的辅助类，用于快速查询一些内容的对象
    /// </summary>
    public static class VaultContent
    {
        /// <summary>
        /// 获取指定模组关联的粒子实例（继承自 <see cref="BasePRT"/>），通过类型 <typeparamref name="T"/> 查找
        /// 从全局 ID 到实例的映射中获取实例，基于类型推导的 ID
        /// </summary>
        /// <typeparam name="T">粒子类型，必须继承自 <see cref="BasePRT"/></typeparam>
        /// <param name="_">模组实例，此方法未使用，仅为扩展方法兼容性保留</param>
        /// <returns>类型为 <typeparamref name="T"/> 的粒子实例，若未找到或实例类型不匹配则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若 ID 在 <see cref="PRTLoader.PRT_IDToInstances"/> 中不存在，可能抛出此异常</exception>
        public static T FindPRT<T>(this Mod _) where T : BasePRT => PRTLoader.PRT_IDToInstances[PRTLoader.GetParticleID<T>()] as T;
        /// <summary>
        /// 获取指定模组关联的 UI 句柄实例（继承自 <see cref="UIHandle"/>），通过类型 <typeparamref name="T"/> 查找
        /// 从全局 ID 到实例的映射中获取实例，基于类型推导的 ID
        /// </summary>
        /// <typeparam name="T">UI 句柄类型，必须继承自 <see cref="UIHandle"/></typeparam>
        /// <param name="_">模组实例，此方法未使用，仅为扩展方法兼容性保留</param>
        /// <returns>类型为 <typeparamref name="T"/> 的 UI 句柄实例，若未找到或实例类型不匹配则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若 ID 在 <see cref="UIHandleLoader.UIHandles"/> 中不存在，可能抛出此异常</exception>
        public static T FindUI<T>(this Mod _) where T : UIHandle => UIHandleLoader.UIHandles[UIHandleLoader.GetUIHandleID<T>()] as T;
        /// <summary>
        /// 获取指定模组关联的瓦片处理器实例（继承自 <see cref="TileProcessor"/>），通过类型 <typeparamref name="T"/> 查找
        /// 从全局 ID 到实例的映射中获取实例，基于类型推导的 ID
        /// </summary>
        /// <typeparam name="T">瓦片处理器类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="_">模组实例，此方法未使用，仅为扩展方法兼容性保留</param>
        /// <returns>类型为 <typeparamref name="T"/> 的瓦片处理器实例，若未找到或实例类型不匹配则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若 ID 在 <see cref="TileProcessorLoader.TP_Instances"/> 中不存在，可能抛出此异常</exception>
        public static T FindTP<T>(this Mod _) where T : TileProcessor => TileProcessorLoader.TP_Instances[TileProcessorLoader.GetModuleID<T>()] as T;
        /// <summary>
        /// 获取指定模组关联的手持投射物实例（继承自 <see cref="BaseHeldProj"/>），通过类型 <typeparamref name="T"/> 查找
        /// 从全局类型到实例的映射中直接获取实例
        /// </summary>
        /// <typeparam name="T">手持投射物类型，必须继承自 <see cref="BaseHeldProj"/></typeparam>
        /// <param name="_">模组实例，此方法未使用，仅为扩展方法兼容性保留</param>
        /// <returns>类型为 <typeparamref name="T"/> 的手持投射物实例，若未找到或实例类型不匹配则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若类型 <typeparamref name="T"/> 在 <see cref="GameLoaden.BaseHeldProj_Type_To_Instances"/> 中不存在，可能抛出此异常</exception>
        public static T FindHeldProj<T>(this Mod _) where T : BaseHeldProj => GameLoaden.BaseHeldProj_Type_To_Instances[typeof(T)] as T;
        /// <summary>
        /// 通过类型名称和关联模组查找粒子实例（<see cref="BasePRT"/>）
        /// 遍历类型到模组的映射，匹配模组和类型名称，然后通过 ID 映射解析实例
        /// </summary>
        /// <param name="mod">拥有该粒子类型的模组实例</param>
        /// <param name="name">粒子类型的名称（通常为 <see cref="BasePRT"/> 子类的类名）</param>
        /// <returns>匹配模组和名称的 <see cref="BasePRT"/> 实例，若未找到则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若类型或 ID 在 <see cref="PRTLoader.PRT_TypeToID"/> 或 <see cref="PRTLoader.PRT_IDToInstances"/> 中不存在，可能抛出此异常</exception>
        public static BasePRT FindPRT(this Mod mod, string name) {
            foreach (var typed in PRTLoader.PRT_TypeToMod) {
                if (typed.Value != mod) {
                    continue;
                }
                if (typed.Key.Name != name) {
                    continue;
                }
                return PRTLoader.PRT_IDToInstances[PRTLoader.PRT_TypeToID[typed.Key]];
            }
            return null;
        }
        /// <summary>
        /// 通过类型名称和关联模组查找 UI 句柄实例（<see cref="UIHandle"/>）
        /// 遍历类型到模组的映射，匹配模组和类型名称，然后通过 ID 映射解析实例
        /// </summary>
        /// <param name="mod">拥有该 UI 句柄类型的模组实例</param>
        /// <param name="name">UI 句柄类型的名称（通常为 <see cref="UIHandle"/> 子类的类名）</param>
        /// <returns>匹配模组和名称的 <see cref="UIHandle"/> 实例，若未找到则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若类型或 ID 在 <see cref="UIHandleLoader.UIHandle_Type_To_ID"/> 或 <see cref="UIHandleLoader.UIHandle_ID_To_Instance"/> 中不存在，可能抛出此异常</exception>
        public static UIHandle FindUI(this Mod mod, string name) {
            foreach (var typed in UIHandleLoader.UIHandle_Type_To_Mod) {
                if (typed.Value != mod) {
                    continue;
                }
                if (typed.Key.Name != name) {
                    continue;
                }
                return UIHandleLoader.UIHandle_ID_To_Instance[UIHandleLoader.UIHandle_Type_To_ID[typed.Key]];
            }
            return null;
        }
        /// <summary>
        /// 通过类型名称和关联模组查找瓦片处理器实例（<see cref="TileProcessor"/>）
        /// 遍历类型到模组的映射，匹配模组和类型名称，然后通过 ID 映射解析实例
        /// </summary>
        /// <param name="mod">拥有该瓦片处理器类型的模组实例</param>
        /// <param name="name">瓦片处理器类型的名称（通常为 <see cref="TileProcessor"/> 子类的类名）</param>
        /// <returns>匹配模组和名称的 <see cref="TileProcessor"/> 实例，若未找到则返回 <see langword="null"/></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">若类型或 ID 在 <see cref="TileProcessorLoader.TP_Type_To_ID"/> 或 <see cref="TileProcessorLoader.TP_ID_To_Instance"/> 中不存在，可能抛出此异常</exception>
        public static TileProcessor FindTP(this Mod mod, string name) {
            foreach (var typed in TileProcessorLoader.TP_Type_To_Mod) {
                if (typed.Value != mod) {
                    continue;
                }
                if (typed.Key.Name != name) {
                    continue;
                }
                return TileProcessorLoader.TP_ID_To_Instance[TileProcessorLoader.TP_Type_To_ID[typed.Key]];
            }
            return null;
        }
    }
}
