using System.Collections.Generic;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 样式注册表。对话框 / 选择框 / 弹窗皮肤统一以 <see cref="StyleId"/> 注册与查找，<br/>
    /// 新增主题只需注册新皮肤，无需修改框架枚举或核心 UI 类。<br/>
    /// 任意查找在缺省时回退到 <see cref="StyleId.Default"/> 的内置朴素皮肤，保证总能绘制
    /// </summary>
    public static class StyleRegistry
    {
        private static readonly Dictionary<StyleId, DialogueSkin> _dialogue = [];
        private static readonly Dictionary<StyleId, ChoiceSkin> _choice = [];
        private static readonly Dictionary<StyleId, PopupSkin> _popup = [];

        private static readonly DialogueSkin _defaultDialogue = new BasicDialogueSkin();
        private static readonly ChoiceSkin _defaultChoice = new BasicChoiceSkin();
        private static readonly PopupSkin _defaultPopup = new BasicPopupSkin();

        /// <summary>注册对话框皮肤</summary>
        public static void RegisterDialogue(StyleId id, DialogueSkin skin)
        {
            if (skin != null)
            {
                _dialogue[id] = skin;
            }
        }

        /// <summary>注册选择框皮肤</summary>
        public static void RegisterChoice(StyleId id, ChoiceSkin skin)
        {
            if (skin != null)
            {
                _choice[id] = skin;
            }
        }

        /// <summary>注册弹窗皮肤</summary>
        public static void RegisterPopup(StyleId id, PopupSkin skin)
        {
            if (skin != null)
            {
                _popup[id] = skin;
            }
        }

        /// <summary>一次注册同一 <see cref="StyleId"/> 下的整套皮肤</summary>
        public static void RegisterSet(StyleId id, DialogueSkin dialogue = null, ChoiceSkin choice = null, PopupSkin popup = null)
        {
            RegisterDialogue(id, dialogue);
            RegisterChoice(id, choice);
            RegisterPopup(id, popup);
        }

        /// <summary>获取对话框皮肤，缺省回退默认</summary>
        public static DialogueSkin GetDialogue(StyleId id)
            => _dialogue.TryGetValue(id, out var skin) ? skin : _defaultDialogue;

        /// <summary>获取选择框皮肤，缺省回退默认</summary>
        public static ChoiceSkin GetChoice(StyleId id)
            => _choice.TryGetValue(id, out var skin) ? skin : _defaultChoice;

        /// <summary>获取弹窗皮肤，缺省回退默认</summary>
        public static PopupSkin GetPopup(StyleId id)
            => _popup.TryGetValue(id, out var skin) ? skin : _defaultPopup;

        /// <summary>清空消费者注册的皮肤（卸载时调用），内置默认皮肤始终可用</summary>
        internal static void Clear()
        {
            _dialogue.Clear();
            _choice.Clear();
            _popup.Clear();
        }
    }
}
