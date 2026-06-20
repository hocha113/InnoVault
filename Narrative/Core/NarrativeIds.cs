namespace InnoVault.Narrative
{
    /// <summary>
    /// 角色标识符。用于把"角色身份"与"显示名 / 立绘"解耦，<br/>
    /// 取代旧式把说话者名字直接当立绘键、用空格后缀区分表情的脆弱写法<br/>
    /// 支持与 <see cref="string"/> 的隐式互转，因此内容作者可以直接写字符串字面量
    /// </summary>
    public readonly record struct CharacterId(string Value)
    {
        /// <summary>未指定角色</summary>
        public static readonly CharacterId None = new(string.Empty);
        /// <summary>是否为空标识</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        /// <summary>从字符串隐式构造</summary>
        public static implicit operator CharacterId(string value) => new(value ?? string.Empty);
        /// <summary>隐式转换为字符串</summary>
        public static implicit operator string(CharacterId id) => id.Value ?? string.Empty;
        /// <summary>构造带模组前缀的角色 id（推荐消费者使用）</summary>
        public static CharacterId ForMod(string modName, string name) => new($"{modName}/{name}");
        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>
    /// 表情标识符，配合 <see cref="CharacterId"/> 解析具体立绘，默认 <see cref="Default"/> 表示角色默认表情
    /// </summary>
    public readonly record struct ExpressionId(string Value)
    {
        /// <summary>默认表情</summary>
        public static readonly ExpressionId Default = new(string.Empty);
        /// <summary>是否为默认表情</summary>
        public bool IsDefault => string.IsNullOrEmpty(Value);
        /// <summary>从字符串隐式构造</summary>
        public static implicit operator ExpressionId(string value) => new(value ?? string.Empty);
        /// <summary>隐式转换为字符串</summary>
        public static implicit operator string(ExpressionId id) => id.Value ?? string.Empty;
        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>
    /// 皮肤样式标识符。对话框 / 选择框 / 功能弹窗共用一套基于 id 的样式注册，<br/>
    /// 避免使用枚举硬编码所有主题，新增主题无需修改框架核心
    /// </summary>
    public readonly record struct StyleId(string Value)
    {
        /// <summary>框架内置的朴素默认样式</summary>
        public static readonly StyleId Default = new("InnoVault/Default");
        /// <summary>是否为空标识</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        /// <summary>从字符串隐式构造</summary>
        public static implicit operator StyleId(string value) => new(string.IsNullOrEmpty(value) ? Default.Value : value);
        /// <summary>隐式转换为字符串</summary>
        public static implicit operator string(StyleId id) => id.Value ?? Default.Value;
        /// <summary>构造带模组前缀的样式 id（推荐消费者使用）</summary>
        public static StyleId ForMod(string modName, string name) => new($"{modName}/{name}");
        /// <inheritdoc/>
        public override string ToString() => Value ?? Default.Value;
    }

    /// <summary>
    /// 选项标识符，给每个选项一个稳定 id，便于存档分支、统计与回溯，<br/>
    /// 而不是依赖选项文本或在列表中的下标
    /// </summary>
    public readonly record struct ChoiceId(string Value)
    {
        /// <summary>是否为空标识</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        /// <summary>从字符串隐式构造</summary>
        public static implicit operator ChoiceId(string value) => new(value ?? string.Empty);
        /// <summary>隐式转换为字符串</summary>
        public static implicit operator string(ChoiceId id) => id.Value ?? string.Empty;
        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;
    }
}
