using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using static InnoVault.UIHandles.UIHandleLoader;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// UI处理器，一个简易的UI基类，继承它用于自定义各种UI实现
    /// <br>该API的使用介绍:<see href="https://innovault.wiki/cn/content/ui-handle/"/></br>
    /// </summary>
    public abstract class UIHandle : VaultType<UIHandle>
    {
        #region 生命周期事件

        /// <summary>
        /// 当UI被请求打开时触发，<b>在<see cref="OnOpen"/>调用之前</b>。<br/>
        /// 用于外部订阅打开请求（例如统计、保存上次状态等）
        /// </summary>
        public event Action<UIHandle> OnOpening;
        /// <summary>
        /// 当UI被请求打开时触发，<b>在<see cref="OnOpen"/>与<see cref="OpenSound"/>之后</b>。<br/>
        /// 用于外部订阅"已经成功完成打开流程"的时机
        /// </summary>
        public event Action<UIHandle> OnOpened;
        /// <summary>
        /// 当UI被请求关闭时触发，<b>在<see cref="OnClose"/>调用之前</b>。<br/>
        /// 用于外部订阅关闭请求
        /// </summary>
        public event Action<UIHandle> OnClosing;
        /// <summary>
        /// 当UI被请求关闭时触发，<b>在<see cref="OnClose"/>与<see cref="CloseSound"/>之后</b>。<br/>
        /// 用于外部订阅"已经成功完成关闭流程"的时机
        /// </summary>
        public event Action<UIHandle> OnClosed;

        #endregion

        #region 开关状态

        private bool _isOpen;
        /// <summary>
        /// 阻止<see cref="Open"/>/<see cref="Close"/>在自己的事件 / 钩子中被递归触发，<br/>
        /// 例如<see cref="OnOpen"/>里直接<see cref="Close"/>会让 _isOpen 变成<see langword="false"/>而<see cref="OpenProgress"/>仍指向 1，状态错乱
        /// </summary>
        private bool _inLifecycleTransition;

        /// <summary>
        /// UI是否处于"已打开"状态<br/>
        /// 注意：刚关闭后<see cref="OpenProgress"/>仍可能大于0用于播放淡出动画，<br/>
        /// 此时<see cref="IsOpen"/>已经为<see langword="false"/>，请使用<see cref="IsClosing"/>判断该过渡
        /// </summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// UI是否处于打开过渡阶段（已请求打开，但<see cref="OpenProgress"/>尚未到达1）
        /// </summary>
        public bool IsOpening => _isOpen && OpenProgress.Current < 1f;

        /// <summary>
        /// UI是否处于关闭过渡阶段（已请求关闭，但<see cref="OpenProgress"/>尚未归零）
        /// </summary>
        public bool IsClosing => !_isOpen && OpenProgress.Current > 0f;

        #endregion

        #region 动画进度

        /// <summary>
        /// UI的整体打开进度 [0, 1]，由基类自动跟随<see cref="IsOpen"/>平滑过渡<br/>
        /// 在子类的<see cref="Draw"/>中可以直接将其作为透明度/缩放系数使用
        /// </summary>
        public AnimatedFloat OpenProgress = new AnimatedFloat(0f, AnimatedFloat.DefaultSpeed);

        /// <summary>
        /// 鼠标悬停的动画进度 [0, 1]，由基类根据<see cref="hoverInMainPage"/>自动跟随平滑过渡<br/>
        /// 适合用于按钮/槽位的悬停高亮淡入淡出
        /// </summary>
        public AnimatedFloat HoverProgress = new AnimatedFloat(0f, 0.25f);

        /// <summary>
        /// 全局时间累计（秒），便于子类计算呼吸/闪烁/脉动等周期性动画。<br/>
        /// 由<see cref="UIHandleLoader"/>按真实时间累加，与帧率无关；<br/>
        /// 仅在UI<see cref="Active"/>为<see langword="true"/>时累加
        /// </summary>
        public float GlobalTimer { get; protected set; }

        #endregion

        #region 可重写的钩子

        /// <summary>
        /// 是否在按下 ESC 键时自动调用<see cref="Close"/>，默认<see langword="false"/>
        /// </summary>
        public virtual bool CloseOnEscape => false;

        /// <summary>
        /// 打开UI时播放的音效，默认<see langword="null"/>表示不播放<br/>
        /// 仅在<see cref="Open"/>实际产生状态变化（false→true）时触发一次，时机位于<see cref="OnOpen"/>之后、<see cref="OnOpened"/>之前
        /// </summary>
        public virtual SoundStyle? OpenSound => null;

        /// <summary>
        /// 关闭UI时播放的音效，默认<see langword="null"/>表示不播放<br/>
        /// 仅在<see cref="Close"/>实际产生状态变化（true→false）时触发一次，时机位于<see cref="OnClose"/>之后、<see cref="OnClosed"/>之前
        /// </summary>
        public virtual SoundStyle? CloseSound => null;

        /// <summary>
        /// 当UI被打开时调用一次，子类可重写以执行自定义初始化逻辑（在<see cref="OnOpened"/>事件之前）
        /// </summary>
        protected virtual void OnOpen() { }

        /// <summary>
        /// 当UI被关闭时调用一次，子类可重写以执行自定义清理逻辑（在<see cref="OnClosed"/>事件之前）
        /// </summary>
        protected virtual void OnClose() { }

        #endregion

        #region 公开开关接口

        /// <summary>
        /// 打开UI。如果UI已经是打开状态则不会重复触发事件与音效；<br/>
        /// 在<see cref="OnOpening"/>/<see cref="OnOpen"/>/<see cref="OnOpened"/>内部递归调用<see cref="Open"/>或<see cref="Close"/>会被忽略以避免状态错乱<br/>
        /// 调用本方法等价于让<see cref="OpenProgress"/>开始向 1 过渡，并依次触发<see cref="OnOpening"/> → <see cref="OnOpen"/> → <see cref="OpenSound"/> → <see cref="OnOpened"/>
        /// </summary>
        public virtual void Open() {
            if (_isOpen || _inLifecycleTransition) {
                return;
            }
            _inLifecycleTransition = true;
            try {
                _isOpen = true;
                OpenProgress.TweenTo(1f);

                InvokeLifecycleEvent(OnOpening, nameof(OnOpening));
                InvokeLifecycleHook(OnOpen, nameof(OnOpen));

                if (OpenSound.HasValue && !Main.dedServ) {
                    SoundEngine.PlaySound(OpenSound.Value);
                }

                InvokeLifecycleEvent(OnOpened, nameof(OnOpened));
            } finally {
                _inLifecycleTransition = false;
            }
        }

        /// <summary>
        /// 关闭UI。如果UI已经是关闭状态则不会重复触发事件与音效；<br/>
        /// 在<see cref="OnClosing"/>/<see cref="OnClose"/>/<see cref="OnClosed"/>内部递归调用<see cref="Open"/>或<see cref="Close"/>会被忽略<br/>
        /// 调用本方法不会立即将<see cref="OpenProgress"/>清零，而是开始向 0 过渡，<br/>
        /// 期间<see cref="IsClosing"/>为<see langword="true"/>，绘制依然会被调用以播放淡出动画
        /// </summary>
        public virtual void Close() {
            if (!_isOpen || _inLifecycleTransition) {
                return;
            }
            _inLifecycleTransition = true;
            try {
                _isOpen = false;
                OpenProgress.TweenTo(0f);

                //关闭时强制结束拖拽，避免重新打开后出现"幽灵拖拽"
                ForceEndDrag(nameof(Close));

                InvokeLifecycleEvent(OnClosing, nameof(OnClosing));
                InvokeLifecycleHook(OnClose, nameof(OnClose));

                if (CloseSound.HasValue && !Main.dedServ) {
                    SoundEngine.PlaySound(CloseSound.Value);
                }

                InvokeLifecycleEvent(OnClosed, nameof(OnClosed));
            } finally {
                _inLifecycleTransition = false;
            }
        }

        /// <summary>
        /// 切换UI的开关状态。若当前为打开状态则<see cref="Close"/>，否则<see cref="Open"/>
        /// </summary>
        public void Toggle() {
            if (_isOpen) {
                Close();
            }
            else {
                Open();
            }
        }

        /// <summary>
        /// 立即将<see cref="OpenProgress"/>设为目标值，跳过过渡动画<br/>
        /// 通常用于场景切换或加载完成后避免一次"突然滑入"
        /// </summary>
        public void SnapOpenProgress() => OpenProgress.Snap(_isOpen ? 1f : 0f);

        #endregion

        #region 内部辅助

        /// <summary>
        /// 独立 try/catch 调用一次<see cref="Action{UIHandle}"/>事件，单个订阅者抛出不会影响后续订阅或主流程
        /// </summary>
        private void InvokeLifecycleEvent(Action<UIHandle> evt, string evtName) {
            if (evt == null) {
                return;
            }
            //逐个订阅者调用，避免一个订阅者抛出影响其他订阅
            foreach (Action<UIHandle> handler in evt.GetInvocationList()) {
                try {
                    handler(this);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"{this} {evtName} subscriber threw: {ex}");
                }
            }
        }

        /// <summary>
        /// 独立 try/catch 调用一次<see langword="protected"/>钩子（<see cref="OnOpen"/> / <see cref="OnClose"/>）
        /// </summary>
        private void InvokeLifecycleHook(Action hook, string hookName) {
            try {
                hook();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"{this} {hookName} threw: {ex}");
            }
        }

        /// <summary>
        /// 强制释放本UI持有的拖拽状态与全局拖拽锁。<br/>
        /// 如果当前处于基类拖拽状态，则会触发<see cref="OnDragEnd"/>
        /// </summary>
        internal void ForceEndDrag(string reason) {
            bool wasDragging = IsDragging;
            if (!wasDragging && CurrentDragOwner != this) {
                return;
            }

            IsDragging = false;
            if (CurrentDragOwner == this) {
                CurrentDragOwner = null;
            }

            if (!wasDragging) {
                return;
            }

            try {
                OnDragEnd();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"{this} OnDragEnd (forced by {reason}) threw: {ex}");
            }
        }

        #endregion

        #region 自动悬停判定

        /// <summary>
        /// 是否在每帧的<see cref="UIHanderElementUpdate"/>调用<see cref="Update"/>之前，<br/>
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
        /// 同一时刻只允许一个UI处于拖拽状态，由<see cref="CurrentDragOwner"/>裁决
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
        public static KeyPressState keyMiddlePressState
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
        /// 每帧由<see cref="UIHanderElementUpdate"/>在用户的<see cref="Update"/>之前调用<br/>
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
        /// 每帧由<see cref="UIHanderElementUpdate"/>在用户的<see cref="Update"/>之后、<see cref="Draw"/>之前调用<br/>
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
                if (CurrentDragOwner != null && CurrentDragOwner != this) {
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
                CurrentDragOwner = this;
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
                if (CurrentDragOwner == this) {
                    CurrentDragOwner = null;
                }
                try {
                    OnDragEnd();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"{this} OnDragEnd threw: {ex}");
                }
            }
        }

        #endregion

        #region Data
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
        /// 这个UI是否活跃<br/>
        /// 默认实现绑定到生命周期：当<see cref="IsOpen"/>为<see langword="true"/>，<br/>
        /// 或<see cref="OpenProgress"/>正在淡出过渡（<see cref="IsClosing"/>）时返回<see langword="true"/><br/>
        /// 子类如不使用<see cref="Open"/>/<see cref="Close"/>体系，可以照旧自行重写该属性
        /// </summary>
        public virtual bool Active {
            get => IsOpen || OpenProgress.Current > 0f;
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

        #endregion

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
            downL = Main.LocalPlayer.PressKey(); //检查左键是否按下
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
            downR = Main.LocalPlayer.PressKey(false); //检查右键是否按下
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
        /// 更新逻辑相关，该更新钩子运行在游戏主循环的逻辑更新中，在主菜单中不会被调用，因为主菜单只有绘制线程更新可用<br/>
        /// 如果需要在主菜单中获取稳定60tick的逻辑更新，请使用 <see cref="MenuLogicUpdate"/>
        /// </summary>
        public virtual void LogicUpdate() { }

        /// <summary>
        /// 主菜单逻辑更新，以固定60tick频率调用，与帧率无关<br/>
        /// 由于主菜单没有独立的逻辑线程，此方法通过时间累积器在绘制线程中驱动<br/>
        /// 仅在 <see cref="Main.gameMenu"/> 为 <see langword="true"/> 且 <see cref="LayersMode"/> 为 <see cref="LayersModeEnum.Mod_MenuLoad"/> 时才会被调用
        /// </summary>
        public virtual void MenuLogicUpdate() { }

        /// <summary>
        /// 玩家进入世界时调用一次该方法，可以用于一些UI的初始化操作
        /// </summary>
        public virtual void OnEnterWorld() { }

        /// <summary>
        /// 保存UI数据，UI数据将以<see cref="FullName"/>为键作为单例进行保存，<br/>
        /// 如果没有存入数据，<see cref="LoadUIData"/>就不会被调用
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveUIData(TagCompound tag) { }

        /// <summary>
        /// 加载UI数据，UI数据将以<see cref="FullName"/>为键作为单例进行保存，<br/>
        /// 如果 <see cref="SaveUIData"/> 没有存入数据，就不会调用该方法
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
