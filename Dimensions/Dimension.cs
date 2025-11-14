using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.GameContent;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度数据复制接口,用于在维度间传输数据
    /// </summary>
    public interface IDimensionDataTransfer : ILoadable
    {
        /// <summary>
        /// 从主世界复制数据到当前维度
        /// </summary>
        void CopyFromMainWorld() { }

        /// <summary>
        /// 读取从主世界复制的数据
        /// </summary>
        void ReadMainWorldData() { }

        /// <summary>
        /// 复制当前维度的数据以传输到其他维度
        /// </summary>
        void CopyDimensionData() { }

        /// <summary>
        /// 读取从其他维度传输的数据
        /// </summary>
        void ReadDimensionData() { }
    }

    /// <summary>
    /// 维度环境特性,定义维度的特殊环境效果
    /// </summary>
    public class DimensionEnvironment
    {
        /// <summary>
        /// 环境色调,影响整个维度的颜色滤镜
        /// </summary>
        public Color ColorTint { get; set; } = Color.White;

        /// <summary>
        /// 雾效强度(0-1)
        /// </summary>
        public float FogDensity { get; set; } = 0f;

        /// <summary>
        /// 雾效颜色
        /// </summary>
        public Color FogColor { get; set; } = Color.Gray;

        /// <summary>
        /// 环境粒子类型ID列表
        /// </summary>
        public List<int> AmbientParticles { get; set; } = new List<int>();

        /// <summary>
        /// 粒子生成频率(每帧生成概率)
        /// </summary>
        public float ParticleSpawnRate { get; set; } = 0.01f;

        /// <summary>
        /// 是否显示星空背景
        /// </summary>
        public bool ShowStars { get; set; } = true;

        /// <summary>
        /// 是否显示太阳/月亮
        /// </summary>
        public bool ShowCelestialBodies { get; set; } = true;
    }

    /// <summary>
    /// 维度层级,用于组织维度的层次结构
    /// </summary>
    public enum DimensionLayer
    {
        /// <summary>
        /// 主世界层
        /// </summary>
        MainWorld = 0,

        /// <summary>
        /// 平行维度层,与主世界同级但独立
        /// </summary>
        Parallel = 1,

        /// <summary>
        /// 子维度层,依附于某个父维度
        /// </summary>
        Sub = 2,

        /// <summary>
        /// 口袋维度层,小型独立空间
        /// </summary>
        Pocket = 3,

        /// <summary>
        /// 临时维度层,短期存在的维度
        /// </summary>
        Temporary = 4
    }

    /// <summary>
    /// 维度系统的抽象基类
    /// </summary>
    public abstract class Dimension : VaultType<Dimension>, IDimensionDataTransfer, ILocalizedModType
    {
        /// <summary>
        /// 本地化类别
        /// </summary>
        public string LocalizationCategory => "Dimensions";

        /// <summary>
        /// 维度显示名称
        /// </summary>
        public virtual LocalizedText DisplayName => this.GetLocalization(nameof(DisplayName), PrettyPrintName);

        /// <summary>
        /// 维度描述
        /// </summary>
        public virtual LocalizedText Description => this.GetLocalization(nameof(Description), () => "");

        /// <summary>
        /// 维度文件名
        /// </summary>
        public string FileName => Mod.Name + "_Dimension_" + Name;

        #region 基础属性

        /// <summary>
        /// 维度宽度,必须重写
        /// </summary>
        public abstract int Width { get; }

        /// <summary>
        /// 维度高度,必须重写
        /// </summary>
        public abstract int Height { get; }

        /// <summary>
        /// 维度的层级类型
        /// </summary>
        public virtual DimensionLayer Layer => DimensionLayer.Parallel;

        /// <summary>
        /// 父维度的索引,仅当Layer为Sub时有效
        /// </summary>
        public virtual int ParentDimensionIndex => -1;

        #endregion

        #region 世界生成

        /// <summary>
        /// 世界生成任务列表,必须重写
        /// </summary>
        public abstract List<GenPass> GenerationTasks { get; }

        /// <summary>
        /// 世界生成配置
        /// </summary>
        public virtual WorldGenConfiguration GenerationConfig => null;

        #endregion

        #region 维度行为

        /// <summary>
        /// 是否保存维度数据,默认为true
        /// </summary>
        public virtual bool ShouldSave => true;

        /// <summary>
        /// 是否在离开维度时重置玩家数据,默认为false
        /// </summary>
        public virtual bool ResetPlayerOnExit => false;

        /// <summary>
        /// 是否使用标准更新逻辑,默认为false
        /// </summary>
        public virtual bool UseStandardUpdates => false;

        /// <summary>
        /// 时间流速倍率,1.0为正常速度,可以为负数实现时间倒流
        /// </summary>
        public virtual float TimeScale => 1.0f;

        /// <summary>
        /// 是否允许时间循环(昼夜交替)
        /// </summary>
        public virtual bool EnableTimeOfDay => true;

        /// <summary>
        /// 是否允许天气系统
        /// </summary>
        public virtual bool EnableWeather => true;

        #endregion

        #region 返回目标

        /// <summary>
        /// 返回目标维度索引,默认返回主世界(-1)
        /// <para>-1: 返回主世界</para>
        /// <para>int.MinValue: 返回主菜单</para>
        /// <para>其他值: 对应维度的索引</para>
        /// </summary>
        public virtual int ReturnTarget => -1;

        /// <summary>
        /// 是否隐藏返回按钮,默认为false
        /// </summary>
        public virtual bool HideReturnButton => false;

        #endregion

        protected sealed override void VaultRegister() {
            DimensionSystem.registeredDimensions.Add(this);
        }

        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }

        #region 环境系统

        /// <summary>
        /// 维度环境特性
        /// </summary>
        public virtual DimensionEnvironment Environment => new DimensionEnvironment();

        /// <summary>
        /// 获取实体的重力倍率
        /// </summary>
        public virtual float GetGravityMultiplier(Entity entity) => 1.0f;

        /// <summary>
        /// 自定义光照计算
        /// <para>返回true表示使用自定义光照,color将被应用</para>
        /// </summary>
        public virtual bool CustomLighting(Tile tile, int x, int y, ref FastRandom rand, ref Vector3 color) => false;

        /// <summary>
        /// 是否隐藏地狱层背景,默认为false
        /// </summary>
        public virtual bool HideUnderworldBackground => false;

        #endregion

        #region 音频系统

        /// <summary>
        /// 是否自定义音频系统,返回true时可以手动控制音乐
        /// </summary>
        public virtual bool CustomAudioControl => false;

        /// <summary>
        /// 是否手动更新音频,需要CustomAudioControl返回true
        /// </summary>
        public virtual bool ManualAudioUpdate => false;

        /// <summary>
        /// 获取维度的背景音乐ID
        /// </summary>
        public virtual int GetMusicID() => -1;

        #endregion

        #region 生命周期钩子

        /// <summary>
        /// 进入维度时调用
        /// </summary>
        public virtual void OnEnter() { }

        /// <summary>
        /// 离开维度时调用
        /// </summary>
        public virtual void OnExit() { }

        /// <summary>
        /// 维度加载完成时调用
        /// </summary>
        public virtual void OnLoad() { }

        /// <summary>
        /// 维度卸载时调用
        /// </summary>
        public virtual void OnUnload() { }

        /// <summary>
        /// 每帧更新时调用
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// 后更新,在所有常规更新之后调用
        /// </summary>
        public virtual void PostUpdate() { }

        #endregion

        #region 数据传输

        /// <summary>
        /// 从主世界复制数据
        /// </summary>
        public virtual void CopyFromMainWorld() { }

        /// <summary>
        /// 读取主世界数据
        /// </summary>
        public virtual void ReadMainWorldData() { }

        /// <summary>
        /// 复制维度数据以供传输
        /// </summary>
        public virtual void CopyDimensionData() { }

        /// <summary>
        /// 读取传输的维度数据
        /// </summary>
        public virtual void ReadDimensionData() { }

        #endregion

        #region 文件操作

        /// <summary>
        /// 读取维度文件,高级功能,需要了解原版世界文件结构
        /// </summary>
        /// <returns>退出状态,大于0表示读取失败</returns>
        public virtual int ReadDimensionFile(BinaryReader reader) {
            int status = WorldFile.LoadWorld_Version2(reader);
            Main.ActiveWorldFileData.Name = Main.worldName;
            Main.ActiveWorldFileData.Metadata = Main.WorldFileMetadata;
            return status;
        }

        /// <summary>
        /// 文件读取后的处理,高级功能
        /// </summary>
        public virtual void PostReadFile() {
            //默认使用标准的世界文件后处理
        }

        #endregion

        #region UI绘制

        /// <summary>
        /// 绘制加载界面
        /// </summary>
        public virtual void DrawLoadingScreen(GameTime gameTime) {
            //默认绘制加载文本
            Vector2 textSize = FontAssets.DeathText.Value.MeasureString(Main.statusText);
            Vector2 position = new Vector2(Main.screenWidth, Main.screenHeight) / 2 - textSize / 2;

            Main.spriteBatch.DrawString(
                FontAssets.DeathText.Value,
                Main.statusText,
                position,
                Color.White
            );
        }

        /// <summary>
        /// 绘制自定义UI元素
        /// </summary>
        public virtual void DrawCustomUI(SpriteBatch spriteBatch) { }

        #endregion

        #region 玩家交互

        /// <summary>
        /// 玩家进入维度时调用
        /// </summary>
        public virtual void OnPlayerEnter(Player player) { }

        /// <summary>
        /// 玩家离开维度时调用
        /// </summary>
        public virtual void OnPlayerLeave(Player player) { }

        /// <summary>
        /// 玩家在维度中死亡时调用
        /// <para>返回true表示自定义死亡处理,false使用默认处理</para>
        /// </summary>
        public virtual bool OnPlayerDeath(Player player) => false;

        #endregion

        #region 实用方法

        /// <summary>
        /// 获取维度的完整路径
        /// </summary>
        public string GetDimensionPath(WorldFileData mainWorldData) {
            return Path.Combine(Main.WorldPath, mainWorldData.UniqueId.ToString(), FileName + ".wld");
        }

        /// <summary>
        /// 检查是否可以进入此维度
        /// </summary>
        public virtual bool CanEnter(Player player) => true;

        /// <summary>
        /// 检查是否可以离开此维度
        /// </summary>
        public virtual bool CanLeave(Player player) => true;

        /// <summary>
        /// 获取维度的生成进度百分比
        /// </summary>
        public virtual float GetGenerationProgress() {
            if (WorldGenerator.CurrentGenerationProgress == null)
                return 0f;

            return (float)WorldGenerator.CurrentGenerationProgress.Value;
        }

        #endregion

        #region 高级特性

        /// <summary>
        /// 维度特殊规则列表,用于定义维度的独特机制
        /// </summary>
        public virtual List<DimensionRule> SpecialRules => new List<DimensionRule>();

        /// <summary>
        /// 是否为临时维度,临时维度在所有玩家离开后会被销毁
        /// </summary>
        public virtual bool IsTemporary => Layer == DimensionLayer.Temporary;

        /// <summary>
        /// 维度最大同时在线玩家数,-1为无限制
        /// </summary>
        public virtual int MaxPlayers => -1;

        /// <summary>
        /// 维度的生存时间(秒),-1为永久,仅对临时维度有效
        /// </summary>
        public virtual float LifeTime => -1f;

        #endregion
    }

    /// <summary>
    /// 维度规则基类,用于定义维度的特殊规则
    /// </summary>
    public abstract class DimensionRule
    {
        /// <summary>
        /// 规则名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 规则是否激活
        /// </summary>
        public virtual bool IsActive => true;

        /// <summary>
        /// 应用规则
        /// </summary>
        public abstract void Apply();

        /// <summary>
        /// 移除规则效果
        /// </summary>
        public abstract void Remove();
    }
}
