using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度数据复制接口，用于在主世界和维度之间传递数据
    /// </summary>
    public interface ICopyDimensionData : ILoadable
    {
        /// <summary>
        /// 在进入维度前调用，用于从主世界复制数据到维度
        /// <code>DimensionSystem.CopyWorldData(nameof(DownedSystem.downedBoss), DownedSystem.downedBoss);</code>
        /// </summary>
        void CopyMainWorldData() { }

        /// <summary>
        /// 在维度生成或加载后调用，用于读取从主世界复制的数据
        /// <code>DownedSystem.downedBoss = DimensionSystem.ReadCopiedWorldData&lt;bool&gt;(nameof(DownedSystem.downedBoss));</code>
        /// </summary>
        void ReadCopiedMainWorldData() { }
    }

    /// <summary>
    /// 维度基类，继承此类可创建自定义维度（子世界）
    /// <br/>维度是独立于主世界的空间，玩家可以自由进出
    /// </summary>
    public abstract class Dimension : VaultType<Dimension>, ICopyDimensionData, ILocalizedModType
    {
        #region 静态数据
        /// <summary>
        /// 所有已注册的维度实例列表
        /// </summary>
        public static List<Dimension> Dimensions { get; internal set; } = [];
        /// <summary>
        /// 维度ID计数器
        /// </summary>
        internal static int DimensionIDCount { get; set; } = 0;
        #endregion

        #region 本地化
        /// <summary>
        /// 本地化分类
        /// </summary>
        public string LocalizationCategory => "Dimensions";
        /// <summary>
        /// 维度的显示名称
        /// </summary>
        public virtual LocalizedText DisplayName => this.GetLocalization(nameof(DisplayName), PrettyPrintName);
        #endregion

        #region 核心属性
        /// <summary>
        /// 维度的文件名，用于存储维度世界文件
        /// </summary>
        public string FileName => Mod.Name + "_" + Name;

        /// <summary>
        /// 维度的宽度（物块数）
        /// </summary>
        public abstract int Width { get; }

        /// <summary>
        /// 维度的高度（物块数）
        /// </summary>
        public abstract int Height { get; }

        /// <summary>
        /// 维度的世界生成任务列表
        /// </summary>
        public abstract List<GenPass> Tasks { get; }

        /// <summary>
        /// 世界生成配置，默认为null
        /// </summary>
        public virtual WorldGenConfiguration Config => null;

        /// <summary>
        /// 玩家选择返回时要进入的维度索引
        /// <br/>-1 表示返回主世界
        /// <br/><see cref="int.MinValue"/> 表示返回主菜单
        /// <br/>默认值: -1
        /// </summary>
        public virtual int ReturnDestination => -1;

        /// <summary>
        /// 维度是否应该保存到文件
        /// <br/>默认值: false
        /// </summary>
        public virtual bool ShouldSave => false;

        /// <summary>
        /// 离开维度时是否恢复玩家状态
        /// <br/>默认值: false
        /// </summary>
        public virtual bool NoPlayerSaving => false;

        /// <summary>
        /// 是否使用原版的世界更新逻辑
        /// <br/>除非你在复制一个标准世界，否则不要启用
        /// <br/>默认值: false
        /// </summary>
        public virtual bool NormalUpdates => false;

        /// <summary>
        /// 如果 <see cref="ChangeAudio"/> 返回 true，是否完全禁用原版音频更新
        /// <br/>通常不需要启用，只有在你知道自己在做什么时才启用
        /// <br/>默认值: false
        /// </summary>
        public virtual bool ManualAudioUpdates => false;

        /// <summary>
        /// 维度的唯一ID，在加载时分配
        /// </summary>
        public int ID { get; internal set; } = -1;
        #endregion

        #region 生命周期钩子
        /// <summary>
        /// 进入维度时调用
        /// <br/>在此之前，返回按钮和地狱背景的可见性会被重置
        /// </summary>
        public virtual void OnEnter() { }

        /// <summary>
        /// 离开维度时调用
        /// <br/>在此之后，返回按钮和地狱背景的可见性会被重置
        /// </summary>
        public virtual void OnExit() { }

        /// <summary>
        /// 在 <see cref="ModSystem.PreUpdateWorld"/> 之后、<see cref="ModSystem.PostUpdateWorld"/> 之前调用
        /// <br/>用于在维度中执行自定义逻辑
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// 在所有维度的 <see cref="OnEnter"/> 之前调用
        /// <br/>用于从主世界复制数据到维度
        /// <code>DimensionSystem.CopyWorldData(nameof(DownedSystem.downedBoss), DownedSystem.downedBoss);</code>
        /// </summary>
        public virtual void CopyMainWorldData() { }

        /// <summary>
        /// 在 <see cref="OnExit"/> 之前调用
        /// <br/>用于从维度复制数据到其他世界
        /// <code>DimensionSystem.CopyWorldData(nameof(DownedSystem.downedBoss), DownedSystem.downedBoss);</code>
        /// </summary>
        public virtual void CopyDimensionData() { }

        /// <summary>
        /// 在所有维度生成或加载后调用
        /// <br/>用于读取从主世界复制的数据
        /// <code>DownedSystem.downedBoss = DimensionSystem.ReadCopiedWorldData&lt;bool&gt;(nameof(DownedSystem.downedBoss));</code>
        /// </summary>
        public virtual void ReadCopiedMainWorldData() { }

        /// <summary>
        /// 在离开维度时调用，在其他世界生成或加载后
        /// <br/>用于读取从维度复制的数据
        /// <code>DownedSystem.downedBoss = DimensionSystem.ReadCopiedWorldData&lt;bool&gt;(nameof(DownedSystem.downedBoss));</code>
        /// </summary>
        public virtual void ReadCopiedDimensionData() { }

        /// <summary>
        /// 维度生成或从文件加载后调用
        /// </summary>
        public virtual void OnLoad() { }

        /// <summary>
        /// 离开维度时调用，在其他世界生成或加载之前
        /// </summary>
        public virtual void OnUnload() { }
        #endregion

        #region 文件读写
        /// <summary>
        /// 读取维度世界文件
        /// <br/>需要了解原版世界文件加载机制才能正确重写
        /// </summary>
        /// <returns>退出状态，大于0表示读取失败</returns>
        public virtual int ReadFile(BinaryReader reader) {
            int status = WorldFile.LoadWorld_Version2(reader);
            Main.ActiveWorldFileData.Name = Main.worldName;
            Main.ActiveWorldFileData.Metadata = Main.WorldFileMetadata;
            return status;
        }

        /// <summary>
        /// 读取维度世界文件后的处理
        /// <br/>需要了解原版世界文件加载机制才能正确重写
        /// </summary>
        public virtual void PostReadFile() {
            DimensionLoader.PostLoadWorldFile();
        }
        #endregion

        #region 绘制
        /// <summary>
        /// 修正缩放并清屏，然后调用 DrawMenu 并绘制光标
        /// </summary>
        public virtual void DrawSetup(GameTime gameTime) {
            PlayerInput.SetZoom_UI();

            Main.instance.GraphicsDevice.Clear(Color.Black);

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend
                , SamplerState.LinearClamp, DepthStencilState.None, Main.Rasterizer, null, Main.UIScaleMatrix);

            DrawMenu(gameTime);
            Main.DrawCursor(Main.DrawThickCursor());

            Main.spriteBatch.End();
        }

        /// <summary>
        /// 由 DrawSetup 调用，用于绘制维度的加载菜单
        /// <br/>默认在黑色背景上显示状态文本
        /// </summary>
        public virtual void DrawMenu(GameTime gameTime) {
            Main.spriteBatch.DrawString(FontAssets.DeathText.Value, Main.statusText
                , new Vector2(Main.screenWidth, Main.screenHeight) / 2 - FontAssets.DeathText.Value.MeasureString(Main.statusText) / 2, Color.White);
        }
        #endregion

        #region 音频与物理
        /// <summary>
        /// 在选择音乐前调用，包括加载菜单
        /// <br/>返回 true 可禁用原版行为，允许修改 <see cref="Main.newMusic"/> 等变量
        /// <br/>默认值: false
        /// </summary>
        public virtual bool ChangeAudio() => false;

        /// <summary>
        /// 控制维度中实体的重力
        /// <br/>默认值: 1
        /// </summary>
        public virtual float GetGravity(Entity entity) => 1;

        /// <summary>
        /// 控制维度中物块的光照
        /// <br/>返回 true 可禁用原版行为
        /// <br/>默认值: false
        /// </summary>
        public virtual bool GetLight(Tile tile, int x, int y, ref FastRandom rand, ref Vector3 color) => false;
        #endregion

        #region VaultType 实现
        /// <inheritdoc/>
        protected override void VaultRegister() {
            ID = DimensionIDCount++;
            Dimensions.Add(this);
        }

        /// <inheritdoc/>
        public override void VaultSetup() {
            SetStaticDefaults();
        }

        /// <inheritdoc/>
        public override void Unload() {
            Dimensions.Clear();
            DimensionIDCount = 0;
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 获取该维度类型的索引
        /// </summary>
        public static int GetIndex<T>() where T : Dimension {
            for (int i = 0; i < Dimensions.Count; i++) {
                if (Dimensions[i].GetType() == typeof(T)) {
                    return i;
                }
            }
            return int.MinValue;
        }

        /// <summary>
        /// 通过完整名称获取维度索引
        /// <code>Dimension.GetIndex("MyMod/MyDimension")</code>
        /// </summary>
        public static int GetIndex(string fullName) {
            for (int i = 0; i < Dimensions.Count; i++) {
                if (Dimensions[i].FullName == fullName) {
                    return i;
                }
            }
            return int.MinValue;
        }

        /// <inheritdoc/>
        public override string ToString() => $"Dimension: {FullName} (ID: {ID})";
        #endregion
    }
}
