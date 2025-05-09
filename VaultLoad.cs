using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Linq;
using System.Reflection;
using Terraria.Audio;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 资源模式，将要加载什么样的资源
    /// </summary>
    public enum AssetMode
    {
        /// <summary>
        /// 无类型，目前用于自动指定
        /// </summary>
        None,
        /// <summary>
        /// 声音，即<see cref="SoundStyle"/>类型
        /// </summary>
        Sound,
        /// <summary>
        /// 纹理，即<see cref="Texture2D"/>类型
        /// </summary>
        Texture,
        /// <summary>
        /// 渲染类文件
        /// </summary>
        Effects,
        /// <summary>
        /// 加载<see cref="ArmorShaderData"/>类型的渲染类文件
        /// </summary>
        ArmorShader,
    }

    /// <summary>
    /// 指定一个用于自动资源加载的标签
    /// 可用于标记静态字段或属性，自动为其分配并管理资源对象
    /// </summary>
    /// <remarks>
    /// <para>资源类型将根据目标成员的声明类型自动推断</para>
    /// <para>只读属性无法被加载</para>
    /// <para>支持的资源类型包括：<see cref="SoundStyle"/>、<see cref="ArmorShaderData"/>、<see cref="Asset{T}"/>，其中 T 可为 <see cref="Texture2D"/> 或 <see cref="Effect"/></para>
    /// <para>如果指定的路径以 <c>"/"</c> 结尾，将自动在末尾追加字段名作为资源名</para>
    /// <para>路径中若包含 <c>{@namespace}</c>，将自动替换为字段所在的完整命名空间路径</para>
    /// </remarks>
    /// <param name="path">资源的加载路径可省略模组名支持路径占位符规则</param>
    /// <param name="assetMode">资源加载类型，默认为 <see cref="AssetMode.None"/>，即自动推断</param>
    /// <param name="effectPassname">
    /// 用于 <see cref="AssetMode.Effects"/> 模式时指定 Pass 名称留空则自动采用渲染文件名作为默认 Pass
    /// </param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class VaultLoadenAttribute(string path, AssetMode assetMode = AssetMode.None, string effectPassname = "") : Attribute
    {
        /// <summary>
        /// 这个字段或属性所属的模组程序集，自动指定
        /// </summary>
        public Mod Mod { get; set; }
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
    }

    /// <summary>
    /// 管理资源
    /// </summary>
    public static class VaultLoad
    {
        internal static void LoadAsset() {
            foreach (var t in VaultUtils.GetAnyModCodeType()) {
                ProcessTypeAssets(t, load: true);
            }
        }

        internal static void UnLoadAsset() {
            foreach (var t in VaultUtils.GetAnyModCodeType()) {
                ProcessTypeAssets(t, load: false);
            }
        }

        private static void ProcessTypeAssets(Type type, bool load) {
            //反射所有的静态字段，无论是否公开
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            //寻找并加载字段
            foreach (var field in type.GetFields(flags)) {
                ProcessMemberAsset(field, type, load);
            }
            //寻找并加载属性
            foreach (var property in type.GetProperties(flags)) {
                ProcessMemberAsset(property, type, load);
            }
        }

        private static void ProcessMemberAsset(MemberInfo member, Type type, bool load) {
            VaultLoadenAttribute attribute;
            try {
                attribute = member.GetCustomAttribute<VaultLoadenAttribute>();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"Skipped {member.MemberType.ToString().ToLower()} {member.Name} due to attribute load error: {ex.Message}");
                return;
            }

            if (attribute == null) {
                return;
            }

            if (!FindattributeByMod(type, attribute)) {
                return;//存在无法找到源模组的情况，在这种情况下需要返回
            }

            if (load) {
                CheckAttributePath(type, member.Name, attribute);
                LoadMember(member, attribute);
            }
            else {
                UnloadMember(member);
                attribute.Mod = null;
            }
        }

        private static void LoadMember(MemberInfo member, VaultLoadenAttribute attribute) {
            Type valueType = member is FieldInfo field ? field.FieldType : (member as PropertyInfo)?.PropertyType;
            if (valueType == null) {
                return;
            }
            //对于属性需要检测其是否可写
            if (member is PropertyInfo prop && (!prop.CanWrite || prop.GetSetMethod(true) == null)) {
                VaultMod.Instance.Logger.Error($"Property {member.Name} is marked with VaultLoadenAttribute but has no setter.");
                return;
            }

            if (attribute.Mod == null) {//一般来说到这里了不会出现这种情况，但多判断一下总没错
                VaultMod.Instance.Logger.Error($"{member.MemberType} {member.Name} from Mod is Null");
                return;
            }

            if (attribute.AssetMode == AssetMode.None) {//自动指定资源类型
                attribute.AssetMode = GetAttributeAssetMode(valueType);
            }
            //根据资源类型来加载值
            object value = attribute.AssetMode switch {
                AssetMode.Sound => new SoundStyle(attribute.Mod.Name + "/" + attribute.Path),
                AssetMode.Texture => attribute.Mod.Assets.Request<Texture2D>(attribute.Path),
                AssetMode.Effects => LoadEffect(attribute),
                AssetMode.ArmorShader => new ArmorShaderData(LoadEffect(attribute), attribute.EffectPassname),
                _ => null
            };

            if (member is FieldInfo fieldInfo) {
                fieldInfo.SetValue(null, value);
            }
            else if (member is PropertyInfo propInfo) {
                propInfo.SetValue(null, value);
            }
        }

        private static void UnloadMember(MemberInfo member) {
            if (member is FieldInfo field) {
                field.SetValue(null, null);
            }
            else if (member is PropertyInfo prop && prop.CanWrite && prop.GetSetMethod(true) != null) {
                prop.SetValue(null, null);
            }
        }

        private static bool FindattributeByMod(Type type, VaultLoadenAttribute attribute) {
            if (attribute.Mod != null) {
                return true; // 如果已经手动指定了模组对象就不需要进行查找了
            }

            attribute.Mod = VaultUtils.FindModByType(type, ModLoader.Mods);
            return attribute.Mod != null;
        }

        private static void CheckAttributePath(Type type, string targetName, VaultLoadenAttribute attribute) {
            if (attribute.Path.EndsWith('/')) {//如果以该符号结尾，补全成员的句柄名
                attribute.Path += targetName;
            }

            if (!string.IsNullOrEmpty(type.Namespace)) {//替换该特殊词柄为命名空间路径
                string namespacePath = type.Namespace.Replace('.', '/');
                attribute.Path = attribute.Path.Replace("{@namespace}", namespacePath);
            }

            string[] pathParts = attribute.Path.Split('/');//切割路径，检测是否以模组名字开头，如果包含模组名部分则切除
            if (pathParts.Length > 1 && pathParts[0] == attribute.Mod.Name) {
                attribute.Path = string.Join("/", pathParts.Skip(1));
            }
        }

        private static AssetMode GetAttributeAssetMode(Type type) {
            if (type == typeof(SoundStyle)) {
                return AssetMode.Sound;
            }
            else if (type == typeof(Asset<Texture2D>)) {
                return AssetMode.Texture;
            }
            else if (type == typeof(Asset<Effect>)) {
                return AssetMode.Effects;
            }
            else if (type == typeof(ArmorShaderData)) {
                return AssetMode.ArmorShader;
            }
            return AssetMode.None;
        }

        private static Asset<Effect> LoadEffect(VaultLoadenAttribute attribute) {
            Asset<Effect> asset = attribute.Mod.Assets.Request<Effect>(attribute.Path);
            string effectName = attribute.Path.Split('/')[^1];
            string effectKey = attribute.Mod.Name + ":" + effectName;

            if (string.IsNullOrEmpty(attribute.EffectPassname)) {
                attribute.EffectPassname = effectName + "Pass";
            }

            if (Filters.Scene[effectKey] == null) {
                Filters.Scene[effectKey] = new Filter(new ScreenShaderData(asset, attribute.EffectPassname), EffectPriority.VeryHigh);
            }

            return asset;
        }
    }
}
