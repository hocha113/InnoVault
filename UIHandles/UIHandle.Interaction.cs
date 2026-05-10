using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using Terraria;
using Terraria.GameInput;

namespace InnoVault.UIHandles
{
    public abstract partial class UIHandle
    {
        #region 自动悬停判定

        /// <summary>
        /// 是否在每帧的<see cref="UIHandleLoader.UIHanderElementUpdate"/>调用<see cref="Update"/>之前，<br/>
        /// 由基类自动用<see cref="DrawPosition"/>+<see cref="Size"/>更新<see cref="UIHitBox"/>与<see cref="hoverInMainPage"/><br/>
        /// 默认<see langword="false"/>，需要子类显式开启以避免破坏现有手动维护逻辑
        /// </summary>
        public virtual bool AutoUpdateHitBox => false;

        /// <summary>
        /// 当<see cref="hoverInMainPage"/>为<see langword="true"/>时是否自动设置<see cref="Player.mouseInterface"/>=<see langword="true"/>，<br/>
        /// 默认<see langword="false"/>，需要子类显式开启以避免破坏现有手动维护逻辑
        /// </summary>
        public virtual bool BlockMouseWhenHovered => false;

        #endregion

        #region 拖拽

        /// <summary>
        /// 是否启用拖拽，默认<see langword="false"/><br/>
        /// 启用后基类会监听<see cref="DragMouseButton"/>的按下/释放，自动维护<see cref="IsDragging"/>并实时更新<see cref="DrawPosition"/><br/>
        /// 同一时刻只允许一个UI处于拖拽状态，由<see cref="UIHandleLoader.CurrentDragOwner"/>裁决
        /// </summary>
        public virtual bool CanDrag => false;

        /// <summary>
        /// 拖拽使用的鼠标按键，默认<see cref="MouseButtonType.Right"/><br/>
        /// 与游戏内大多数UI的"右键拖动面板"约定保持一致
        /// </summary>
        public virtual MouseButtonType DragMouseButton => MouseButtonType.Right;

        /// <summary>
        /// 限制可触发拖拽的矩形区域，默认<see langword="null"/>表示整个<see cref="UIHitBox"/>都可拖拽<br/>
        /// 子类可重写以仅在标题栏等指定区域内允许拖拽
        /// </summary>
        public virtual Rectangle? DragHandleRect => null;

        /// <summary>
        /// 当前是否正在被拖拽
        /// </summary>
        public bool IsDragging { get; private set; }

        private Vector2 _dragOffset;

        /// <summary>
        /// 拖拽开始时调用
        /// </summary>
        protected virtual void OnDragStart() { }

        /// <summary>
        /// 拖拽结束时调用
        /// </summary>
        protected virtual void OnDragEnd() { }

        #endregion

        #region 扩展输入便利属性（修饰键 / 滚轮 / 中键）

        /// <summary>
        /// 中键按键状态。当 <see cref="LayersMode"/> 为 <see cref="LayersModeEnum.None"/> 时此值不会被自动更新
        /// </summary>
        public KeyPressState keyMiddlePressState
            => IsLogicUpdate ? UIHandleLoader.logicKeyMiddlePressState : UIHandleLoader.keyMiddlePressState;

        /// <summary>
        /// 当前帧的鼠标滚轮增量（向上为正），等价于<see cref="PlayerInput.ScrollWheelDeltaForUI"/>
        /// </summary>
        public static int MouseScrollDelta => PlayerInput.ScrollWheelDeltaForUI;

        /// <summary>
        /// 当前帧 Shift 键是否按下（任意一边）
        /// </summary>
        public static bool ShiftHeld => Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);

        /// <summary>
        /// 当前帧 Ctrl 键是否按下（任意一边）
        /// </summary>
        public static bool CtrlHeld => Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);

        /// <summary>
        /// 当前帧 Alt 键是否按下（任意一边）
        /// </summary>
        public static bool AltHeld => Main.keyState.IsKeyDown(Keys.LeftAlt) || Main.keyState.IsKeyDown(Keys.RightAlt);

        /// <summary>
        /// 鼠标在屏幕上的整数坐标，等价于<c>MousePosition.ToPoint()</c>
        /// </summary>
        public Point MousePoint => MousePosition.ToPoint();

        /// <summary>
        /// 根据按键类型获取对应的当前帧按键状态
        /// </summary>
        public KeyPressState GetKeyState(MouseButtonType button) {
            return button switch {
                MouseButtonType.Left => keyLeftPressState,
                MouseButtonType.Right => keyRightPressState,
                MouseButtonType.Middle => keyMiddlePressState,
                _ => KeyPressState.None,
            };
        }

        #endregion

        #region 内置预/后更新（由 UIHandleLoader 调用）

        /// <summary>
        /// 每帧由<see cref="UIHandleLoader.UIHanderElementUpdate"/>在用户的<see cref="Update"/>之前调用<br/>
        /// 负责推进基类内置的悬停判定、拖拽、ESC关闭、<see cref="OpenProgress"/>动画和<see cref="GlobalTimer"/><br/>
        /// 该方法不打算被子类直接调用，但被声明为<see langword="protected"/>+<see langword="virtual"/>以便完全自定义场景下覆盖
        /// </summary>
        /// <param name="frames">本帧代表多少个"60FPS 帧"，用于驱动帧率无关的动画</param>
        protected internal virtual void BuiltinPreUpdate(float frames) {
            //自动维护 UIHitBox 与悬停判定
            if (AutoUpdateHitBox && Size != Vector2.Zero) {
                UIHitBox = DrawPosition.GetRectangle(Size);
                hoverInMainPage = UIHitBox.Intersects(MouseHitBox);
            }

            //处理拖拽
            if (CanDrag) {
                UpdateDrag();
            }

            //ESC 关闭
            if (CloseOnEscape && IsOpen && Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape)) {
                Close();
            }

            //悬停时阻挡鼠标交互
            if (BlockMouseWhenHovered && hoverInMainPage) {
                player.mouseInterface = true;
            }

            //推进打开进度（HoverProgress 在 BuiltinPostUpdate 中处理，
            //以便用户在 Update 中手动设置 hoverInMainPage 后能反映到当前帧）
            OpenProgress.Update(frames);
            GlobalTimer += frames * (1f / 60f);
        }

        /// <summary>
        /// 每帧由<see cref="UIHandleLoader.UIHanderElementUpdate"/>在用户的<see cref="Update"/>之后、<see cref="Draw"/>之前调用<br/>
        /// 负责推进依赖"用户 Update 后才确定"的状态量，目前仅有<see cref="HoverProgress"/>
        /// </summary>
        /// <param name="frames">本帧代表多少个"60FPS 帧"，用于驱动帧率无关的动画</param>
        protected internal virtual void BuiltinPostUpdate(float frames) {
            HoverProgress.TweenTo(hoverInMainPage ? 1f : 0f);
            HoverProgress.Update(frames);
        }

        /// <summary>
        /// 处理拖拽逻辑：在<see cref="hoverInMainPage"/>且<see cref="DragHandleRect"/>命中时进入拖拽，<br/>
        /// 实时跟随鼠标更新<see cref="DrawPosition"/>，按键释放时退出拖拽
        /// </summary>
        private void UpdateDrag() {
            KeyPressState state = GetKeyState(DragMouseButton);

            if (!IsDragging) {
                //已有其他UI在拖拽时，本UI禁止抢占
                if (UIHandleLoader.CurrentDragOwner != null && UIHandleLoader.CurrentDragOwner != this) {
                    return;
                }
                if (!hoverInMainPage || state != KeyPressState.Pressed) {
                    return;
                }
                Rectangle? handle = DragHandleRect;
                if (handle.HasValue && !handle.Value.Intersects(MouseHitBox)) {
                    return;
                }
                _dragOffset = DrawPosition - MousePosition;
                IsDragging = true;
                UIHandleLoader.CurrentDragOwner = this;
                try {
                    OnDragStart();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"{this} OnDragStart threw: {ex}");
                }
                return;
            }

            //拖拽进行中
            DrawPosition = MousePosition + _dragOffset;
            if (state == KeyPressState.Released || state == KeyPressState.None) {
                IsDragging = false;
                if (UIHandleLoader.CurrentDragOwner == this) {
                    UIHandleLoader.CurrentDragOwner = null;
                }
                try {
                    OnDragEnd();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"{this} OnDragEnd threw: {ex}");
                }
            }
        }

        #endregion
    }
}
