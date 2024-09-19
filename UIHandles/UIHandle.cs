using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// UI处理器，一个简易的UI基类，继承它用于自定义各种UI实现
    /// </summary>
    public abstract class UIHandle
    {
        /// <summary>
        /// 一个纹理的占位，可以重写它用于获取UI的主要纹理
        /// </summary>
        public virtual Texture2D Texture => VaultAsset.placeholder3.Value;
        /// <summary>
        /// 获取玩家对象，一般为 LocalPlayer ，因为运行UI代码的只有可能是当前段玩家，也就是本地玩家
        /// </summary>
        public static Player player => Main.LocalPlayer;
        /// <summary>
        /// 这个UI是否活跃
        /// </summary>
        public virtual bool Active {
            get => false;
            set { }
        }
        /// <summary>
        /// 这个UI集成来自于什么模组
        /// </summary>
        public Mod Mod => UIHandleLoader.UIHander_Type_To_Mod[GetType()];
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
        /// 默认值为1。
        /// UI处理器的渲染优先级，在同一层级列表中，值越大，它的更新周期越靠后，进而绘制的效果越接近上层
        /// 这个属性对于排序效果仅在UI加载阶段执行一次，而非实时更改。
        /// </summary>
        public virtual float RenderPriority => 1;
        /// <summary>
        /// 绘制的位置，这一般意味着UI矩形的左上角
        /// </summary>
        public Vector2 DrawPosition;
        /// <summary>
        /// UI的矩形
        /// </summary>
        public Rectangle UIHitBox;
        /// <summary>
        /// 左键按键状态
        /// </summary>
        public KeyPressState keyLeftPressState => UIHandleLoader.keyLeftPressState;
        /// <summary>
        /// 右键按键状态
        /// </summary>
        public KeyPressState keyRightPressState => UIHandleLoader.keyRightPressState;

        private bool oldDownL;
        private bool downL;
        private bool oldDownR;
        private bool downR;

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
        /// 是否加载进游戏，默认为<see langword="true"/>
        /// </summary>
        /// <returns></returns>
        public virtual bool CanLoad() => true;

        /// <summary>
        /// 在游戏加载时运行一次
        /// </summary>
        public virtual void Load() { }

        /// <summary>
        /// 在游戏卸载时运行一次
        /// </summary>
        public virtual void UnLoad() { }

        /// <summary>
        /// 更新逻辑相关
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// 更新绘制相关
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void Draw(SpriteBatch spriteBatch) { }
    }
}
