using System;
using Terraria;
using Terraria.Audio;

namespace InnoVault.UIHandles
{
    public abstract partial class UIHandle
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
            }
            finally {
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
                ForceEndDragOnClose();

                InvokeLifecycleEvent(OnClosing, nameof(OnClosing));
                InvokeLifecycleHook(OnClose, nameof(OnClose));

                if (CloseSound.HasValue && !Main.dedServ) {
                    SoundEngine.PlaySound(CloseSound.Value);
                }

                InvokeLifecycleEvent(OnClosed, nameof(OnClosed));
            }
            finally {
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
        /// 由<see cref="Close"/>触发：如果当前正在被拖拽，则强制重置拖拽状态并触发<see cref="OnDragEnd"/>，<br/>
        /// 同时清理<see cref="UIHandleLoader.CurrentDragOwner"/>
        /// </summary>
        private void ForceEndDragOnClose() {
            if (!IsDragging) {
                return;
            }
            IsDragging = false;
            if (UIHandleLoader.CurrentDragOwner == this) {
                UIHandleLoader.CurrentDragOwner = null;
            }
            try {
                OnDragEnd();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"{this} OnDragEnd (forced by Close) threw: {ex}");
            }
        }

        #endregion
    }
}
