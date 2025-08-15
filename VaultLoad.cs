using InnoVault.GameSystem;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
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
        /// 加载<see cref="Asset{T}"/>纹理类文件
        /// </summary>
        Texture,
        /// <summary>
        /// 加载<see cref="Asset{T}"/>渲染类文件
        /// </summary>
        Effects,
        /// <summary>
        /// 加载<see cref="ArmorShaderData"/>类型的渲染类文件
        /// </summary>
        ArmorShader,
        /// <summary>
        /// 加载<see cref="MiscShaderData"/>类型的渲染类文件
        /// </summary>
        MiscShader,
        /// <summary>
        /// 加载<see cref="Texture2D"/>纹理类文件
        /// </summary>
        TextureValue,
        /// <summary>
        /// 加载<see cref="Effect"/>类型的渲染类文件
        /// </summary>
        EffectValue,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        SoundArray,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        TextureArray,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        EffectArray,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        ArmorShaderArray,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        MiscShaderArray,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        TextureValueArray,
        /// <summary>
        /// 加载数组批次处理
        /// </summary>
        EffectValueArray,
    }

    /// <summary>
    /// 标记静态字段、属性或类以自动加载和管理资源
    /// 类级别标记会自动处理所有符合条件的静态成员，但跳过已标记 `VaultLoadenAttribute` 的成员
    /// <br>该API的使用介绍:<see href="https://github.com/hocha113/InnoVault/wiki/en-Basic-VaultLoaden"/></br>
    /// </summary>
    /// <remarks>
    /// <para>支持的资源类型包括：
    /// <list type="bullet">
    ///   <item><see cref="SoundStyle"/></item>
    ///   <item><see cref="ArmorShaderData"/></item>
    ///   <item><see cref="MiscShaderData"/></item>
    ///   <item><see cref="Asset{T}"/>(其中 T 为 <see cref="Texture2D"/> or <see cref="Effect"/>)</item>
    ///   <item><see cref="Texture2D"/></item>
    ///   <item><see cref="Effect"/></item>
    ///   <item><see cref="IList{T}"/>(其中 T 为 <see cref="SoundStyle"/> or <see cref="Asset{T}"/> or <see cref="ArmorShaderData"/> 
    ///   or <see cref="MiscShaderData"/> or <see cref="Texture2D"/> or <see cref="Effect"/>)</item>
    /// </list>
    /// </para>
    /// <para>资源类型根据成员的声明类型自动推断（除非指定 <paramref name="assetMode"/>）</para>
    /// <para>路径规则：
    /// <list type="bullet">
    ///   <item>以 <c>"/"</c> 结尾的路径会自动追加成员名作为资源名</item>
    ///   <item>包含 <c>{@namespace}</c> 的路径会替换为成员的完整命名空间（以 <c>/</c> 分隔）</item>
    ///   <item>路径可省略模组名前缀，自动使用当前模组</item>
    ///   <item>@可以标记外部模组名，比如"@OtherMod/Asset/MyItem"</item>
    /// </list>
    /// </para>
    /// <para>限制：
    /// <list type="bullet">
    ///   <item>只读属性（无 setter）无法加载</item>
    ///   <item>类级别标记时，<paramref name="assetMode"/> 永远不会自动推断，保持 <see cref="AssetMode.None"/> 将加载所有支持类型的成员如果显式指定则只会加载对应资源类型的成员</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="path">资源加载路径（支持占位符，省略模组名前缀）</param>
    /// <param name="assetMode">资源加载类型，默认为 <see cref="AssetMode.None"/>（自动推断）类级别标记时，指定非 <see cref="AssetMode.None"/> 将只加载匹配类型的成员</param>
    /// <param name="effectPassname">用于 <see cref="AssetMode.Effects"/> 时指定的 Pass 名称，留空则使用资源文件名作为默认 Pass</param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
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

        internal int ArrayCount { get; set; }
    }

    /// <summary>
    /// 管理资源
    /// </summary>
    public static class VaultLoad
    {
        /// <summary>
        /// 在绝大部分内容加载完成后被设置为<see langword="true"/>
        /// </summary>
        public static bool LoadenContent { get; private set; } = false;
        /// <summary>
        /// 存储纹理实例
        /// </summary>
        private readonly static HashSet<Texture2D> TextureValues = [];
        /// <summary>
        /// 存储渲染器实例
        /// </summary>
        private readonly static HashSet<Effect> EffectValues = [];
        /// <summary>
        /// 一个非常靠后的加载钩子，此时本地化、配方修改、菜单排序等内容已经设置完成
        /// </summary>
        public static event Action EndLoadenEvent;

        internal static void LoadData() {
            try {//BossBarLoader的GotoSavedStyle是非常靠后的加载调用
                VaultHook.Add(typeof(BossBarLoader).GetMethod("GotoSavedStyle"
                , BindingFlags.NonPublic | BindingFlags.Static), EndLoaden);
            } catch {

            }
        }

        internal static void UnLoadData() {
            EndLoadenEvent = null;
            LoadenContent = false;
        }

        private static void EndLoaden(Action orig) {
            orig.Invoke();
            EndLoadenEvent?.Invoke();
            LoadenContent = true;
        }

        internal static void LoadAsset() {
            foreach (var t in VaultUtils.GetAnyModCodeType()) {
                ProcessClassAssets(t, load: true);
                ProcessTypeAssets(t, load: true);
            }
        }

        internal static void UnLoadAsset() {
            foreach (var t in VaultUtils.GetAnyModCodeType()) {
                ProcessClassAssets(t, load: false);
                ProcessTypeAssets(t, load: false);
            }

            foreach (var item in TextureValues) {
                if (item.IsDisposed) {
                    continue;
                }
                item.Dispose();
            }
            TextureValues.Clear();

            foreach (var item in EffectValues) {
                if (item.IsDisposed) {
                    continue;
                }
                item.Dispose();
            }
            EffectValues.Clear();
        }

        internal static void ProcessTypeAssets(Type type, bool load) {
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

        internal static void UnloadMember(MemberInfo member) {
            if (member is FieldInfo field) {
                field.SetValue(null, null);
            }
            else if (member is PropertyInfo prop && prop.CanWrite && prop.GetSetMethod(true) != null) {
                prop.SetValue(null, null);
            }
        }

        internal static bool FindattributeByMod(Type type, VaultLoadenAttribute attribute) {
            if (attribute.Mod != null) {
                return true; // 如果已经手动指定了模组对象就不需要进行查找了
            }

            attribute.Mod = VaultUtils.FindModByType(type, ModLoader.Mods);
            return attribute.Mod != null;
        }

        internal static void CheckAttributePath(Type type, string targetName, VaultLoadenAttribute attribute) {
            if (attribute.Path.EndsWith('/')) {//如果以该符号结尾，补全成员的句柄名
                attribute.Path += targetName;
            }

            if (!string.IsNullOrEmpty(type.Namespace)) {//替换该特殊词柄为命名空间路径
                string namespacePath = type.Namespace.Replace('.', '/');
                attribute.Path = attribute.Path.Replace("{@namespace}", namespacePath);
            }

            string[] pathParts = attribute.Path.Split('/');//切割路径，检测是否以模组名字开头，如果包含模组名部分则切除
            if (pathParts.Length == 0) {
                throw new Exception($"Attribute path on member \"{targetName}\" is empty or invalid: \"{attribute.Path}\"");
            }

            if (attribute.Path.StartsWith('@')) {//用@指定其他模组，重新设置源模组对象
                pathParts[0] = pathParts[0][1..]; // 去掉@
                if (ModLoader.TryGetMod(pathParts[0], out Mod newMod)) {
                    attribute.Mod = newMod;
                }
                else {
                    throw new Exception($"Member {targetName} couldn't find Mod \"{pathParts[0]}\". Original Mod Name: \"{attribute.Mod.Name}\"");
                }
            }

            if (pathParts[0] == attribute.Mod.Name) {//最后检测一下是否对齐源模组名称，如果对齐则进行剔除
                attribute.Path = string.Join("/", pathParts.Skip(1));
            }
        }

        internal static void ProcessMemberAsset(MemberInfo member, Type type, bool load) {
            VaultLoadenAttribute attribute = VaultUtils.GetAttributeSafely<VaultLoadenAttribute>(member, (phase, ex) => {
                VaultMod.Instance.Logger.Warn($"Skipped {member.MemberType.ToString().ToLower()} {member.Name} " +
                    $"Due To {phase} Load Error: {ex.Message}"
                );
            });

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

        internal static bool GetAttributeAssetArrayByIsAssignableFromMode(Type elementType, out AssetMode assetMode) {
            assetMode = AssetMode.None;
            if (typeof(SoundStyle).IsAssignableFrom(elementType))
                assetMode = AssetMode.SoundArray;
            if (typeof(Asset<Texture2D>).IsAssignableFrom(elementType))
                assetMode = AssetMode.TextureArray;
            if (typeof(Asset<Effect>).IsAssignableFrom(elementType))
                assetMode = AssetMode.EffectArray;
            if (typeof(ArmorShaderData).IsAssignableFrom(elementType))
                assetMode = AssetMode.ArmorShaderArray;
            if (typeof(MiscShaderData).IsAssignableFrom(elementType))
                assetMode = AssetMode.MiscShaderArray;
            if (typeof(Texture2D).IsAssignableFrom(elementType))
                assetMode = AssetMode.TextureValueArray;
            if (typeof(Effect).IsAssignableFrom(elementType))
                assetMode = AssetMode.EffectValueArray;
            return assetMode != AssetMode.None;
        }

        private static T LoadValue<T>(VaultLoadenAttribute attribute) {
            var type = typeof(T);
            if (type == typeof(SoundStyle))
                return (T)(object)new SoundStyle(attribute.Mod.Name + "/" + attribute.Path);
            if (type == typeof(Asset<Texture2D>))
                return (T)(object)attribute.Mod.Assets.Request<Texture2D>(attribute.Path);
            if (type == typeof(Asset<Effect>))
                return (T)(object)LoadEffect(attribute);
            if (type == typeof(ArmorShaderData))
                return (T)(object)new ArmorShaderData(LoadEffect(attribute), attribute.EffectPassname);
            if (type == typeof(MiscShaderData))
                return (T)(object)LoadMiscShader(attribute);
            if (type == typeof(Texture2D))
                return (T)(object)LoadTextureValue(attribute);
            if (type == typeof(Effect))
                return (T)(object)LoadEffectValue(attribute);

            throw new NotSupportedException($"不支持加载类型 {type}");
        }

        internal static AssetMode GetAttributeAssetMode(Type type) {
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
            else if (type == typeof(MiscShaderData)) {
                return AssetMode.MiscShader;
            }
            else if (type == typeof(Texture2D)) {
                return AssetMode.TextureValue;
            }
            else if (type == typeof(Effect)) {
                return AssetMode.EffectValue;
            }

            if (type.IsArray && type.GetElementType() != null) {
                var elementType = type.GetElementType();
                if (GetAttributeAssetArrayByIsAssignableFromMode(elementType, out var assetMode))
                    return assetMode;
            }
            else if (type.IsGenericType && typeof(IList<>).IsAssignableFrom(type.GetGenericTypeDefinition())) {
                var elementType = type.GetGenericArguments()[0];
                if (GetAttributeAssetArrayByIsAssignableFromMode(elementType, out var assetMode))
                    return assetMode;
            }
            else {
                var ilistInterface = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
                if (ilistInterface != null) {
                    var elementType = ilistInterface.GetGenericArguments()[0];
                    if (GetAttributeAssetArrayByIsAssignableFromMode(elementType, out var assetMode))
                        return assetMode;
                }
            }

            return AssetMode.None;
        }

        internal static IList<T> LoadAsset<T>(MemberInfo member, VaultLoadenAttribute attribute) {
            if (member is FieldInfo field) {
                if (typeof(IList<T>).IsAssignableFrom(field.FieldType)) {
                    var currentValue = field.GetValue(null);

                    int count = 0;
                    if (currentValue is IList<T> listVal) {
                        count = listVal.Count;
                    }
                    else if (currentValue is T[] arrVal) {
                        count = arrVal.Length;
                    }

                    attribute.ArrayCount = count;

                    var newList = new List<T>();
                    for (int i = 0; i < attribute.ArrayCount; i++) {
                        string origPath = attribute.Path;
                        attribute.Path = origPath + i;

                        T value = LoadValue<T>(attribute);//通用的资源加载方法
                        attribute.Path = origPath;
                        newList.Add(value);
                    }

                    return newList;
                }
            }
            if (member is PropertyInfo prop && prop.CanWrite && prop.GetSetMethod(true) != null) {
                if (typeof(IList<T>).IsAssignableFrom(prop.PropertyType) ||
                    (prop.PropertyType.IsArray && prop.PropertyType.GetElementType() == typeof(T))) {
                    var currentValue = prop.GetValue(null);

                    int count = 0;
                    if (currentValue is IList<T> listVal) {
                        count = listVal.Count;
                    }
                    else if (currentValue is T[] arrVal) {
                        count = arrVal.Length;
                    }

                    attribute.ArrayCount = count;

                    var newList = new List<T>();
                    for (int i = 0; i < attribute.ArrayCount; i++) {
                        string origPath = attribute.Path;
                        attribute.Path = origPath + i;

                        T value = LoadValue<T>(attribute);//通用的资源加载方法
                        attribute.Path = origPath;
                        newList.Add(value);
                    }

                    return newList;
                }
            }
            return null;
        }

        internal static void LoadMember(MemberInfo member, VaultLoadenAttribute attribute) {
            Type valueType = member is FieldInfo field ? field.FieldType : (member as PropertyInfo)?.PropertyType;
            if (valueType == null) {
                return;
            }

            if (member is PropertyInfo prop && (!prop.CanWrite || prop.GetSetMethod(true) == null)) {//对于属性需要检测其是否可写
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
            if (attribute.AssetMode == AssetMode.None) {//第二次检测，如果还是None就跳过
                VaultMod.Instance.Logger.Warn($"Cannot determine asset mode for {member.Name} of type {valueType}. Skipped.");
                return;
            }

            object value = attribute.AssetMode switch {//根据资源类型来加载值
                AssetMode.Sound => new SoundStyle(attribute.Mod.Name + "/" + attribute.Path),
                AssetMode.Texture => attribute.Mod.Assets.Request<Texture2D>(attribute.Path),
                AssetMode.Effects => LoadEffect(attribute),
                AssetMode.ArmorShader => new ArmorShaderData(LoadEffect(attribute), attribute.EffectPassname),
                AssetMode.MiscShader => LoadMiscShader(attribute),
                AssetMode.TextureValue => LoadTextureValue(attribute),
                AssetMode.EffectValue => LoadEffectValue(attribute),
                AssetMode.SoundArray => LoadAsset<SoundStyle>(member, attribute),
                AssetMode.TextureArray => LoadAsset<Asset<Texture2D>>(member, attribute),
                AssetMode.EffectArray => LoadAsset<Asset<Effect>>(member, attribute),
                AssetMode.ArmorShaderArray => LoadAsset<ArmorShaderData>(member, attribute),
                AssetMode.MiscShaderArray => LoadAsset<MiscShaderData>(member, attribute),
                AssetMode.TextureValueArray => LoadAsset<Texture2D>(member, attribute),
                AssetMode.EffectValueArray => LoadAsset<Effect>(member, attribute),
                _ => null
            };

            if (member is FieldInfo fieldInfo) {
                fieldInfo.SetValue(null, value);
            }
            else if (member is PropertyInfo propInfo) {
                propInfo.SetValue(null, value);
            }
        }

        internal static Asset<Effect> LoadEffect(VaultLoadenAttribute attribute, AssetRequestMode requestMode = AssetRequestMode.AsyncLoad) {
            Asset<Effect> asset = attribute.Mod.Assets.Request<Effect>(attribute.Path, requestMode);
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

        internal static MiscShaderData LoadMiscShader(VaultLoadenAttribute attribute) {
            MiscShaderData miscShader = new MiscShaderData(LoadEffect(attribute), attribute.EffectPassname);
            string effectName = attribute.Path.Split('/')[^1];
            string effectKey = attribute.Mod.Name + ":" + effectName;
            if (!GameShaders.Misc.TryGetValue(effectKey, out var value) || value == null) {
                GameShaders.Misc[effectKey] = miscShader;
            }
            return miscShader;
        }

        internal static Texture2D LoadTextureValue(VaultLoadenAttribute attribute) {
            Texture2D value = attribute.Mod.Assets.Request<Texture2D>(attribute.Path, AssetRequestMode.ImmediateLoad).Value;
            TextureValues.Add(value);
            return value;
        }

        internal static Effect LoadEffectValue(VaultLoadenAttribute attribute) {
            Effect effect = LoadEffect(attribute, AssetRequestMode.ImmediateLoad).Value;
            EffectValues.Add(effect);
            return effect;
        }

        /// <summary>
        /// 检查类级路径，确保路径正确并替换命名空间等占位符
        /// </summary>
        /// <param name="type">类类型</param>
        /// <param name="attribute">类级别的 VaultLoadenAttribute</param>
        private static void CheckClassAttributePath(Type type, VaultLoadenAttribute attribute) {
            //复用 VaultLoad 的路径检查逻辑，但不追加成员名
            if (!string.IsNullOrEmpty(type.Namespace)) {
                string namespacePath = type.Namespace.Replace('.', '/');
                attribute.Path = attribute.Path.Replace("{@namespace}", namespacePath);
            }

            string[] pathParts = attribute.Path.Split('/');
            if (pathParts.Length == 0) {
                throw new Exception($"Attribute path on class {type.FullName} is empty or invalid: \"{attribute.Path}\"");
            }

            if (attribute.Path.StartsWith('@')) {
                pathParts[0] = pathParts[0][1..]; // 去掉@
                if (ModLoader.TryGetMod(pathParts[0], out Mod newMod)) {
                    attribute.Mod = newMod;
                }
                else {
                    throw new Exception($"Class {type.FullName} couldn't find Mod \"{pathParts[0]}\". Original Mod Name: \"{attribute.Mod.Name}\"");
                }
            }

            if (pathParts[0] == attribute.Mod.Name) {
                attribute.Path = string.Join("/", pathParts.Skip(1));
            }
        }

        /// <summary>
        /// 处理类中的单个静态成员的资源加载或卸载
        /// </summary>
        /// <param name="member">要处理的成员</param>
        /// <param name="type">成员所在的类</param>
        /// <param name="classAttribute">类级别的<see cref="VaultLoadenAttribute"/></param>
        /// <param name="load">true 表示加载，false 表示卸载</param>
        private static void ProcessClassMemberAsset(MemberInfo member, Type type, VaultLoadenAttribute classAttribute, bool load) {
            VaultLoadenAttribute memberAttribute = VaultUtils.GetAttributeSafely<VaultLoadenAttribute>(member, (phase, ex) => {
                    VaultMod.Instance.Logger.Warn($"Skipped {member.MemberType.ToString().ToLower()} {member.Name} " +
                        $"in class {type.FullName} due to {phase} load error: {ex.Message}");
                }
            );

            if (memberAttribute != null) {
                //成员有自己的 VaultLoadenAttribute，直接跳过
                return;
            }

            //使用类级别的 VaultLoadenAttribute
            Type valueType = member is FieldInfo field ? field.FieldType : (member as PropertyInfo)?.PropertyType;
            if (valueType == null) {
                return;
            }

            //仅处理支持的资源类型
            AssetMode assetMode = GetAttributeAssetMode(valueType);
            if (assetMode == AssetMode.None) {
                //VaultMod.Instance.Logger.Warn($"Cannot determine asset mode for {member.Name} of type {valueType} in class {type.FullName}. Skipped.");
                return;
            }

            //使用成员名称构造资源路径
            string memberName = member.Name;
            string memberPath = classAttribute.Path.EndsWith('/') ? classAttribute.Path + memberName : $"{classAttribute.Path}/{memberName}";

            //创建临时的 VaultLoadenAttribute 用于加载
            var tempAttribute = new VaultLoadenAttribute(memberPath, assetMode, classAttribute.EffectPassname) {
                Mod = classAttribute.Mod
            };

            if (load) {
                //加载资源
                CheckAttributePath(type, memberName, tempAttribute);
                //VaultMod.Instance.Logger.Debug($"CheckAttributePath set TempAttribute resource path for {member.MemberType} : {tempAttribute.Path}");
                LoadMember(member, tempAttribute);
            }
            else {
                //卸载资源
                UnloadMember(member);
            }
        }

        private static void ProcessClassMemberPassInto(MemberInfo member, Type type, VaultLoadenAttribute classAttribute, bool load) {
            if (member is FieldInfo field && field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) {
                //自动实现的属性会生成一个隐形的字段，而GetFields会将这个字段也找出来，导致错误的加载现象
                //在针对成员自身的标签加载中可以因为标签识别而自动避开这个错误，但类级别标签的加载中却不行
                //因为隐藏字段都属于编译器自行生成，有CompilerGeneratedAttribute标签
                //所以在这里添加一个检测跳过这些隐藏字段
                return;
            }

            Type memberType = member is FieldInfo f ? f.FieldType : (member as PropertyInfo)?.PropertyType;
            if (memberType == null) {
                return;
            }

            //检查 AssetMode（如果类级别指定了 AssetMode）
            if (classAttribute.AssetMode != AssetMode.None) {
                VaultLoadenAttribute memberAttribute = VaultUtils.GetAttributeSafely<VaultLoadenAttribute>(member, (phase, ex) => {
                        VaultMod.Instance.Logger.Warn($"Skipped {member.MemberType.ToString().ToLower()} {member.Name} " +
                            $"in class {type.FullName} due to {phase} load error: {ex.Message}");
                    }
                );

                if (GetAttributeAssetMode(memberType) != classAttribute.AssetMode) {
                    //VaultMod.Instance.Logger.Debug($"Skipped {member.MemberType} {member.Name} in class {type.FullName} due to mismatched AssetMode");
                    return;
                }
            }

            //处理成员
            ProcessClassMemberAsset(member, type, classAttribute, load);
        }

        /// <summary>
        /// 处理单个类型的类级别资源加载或卸载
        /// </summary>
        /// <param name="type">要处理的类型</param>
        /// <param name="load">true 表示加载，false 表示卸载</param>
        private static void ProcessClassAssets(Type type, bool load) {
            // 检查类上是否有 VaultLoadenAttribute
            VaultLoadenAttribute classAttribute = VaultUtils.GetAttributeSafely<VaultLoadenAttribute>(type, (phase, ex) => {
                    VaultMod.Instance.Logger.Warn($"Skipped class {type.FullName} due to {phase} load error: {ex.Message}");
                }
            );

            if (classAttribute == null) {
                return; //类上没有 VaultLoadenAttribute，跳过
            }

            //查找模组
            if (!FindattributeByMod(type, classAttribute)) {
                VaultMod.Instance.Logger.Warn($"Cannot find mod for class {type.FullName}. Skipped.");
                return;
            }

            if (load) {
                //加载时，检查类级路径
                CheckClassAttributePath(type, classAttribute);
            }

            //获取所有静态字段和属性
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

            //处理字段
            foreach (var field in type.GetFields(flags)) {
                ProcessClassMemberPassInto(field, type, classAttribute, load);
            }

            //处理属性
            foreach (var property in type.GetProperties(flags)) {
                ProcessClassMemberPassInto(property, type, classAttribute, load);
            }
        }
    }
}
