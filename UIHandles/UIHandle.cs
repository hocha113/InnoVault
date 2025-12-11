using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using static InnoVault.UIHandles.UIHandleLoader;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// UI处理器，一个简易的UI基类，继承它用于自定义各种UI实现
    /// <br>该API的使用介绍:<see href="https://github.com/hocha113/InnoVault/wiki/en-Basic-UI"/></br>
    /// </summary>
    public abstract class UIHandle : VaultType<UIHandle>
    {
        /// <summary>
        /// 一个纹理的占位，可以重写它用于获取UI的主要纹理
        /// </summary>
        public virtual Texture2D Texture => VaultAsset.placeholder3.Value;
        /// <summary>
        /// 获取玩家对象，等价于 <see cref="Main.LocalPlayer"/> ，因为运行UI代码的只有可能是当前端玩家，也就是本地玩家
        /// </summary>
        public static Player player => Main.LocalPlayer;
        /// <summary>
        /// 这个UI元素的内部ID
        /// </summary>
        public int ID => UIHandle_Type_To_ID[GetType()];
        /// <summary>
        /// 这个UI集成来自于什么模组
        /// </summary>
        public new Mod Mod => TypeToMod[GetType()];
        /// <summary>
        /// 这个UI的内部填充名
        /// </summary>
        public new string FullName => GetFullName(Mod.Name, Name);
        /// <summary>
        /// 这个UI是否活跃
        /// </summary>
        public virtual bool Active {
            get => false;
            set { }
        }
        /// <summary>
        /// 获取用户的鼠标在屏幕上的位置，这个属性一般在绘制函数以外的地方使用，
        /// 因为绘制函数中不需要屏幕因子的坐标矫正，直接使用 Main.MouseScreen 即可
        /// </summary>
        public virtual Vector2 MousePosition => Main.MouseScreen;
        /// <summary>
        /// 这个UI应该在什么模式下运行，默认为<see cref="LayersModeEnum.Vanilla_Mouse_Text"/>
        /// </summary>
        public virtual LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <summary>
        /// 默认值为1
        /// UI处理器的渲染优先级，在同一层级列表中，值越大，它的更新周期越靠后，进而绘制的效果越接近上层
        /// 这个属性对于排序效果仅在UI加载阶段执行一次，而非实时更改
        /// </summary>
        public virtual float RenderPriority => 1;
        /// <summary>
        /// 绘制的位置，这一般意味着UI矩形的左上角
        /// </summary>
        public Vector2 DrawPosition;
        /// <summary>
        /// UI矩形大小
        /// </summary>
        public Vector2 Size;
        /// <summary>
        /// UI的矩形
        /// </summary>
        public Rectangle UIHitBox;
        /// <summary>
        /// 屏幕鼠标碰撞箱
        /// </summary>
        public Rectangle MouseHitBox => MousePosition.GetRectangle(1);
        /// <summary>
        /// 左键按键状态
        /// </summary>
        public KeyPressState keyLeftPressState => IsLogicUpdate ? logicKeyLeftPressState : UIHandleLoader.keyLeftPressState;
        /// <summary>
        /// 右键按键状态
        /// </summary>
        public KeyPressState keyRightPressState => IsLogicUpdate ? logicKeyRightPressState : UIHandleLoader.keyRightPressState;
        /// <summary>
        /// 预留的判定主页悬浮的字段，相关的数据建议往这里存储，以确保可能的UI元素之间的交互通畅
        /// </summary>
        public bool hoverInMainPage;

        private bool oldDownL;
        private bool downL;
        private bool oldDownR;
        private bool downR;

        /// <summary>
        /// 当前更新周期是否为逻辑更新
        /// </summary>
        public static bool IsLogicUpdate { get; set; }

        /// <summary>
        /// 封闭内容
        /// </summary>
        protected override void VaultRegister() {
            int id = UIHandleLoader.UIHandles.Count;
            Type type = GetType();
            UIHandle_Type_To_ID.Add(type, id);
            UIHandle_Name_To_ID.Add(FullName, id);
            UIHandle_ID_To_Instance.Add(id, this);
            UIHandleLoader.UIHandles.Add(this);
            GetLayerModeHandlers(LayersMode).Add(this);
            GetLayerModeHandlers(LayersMode).Sort((x, y) => x.RenderPriority.CompareTo(y.RenderPriority));//按照升序排列
        }

        /// <summary>
        /// 加载内容
        /// </summary>
        public override void VaultSetup() {
            SetStaticDefaults();
        }

        /// <summary>
        /// 检查左键的按键状态变化，返回对应的按键状态枚举值<para/>
        /// 与<see cref="UIHandleLoader.CheckLeftKeyState"/>不同，<see cref="CheckLeftKeyState"/>用于单实例的自我检测<para/>
        /// 通常我们不会直接使用<see cref="CheckLeftKeyState"/>来获取按键点击状态，而是使用<see cref="keyLeftPressState"/><para/>
        /// 但是，当<see cref="LayersMode"/>为<see cref="LayersModeEnum.None"/>时，点击事件不会更新，<para/>
        /// 这时<see cref="keyLeftPressState"/>会失效在这种情况下，<see cref="CheckLeftKeyState"/>可以用于获取实时点击状态<para/>
        /// 请注意，这个方法只能在<see cref="Update"/>中调用一次，建议并将结果存储以供后续使用，以确保每个更新周期内只调用一次<para/>
        /// </summary>
        protected KeyPressState CheckLeftKeyState() {
            oldDownL = downL;
            downL = Main.LocalPlayer.PressKey(); // 检查左键是否按下
            if (downL && oldDownL) return KeyPressState.Held;
            if (downL && !oldDownL) return KeyPressState.Pressed;
            if (!downL && oldDownL) return KeyPressState.Released;
            return KeyPressState.None;
        }

        /// <summary>
        /// 检查右键的按键状态变化，返回对应的按键状态枚举值<para/>
        /// 与<see cref="UIHandleLoader.CheckRightKeyState"/>不同，<see cref="CheckRightKeyState"/>用于单实例的自我检测<para/>
        /// 通常我们不会直接使用<see cref="CheckRightKeyState"/>来获取按键点击状态，而是使用<see cref="keyRightPressState"/><para/>
        /// 但是，当<see cref="LayersMode"/>为<see cref="LayersModeEnum.None"/>时，点击事件不会更新，<para/>
        /// 这时<see cref="keyRightPressState"/>会失效在这种情况下，<see cref="CheckRightKeyState"/>可以用于获取实时点击状态<para/>
        /// 请注意：这个方法只能在<see cref="Update"/>中调用一次，建议并将结果存储以供后续使用，以确保每个更新周期内只调用一次<para/>
        /// </summary>
        protected KeyPressState CheckRightKeyState() {
            oldDownR = downR;
            downR = Main.LocalPlayer.PressKey(false); // 检查右键是否按下
            if (downR && oldDownR) return KeyPressState.Held;
            if (downR && !oldDownR) return KeyPressState.Pressed;
            if (!downR && oldDownR) return KeyPressState.Released;
            return KeyPressState.None;
        }

        /// <summary>
        /// 在游戏卸载时运行一次
        /// </summary>
        public virtual void UnLoad() { }

        /// <summary>
        /// 更新逻辑相关，该更新钩子运行在绘制逻辑中，调用在 <see cref="Draw"/> 之前
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// 更新逻辑相关，该更新钩子运行在游戏主循环的逻辑更新中，在主菜单中不会被调用，因为主菜单只有绘制线程更新可用
        /// </summary>
        public virtual void LogicUpdate() { }

        /// <summary>
        /// 玩家进入世界时调用一次该方法，可以用于一些UI的初始化操作
        /// </summary>
        public virtual void OnEnterWorld() { }

        /// <summary>
        /// 保存UI数据，UI数据将以<see cref="FullName"/>为键作为单例进行保存
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveUIData(TagCompound tag) { }

        /// <summary>
        /// 加载UI数据，UI数据将以<see cref="FullName"/>为键作为单例进行保存
        /// </summary>
        /// <param name="tag"></param>
        public virtual void LoadUIData(TagCompound tag) { }

        /// <summary>
        /// 更新绘制相关
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void Draw(SpriteBatch spriteBatch) { }
    }
}
