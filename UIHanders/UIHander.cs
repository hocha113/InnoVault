using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace InnoVault.UIHanders
{
    /// <summary>
    /// UI处理器，一个简易的UI基类，继承它用于自定义各种UI实现
    /// </summary>
    public class UIHander
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
        /// 获取用户的鼠标在屏幕上的位置，这个属性一般在绘制函数以外的地方使用，
        /// 因为绘制函数中不需要屏幕因子的坐标矫正，直接使用 Main.MouseScreen 即可
        /// </summary>
        public virtual Vector2 MousePosition => Main.MouseScreen;
        /// <summary>
        /// 这个UI应该在什么模式下运行，默认为<see cref="LayersModeEnum.Vanilla_Mouse_Text"/>
        /// </summary>
        public virtual LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
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
        public KeyPressState keyLeftPressState => UILoader.keyLeftPressState;
        /// <summary>
        /// 右键按键状态
        /// </summary>
        public KeyPressState keyRightPressState => UILoader.keyRightPressState;

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
