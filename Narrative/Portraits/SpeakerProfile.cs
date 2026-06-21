using InnoVault.Narrative.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace InnoVault.Narrative.Portraits
{
    /// <summary>
    /// 角色档案：把"显示名"与"各表情立绘"集中登记一次，<br/>
    /// 内容脚本只引用 <see cref="CharacterId"/> + <see cref="ExpressionId"/>，<br/>
    /// 不再在每段对话里重复注册立绘，也不再用说话者名字的空格后缀区分表情<br/>
    /// 立绘以 <see cref="Func{Texture2D}"/> 形式延迟解析，从而与具体资源加载方式无关
    /// </summary>
    public sealed class SpeakerProfile
    {
        /// <summary>角色标识</summary>
        public CharacterId Id { get; }
        /// <summary>显示名解析器（通常接本地化文本），<see langword="null"/> 时回退为角色 id</summary>
        public Func<string> DisplayName { get; set; }
        /// <summary>是否以剪影方式绘制立绘</summary>
        public bool Silhouette { get; set; }
        /// <summary>默认表情立绘解析器</summary>
        public Func<Texture2D> DefaultPortrait { get; set; }
        /// <summary>默认表情立绘裁剪区域解析器；<see langword="null"/> 表示绘制整张纹理。</summary>
        public Func<Rectangle?> DefaultPortraitSource { get; set; }

        private readonly Dictionary<ExpressionId, Func<Texture2D>> _expressions = [];
        private readonly Dictionary<ExpressionId, Func<Rectangle?>> _expressionSources = [];

        /// <summary>创建一个角色档案</summary>
        public SpeakerProfile(CharacterId id) {
            Id = id;
        }

        /// <summary>设置显示名（固定字符串）</summary>
        public SpeakerProfile Name(string displayName) {
            DisplayName = () => displayName;
            return this;
        }

        /// <summary>设置显示名（解析器，便于接入本地化）</summary>
        public SpeakerProfile Name(Func<string> displayName) {
            DisplayName = displayName;
            return this;
        }

        /// <summary>设置默认表情立绘</summary>
        public SpeakerProfile Portrait(Func<Texture2D> portrait) {
            DefaultPortrait = portrait;
            return this;
        }

        /// <summary>设置默认表情立绘及裁剪区域</summary>
        public SpeakerProfile Portrait(Func<Texture2D> portrait, Rectangle? sourceRect) {
            DefaultPortrait = portrait;
            DefaultPortraitSource = () => sourceRect;
            return this;
        }

        /// <summary>设置默认表情立绘及延迟解析的裁剪区域</summary>
        public SpeakerProfile Portrait(Func<Texture2D> portrait, Func<Rectangle?> sourceRect) {
            DefaultPortrait = portrait;
            DefaultPortraitSource = sourceRect;
            return this;
        }

        /// <summary>登记一个具名表情立绘</summary>
        public SpeakerProfile Expression(ExpressionId expression, Func<Texture2D> portrait) {
            if (portrait != null) {
                _expressions[expression] = portrait;
            }
            return this;
        }

        /// <summary>登记一个具名表情立绘及裁剪区域</summary>
        public SpeakerProfile Expression(ExpressionId expression, Func<Texture2D> portrait, Rectangle? sourceRect) {
            if (portrait != null) {
                _expressions[expression] = portrait;
                _expressionSources[expression] = () => sourceRect;
            }
            return this;
        }

        /// <summary>登记一个具名表情立绘及延迟解析的裁剪区域</summary>
        public SpeakerProfile Expression(ExpressionId expression, Func<Texture2D> portrait, Func<Rectangle?> sourceRect) {
            if (portrait != null) {
                _expressions[expression] = portrait;
                _expressionSources[expression] = sourceRect;
            }
            return this;
        }

        /// <summary>设置是否剪影</summary>
        public SpeakerProfile AsSilhouette(bool silhouette = true) {
            Silhouette = silhouette;
            return this;
        }

        /// <summary>解析显示名；未配置 <see cref="DisplayName"/> 时回退到角色 id，配置后返回空/空白则隐藏名称</summary>
        public string ResolveName() {
            if (DisplayName == null) {
                return Id.Value;
            }

            string name = DisplayName.Invoke();
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        /// <summary>解析指定表情的立绘，缺省回退到默认表情立绘，仍无则返回 <see langword="null"/></summary>
        public Texture2D ResolvePortrait(ExpressionId expression) {
            if (!expression.IsDefault && _expressions.TryGetValue(expression, out var resolver)) {
                return resolver?.Invoke();
            }
            return DefaultPortrait?.Invoke();
        }

        /// <summary>解析指定表情的裁剪区域，缺省回退默认表情裁剪区域。</summary>
        public Rectangle? ResolvePortraitSource(ExpressionId expression) {
            if (!expression.IsDefault && _expressionSources.TryGetValue(expression, out var resolver)) {
                return resolver?.Invoke();
            }
            return DefaultPortraitSource?.Invoke();
        }
    }
}
