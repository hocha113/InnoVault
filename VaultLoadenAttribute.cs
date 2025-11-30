using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria.Audio;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 标记静态字段、属性或类，以实现资源的自动化加载与管理
    /// <br/>
    /// 当应用于一个类时，此标签将作为其内部所有未被单独标记的静态成员的默认加载规则，并能递归作用于嵌套类
    /// <br/>
    /// 该API的使用介绍:<see href="https://github.com/hocha113/InnoVault/wiki/en-Basic-VaultLoaden"/>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>支持的成员类型</b>
    /// <br/>
    /// 系统会根据成员的声明类型自动推断应加载的资源类型（除非通过 <paramref name="assetMode"/> 显式指定）
    /// <list type="bullet">
    ///   <item><see cref="SoundStyle"/></item>
    ///   <item><see cref="Asset{T}"/> (T 为 <see cref="Texture2D"/> 或 <see cref="Effect"/>)</item>
    ///   <item><see cref="Texture2D"/> 或 <see cref="Effect"/> (注意：这会立即加载资源，可能影响启动性能)</item>
    ///   <item><see cref="ArmorShaderData"/> 或 <see cref="MiscShaderData"/></item>
    ///   <item>以上任意类型的数组 <c>T[]</c> 或列表 <c>IList{T}</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>路径规则</b>
    /// <br/>
    /// <paramref name="path"/> 参数非常灵活，并具备格式化关键词，善用这些规则可以极大提高资源管理的便捷性
    /// <list type="bullet">
    ///   <item><b>自动拼接</b>: 以 <c>"/"</c> 结尾的路径会自动追加成员自身的名称作为文件名<br/>
    ///     示例: <c>path: "Assets/Textures/"</c>，成员名为 <c>MyIcon</c> -> 最终路径 <c>Assets/Textures/MyIcon</c>
    ///   </item>
    ///   <item><b>命名空间占位符</b>: 路径中的 <c>{@namespace}</c> 会被替换为成员所在类的完整命名空间路径<br/>
    ///     示例: 成员在 <c>MyMod.Content.Items</c> 中 -> <c>{@namespace}</c> 变为 <c>MyMod/Content/Items</c>
    ///   </item>
    ///   <item><b>母类路径占位符</b>: 路径中的 <c>{@classPath}</c> 会被替换为成员所在类的完整标签路径<br/>
    ///     示例: 成员在 <c>MyMod.Content.Items</c> 的 MyWeapon 类 中 -> <c>{@classPath}</c> 变为 <c>MyMod/Content/Items/MyWeapon</c><br/>
    ///     如果 MyWeapon 类也标记了 <see cref="VaultLoadenAttribute"/>，<c>{@classPath}</c> 则会变为 <c>MyMod/Content/Items/MyWeapon.<see cref="Path"/></c>
    ///   </item>
    ///   <item><b>跨模组加载</b>: 使用 <c>"@模组名/"</c> 前缀可直接加载其他模组的资源<br/>
    ///     示例: <c>"@AnotherMod/Assets/Music/BossTheme"</c>
    ///   </item>
    ///   <item><b>路径拼接扩展 (<paramref name="pathConcatenation"/>)</b>: 仅用于类级标签，启用后会将成员名按下划线 <c>"_"</c> 拆分为子目录<br/>
    ///     示例: 类路径 <c>"Assets/UI/"</c>，成员名 <c>Button_Primary_Hover</c> -> 最终路径 <c>Assets/UI/Button/Primary/Hover</c>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>重要限制</b>
    /// <list type="bullet">
    ///   <item><b>属性写入</b>: 标签无法作用于只读属性（即没有 <c>setter</c> 的属性）</item>
    ///   <item><b>集合初始化</b>: 当加载集合（数组/列表）且未指定 <paramref name="arrayCount"/> 时，
    ///   该集合必须在使用标签前被初始化以确定长度 (例如 <c>new Asset&lt;Texture2D&gt;[5]</c>)</item>
    ///   <item><b>类级标签的 <paramref name="assetMode"/></b>: 在类级标签上，<paramref name="assetMode"/> 默认为 <c>None</c>，
    ///   会处理所有支持类型的成员，若显式指定一个类型，则只会处理该类中与之匹配的成员</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="path">资源加载的基础路径，支持多种占位符与规则，详见备注</param>
    /// <param name="assetMode">指定资源加载类型，默认为 <see cref="AssetMode.None"/> (自动推断)，在类级标签上可用于筛选成员</param>
    /// <param name="pathConcatenation">仅在类级标签上生效，为 <c>true</c> 时会根据成员名中的下划线拆分并扩展路径</param>
    /// <param name="startIndex">加载集合资源时，文件名数字后缀的起始索引，默认为0 (例如 `_0`, `_1`, `_2`...)</param>
    /// <param name="arrayCount">加载集合资源时指定要加载的数量，若为0，系统将尝试从已初始化的集合推断其长度</param>
    /// <param name="effectPassname">加载着色器 (<see cref="Effect"/>) 时指定的 Pass 名称，若留空则根据资源文件名自动生成</param>
    /// <param name="mod">指定资源所属模组，通常应留空让系统自动指定，跨模组加载请优先使用 <paramref name="path"/> 参数的 <c>"@OtherMod/"</c> 格式</param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
    public class VaultLoadenAttribute(string path, AssetMode assetMode, string effectPassname
        , int startIndex, int arrayCount, bool pathConcatenation, Mod mod) : Attribute
    {
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path)
            : this(path, AssetMode.None, "", 0, 0, false, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, AssetMode assetMode)
            : this(path, assetMode, "", 0, 0, false, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, AssetMode assetMode, string effectPassname)
            : this(path, assetMode, effectPassname, 0, 0, false, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, AssetMode assetMode, string effectPassname, int startIndex)
            : this(path, assetMode, effectPassname, startIndex, 0, false, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, AssetMode assetMode, string effectPassname, int startIndex, int arrayCount)
            : this(path, assetMode, effectPassname, startIndex, arrayCount, false, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, AssetMode assetMode, string effectPassname, int startIndex, int arrayCount, bool pathConcatenation)
            : this(path, assetMode, effectPassname, startIndex, arrayCount, pathConcatenation, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, int startIndex)
            : this(path, AssetMode.None, "", startIndex, 0, false, null) { }
        ///<inheritdoc/>
        public VaultLoadenAttribute(string path, int startIndex, int arrayCount)
            : this(path, AssetMode.None, "", startIndex, arrayCount, false, null) { }
        /// <summary>
        /// 这个字段或属性所属的模组程序集，自动指定
        /// </summary>
        public Mod Mod { get; set; } = mod;
        /// <summary>
        /// 这个字段或属性要加载的资源形式，默认为<see cref="AssetMode.None"/>，即自动指定
        /// </summary>
        public AssetMode AssetMode { get; set; } = assetMode;
        /// <summary>
        /// <br>资源路径，必填，用于指定需要加载的资源的文件路径</br>
        /// <br>路径值可以选择不包含模组名</br>
        /// </summary>
        public string Path { get; set; } = path;
        /// <summary>
        /// 用于<see cref="AssetMode.Effects"/>的加载，默认为空字符串，即自动指定为渲染文件名 + Pass
        /// </summary>
        public string EffectPassname { get; set; } = effectPassname;
        /// <summary>
        /// 元素文件起始索引标号，默认从0开始
        /// </summary>
        public int StartIndex { get; set; } = startIndex;
        /// <summary>
        /// 如果该标签用于收纳集合类型资源，该属性用作表示集合元素数量
        /// </summary>
        public int ArrayCount { get; set; } = arrayCount;
        /// <summary>
        /// 仅作用到类级别的标签之上，启用后会根据下划线拆分成员名来扩充路径进行自动加载
        /// </summary>
        public bool PathConcatenation { get; set; } = pathConcatenation;
    }
}
