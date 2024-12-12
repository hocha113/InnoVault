using InnoVault.InnoGens;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace InnoVault.AutoGens
{
    /// <summary>
    /// 自动的加入和处理世界生成任务
    /// </summary>
    public abstract class AutoGen
    {
        /// <summary>
        /// 任务名，默认为该类型的类型名
        /// </summary>
        public string GenName => GetType().Name;
        /// <summary>
        /// 插入的目标步骤名，默认为"Final Cleanup"
        /// </summary>
        public string IndexName => "Final Cleanup";
        /// <summary>
        /// 这个Gen来自于什么模组
        /// </summary>
        public Mod Mod => GenLoader.Gen_Type_To_Mod[GetType()];
        /// <summary>
        /// 具体是生成行为
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="configuration"></param>
        public virtual void Pass(GenerationProgress progress, GameConfiguration configuration) {

        }
    }
}
