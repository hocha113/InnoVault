using InnoVault.UIHandles;
using System;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 叙事面板视图基类。用与 CWR ADV 相同的<b>线性帧计数</b>驱动进出场，
    /// 替代 <see cref="UIHandle.OpenProgress"/> 默认的指数 Lerp，避免过快或曲线叠加的怪异手感
    /// </summary>
    public abstract class NarrativePanelViewBase<TSelf> : UIHandle<TSelf>
        where TSelf : NarrativePanelViewBase<TSelf>
    {
        private float _showProgress;
        private float _hideProgress;
        private bool _panelClosing;

        /// <summary>打开动画时长（60FPS 帧数），对齐 CWR <c>ShowDuration</c></summary>
        protected virtual float ShowDurationFrames => 18f;

        /// <summary>关闭动画时长（60FPS 帧数），对齐 CWR <c>HideDuration</c></summary>
        protected virtual float HideDurationFrames => 14f;

        /// <summary>
        /// 当前面板视觉进度 [0, 1]。打开时为 <c>showProgress</c>，关闭时为 <c>1 - hideProgress</c>
        /// </summary>
        protected float MotionProgress => _panelClosing
            ? Math.Clamp(1f - _hideProgress, 0f, 1f)
            : _showProgress;

        /// <summary>面板是否处于关闭动画阶段</summary>
        protected bool IsPanelClosing => _panelClosing;

        /// <inheritdoc/>
        public override void Open() {
            _panelClosing = false;
            _hideProgress = 0f;
            base.Open();
        }

        /// <inheritdoc/>
        public override void Close() {
            if (!IsOpen) {
                return;
            }

            _panelClosing = true;
            _hideProgress = 0f;
            base.Close();
        }

        /// <inheritdoc/>
        protected internal override void BuiltinPreUpdate(float frames) {
            AdvanceLinearMotion(frames);
            base.BuiltinPreUpdate(frames);
            OpenProgress.Snap(MotionProgress);
        }

        private void AdvanceLinearMotion(float frames) {
            if (!_panelClosing && IsOpen && _showProgress < 1f) {
                _showProgress = Math.Min(1f, _showProgress + frames / ShowDurationFrames);
                return;
            }

            if (_panelClosing && _hideProgress < 1f) {
                _hideProgress = Math.Min(1f, _hideProgress + frames / HideDurationFrames);
                if (_hideProgress >= 1f) {
                    _panelClosing = false;
                    _showProgress = 0f;
                    _hideProgress = 0f;
                }
                return;
            }

            if (IsOpen && !_panelClosing) {
                _showProgress = 1f;
            }
        }
    }
}
