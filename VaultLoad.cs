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
    /// 管理资源
    /// </summary>
    public static class VaultLoad
    {
        /// <summary>
        /// 在绝大部分内容加载完成后被设置为<see langword="true"/>
        /// </summary>
        public static bool LoadenContent { get; private set; } = false;
        /// <summary>
        /// 存储处理过的类型，在加载完成后会立刻清理释放
        /// </summary>
        private readonly static HashSet<Type> ProcessedTypes = [];
        /// <summary>
        /// 一个非常靠后的加载钩子，此时本地化、配方修改、菜单排序等内容已经设置完成
        /// </summary>
        public static event Action EndLoadenEvent;
        /// <summary>
        /// 将对应的资源枚举类型对应到实际的编译码类型
        /// </summary>
        public readonly static Dictionary<AssetMode, Type> AssetModeToTypeMap = new() {
            { AssetMode.None, null },
            { AssetMode.Sound, typeof(SoundStyle) },
            { AssetMode.Texture, typeof(Asset<Texture2D>) },
            { AssetMode.Effects, typeof(Asset<Effect>) },
            { AssetMode.ArmorShader, typeof(ArmorShaderData) },
            { AssetMode.MiscShader, typeof(MiscShaderData) },
            { AssetMode.TextureValue, typeof(Texture2D) },
            { AssetMode.EffectValue, typeof(Effect) },
            { AssetMode.SoundArray, typeof(IList<SoundStyle>) },
            { AssetMode.TextureArray, typeof(IList<Asset<Texture2D>>) },
            { AssetMode.EffectArray, typeof(IList<Asset<Effect>>) },
            { AssetMode.ArmorShaderArray, typeof(IList<ArmorShaderData>) },
            { AssetMode.MiscShaderArray, typeof(IList<MiscShaderData>) },
            { AssetMode.TextureValueArray, typeof(IList<Texture2D>) },
            { AssetMode.EffectValueArray, typeof(IList<Effect>) },
        };
        /// <summary>
        /// 将实际的编译时类型映射到资源的枚举类型
        /// </summary>
        public readonly static Dictionary<Type, AssetMode> TypeToAssetModeMap = new() {
            { typeof(SoundStyle), AssetMode.Sound },
            { typeof(Asset<Texture2D>), AssetMode.Texture },
            { typeof(Asset<Effect>), AssetMode.Effects },
            { typeof(ArmorShaderData), AssetMode.ArmorShader },
            { typeof(MiscShaderData), AssetMode.MiscShader },
            { typeof(Texture2D), AssetMode.TextureValue },
            { typeof(Effect), AssetMode.EffectValue },
            { typeof(IList<SoundStyle>), AssetMode.SoundArray },
            { typeof(IList<Asset<Texture2D>>), AssetMode.TextureArray },
            { typeof(IList<Asset<Effect>>), AssetMode.EffectArray },
            { typeof(IList<ArmorShaderData>), AssetMode.ArmorShaderArray },
            { typeof(IList<MiscShaderData>), AssetMode.MiscShaderArray },
            { typeof(IList<Texture2D>), AssetMode.TextureValueArray },
            { typeof(IList<Effect>), AssetMode.EffectValueArray },
        };
        internal static void LoadData() {
            try {//BossBarLoader的GotoSavedStyle是非常靠后的加载调用
                VaultHook.Add(typeof(BossBarLoader).GetMethod("GotoSavedStyle"
                , BindingFlags.NonPublic | BindingFlags.Static), EndLoaden);
            } catch {
                VaultMod.Instance.Logger.Error(
                    "Failed to hook BossBarLoader.GotoSavedStyle for VaultLoadenEvent. " +
                    "This may cause some resources to not load correctly.");
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
            ProcessedTypes.Clear();
            foreach (var t in VaultUtils.GetAnyModCodeType()) {
                ProcessClassAssets(t, load: true);
                ProcessTypeAssets(t, load: true);
            }
            ProcessedTypes.Clear();
        }

        internal static void UnLoadAsset() {
            ProcessedTypes.Clear();
            foreach (var t in VaultUtils.GetAnyModCodeType()) {
                ProcessClassAssets(t, load: false);
                ProcessTypeAssets(t, load: false);
            }
            ProcessedTypes.Clear();
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

            if (attribute.Path.Contains("{@classPath}")) {//替换为母类路径，类级别标签加载在字段标签之前，所以这里不用担心类标签数据还没有加载完成
                VaultLoadenAttribute momClassAttribute = VaultUtils.GetAttributeSafely<VaultLoadenAttribute>(type, (phase, ex) => {
                    VaultMod.Instance.Logger.Warn($"Failed to resolve '{{@classPath}}' for member '{targetName}'" +
                        $": Could not read [VaultLoaden] attribute from containing class '{type.FullName}', by {phase}. " +
                        $"Path will fall back to default (namespace + class name). Reason: {ex.Message}");
                }
                );
                string classPath;
                if (momClassAttribute != null) {
                    classPath = momClassAttribute.Path;
                }
                else {
                    classPath = type.Namespace.Replace('.', '/') + type.Name;
                }
                attribute.Path = attribute.Path.Replace("{@classPath}", classPath);
            }

            string[] pathParts = attribute.Path.Split('/');//切割路径，检测是否以模组名字开头，如果包含模组名部分则切除
            if (pathParts.Length == 0) {
                throw new Exception($"Attribute path on member \"{targetName}\" is empty or invalid: \"{attribute.Path}\"");
            }

            if (attribute.Path.StartsWith('@')) {//用@指定其他模组，重新设置源模组对象
                pathParts[0] = pathParts[0][1..]; //去掉@
                if (ModLoader.TryGetMod(pathParts[0], out Mod newMod)) {
                    attribute.Mod = newMod;
                }
                else {
                    string modName = attribute.Mod != null ? attribute.Mod.Name : pathParts[0];
                    //改为记录调试日志而非抛出异常，支持弱联动
                    VaultMod.Instance.Logger.Debug($"Member {targetName} couldn't find Mod \"{pathParts[0]}\". " +
                        $"Original Mod Name: \"{modName}\". " +
                        $"Resource will use default value instead.");
                    //将资源对象设置为null，后续会使用默认值
                    attribute.Mod = null;
                    return;
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
            foreach (Type targetElement in TypeToAssetModeMap.Keys) {
                if (targetElement.IsAssignableFrom(elementType)) {
                    assetMode = TypeToAssetModeMap[targetElement];
                    break;
                }
            }
            return assetMode != AssetMode.None;
        }

        internal static AssetMode GetAttributeAssetMode(Type type) {
            if (TypeToAssetModeMap.TryGetValue(type, out AssetMode assetMode)) {
                return assetMode;
            }

            if (type.IsArray && type.GetElementType() != null) {
                var elementType = type.GetElementType();
                if (GetAttributeAssetArrayByIsAssignableFromMode(elementType, out assetMode)) {
                    return assetMode;
                }

            }
            else if (type.IsGenericType && typeof(IList<>).IsAssignableFrom(type.GetGenericTypeDefinition())) {
                var elementType = type.GetGenericArguments()[0];
                if (GetAttributeAssetArrayByIsAssignableFromMode(elementType, out assetMode)) {
                    return assetMode;
                }
            }
            else {
                var ilistInterface = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
                if (ilistInterface != null) {
                    var elementType = ilistInterface.GetGenericArguments()[0];
                    if (GetAttributeAssetArrayByIsAssignableFromMode(elementType, out assetMode)) {
                        return assetMode;
                    }
                }
            }

            return AssetMode.None;
        }

        private static object LoadValue(MemberInfo member, VaultLoadenAttribute attribute) {
            return attribute.AssetMode switch {//根据资源类型来加载值
                AssetMode.Sound => new SoundStyle(attribute.Mod.Name + "/" + attribute.Path),
                AssetMode.Texture => LoadTexture(attribute),
                AssetMode.Effects => LoadEffect(attribute),
                AssetMode.ArmorShader => new ArmorShaderData(LoadEffect(attribute), attribute.EffectPassname),
                AssetMode.MiscShader => LoadMiscShader(attribute),
                AssetMode.TextureValue => LoadTextureValue(attribute),
                AssetMode.EffectValue => LoadEffectValue(attribute),
                AssetMode.SoundArray => LoadArrayAsset<SoundStyle>(member, attribute),
                AssetMode.TextureArray => LoadArrayAsset<Asset<Texture2D>>(member, attribute),
                AssetMode.EffectArray => LoadArrayAsset<Asset<Effect>>(member, attribute),
                AssetMode.ArmorShaderArray => LoadArrayAsset<ArmorShaderData>(member, attribute),
                AssetMode.MiscShaderArray => LoadArrayAsset<MiscShaderData>(member, attribute),
                AssetMode.TextureValueArray => LoadArrayAsset<Texture2D>(member, attribute),
                AssetMode.EffectValueArray => LoadArrayAsset<Effect>(member, attribute),
                _ => null,
            };
        }

        private static T LoadValue<T>(MemberInfo member, VaultLoadenAttribute attribute) => (T)LoadValue(member, attribute);

        internal static IList<T> LoadArrayAsset<T>(MemberInfo member, VaultLoadenAttribute attribute) {
            //取出当前集合的值
            object currentValue = null;
            bool isValue = false;

            if (member is FieldInfo field) {
                isValue = true;
                currentValue = field.GetValue(null);
            }

            if (member is PropertyInfo prop && prop.CanWrite && prop.GetSetMethod(true) != null) {
                isValue = true;
                currentValue = prop.GetValue(null);
            }

            if (!isValue) {
                return null;
            }

            int count = attribute.ArrayCount;
            string origPath = attribute.Path;

            if (count == 0) {
                //首先尝试从已有集合推断长度
                if (currentValue != null) {
                    count = currentValue switch {
                        T[] arrVal => arrVal.Length,
                        IList<T> listVal => listVal.Count,
                        _ => 0
                    };
                }

                //如果仍然为0则尝试自动探测资源文件数量
                if (count == 0 && attribute.Mod != null) {
                    count = ProbeAssetCount(attribute.Mod, origPath, attribute.StartIndex);
                    if (count > 0) {
                        VaultMod.Instance.Logger.Debug($"Auto-probed {count} assets for {member.Name} at path: {attribute.Mod.Name}/{origPath}");
                    }
                }

                //如果仍然无法确定数量，返回空列表而非null
                if (count == 0) {
                    VaultMod.Instance.Logger.Debug($"No assets found for {member.Name} at path: {attribute.Mod?.Name}/{origPath}, returning empty list.");
                    return new List<T>();
                }

                attribute.ArrayCount = count;
            }

            //按数量逐个加载
            var newList = new List<T>(count);
            AssetMode origAssetMode = attribute.AssetMode;
            if (TypeToAssetModeMap.TryGetValue(typeof(T), out var assetMode)) {
                attribute.AssetMode = assetMode;//进行集合类别的元素降级，防止无限迭代
            }
            for (int i = attribute.StartIndex; i < attribute.StartIndex + count; i++) {
                attribute.Path = origPath + i;
                newList.Add(LoadValue<T>(member, attribute));//这里如果处理不当会触发死循环，前面的orig参数用于避免这种情况
            }
            //恢复
            attribute.Path = origPath;
            attribute.AssetMode = origAssetMode;

            return newList;
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

            if (attribute.Mod == null) {//如果模组对象为null（例如外部模组未启用），使用默认值
                VaultMod.Instance.Logger.Warn($"{member.MemberType} {member.Name} from Mod is Null, using default value instead.");
                //尝试为成员设置默认值
                object defaultValue = GetDefaultValue(valueType);
                if (member is FieldInfo fieldInfo) {
                    fieldInfo.SetValue(null, defaultValue);
                }
                else if (member is PropertyInfo propInfo) {
                    propInfo.SetValue(null, defaultValue);
                }
                return;
            }

            if (attribute.AssetMode == AssetMode.None) {//自动指定资源类型
                attribute.AssetMode = GetAttributeAssetMode(valueType);
            }
            if (attribute.AssetMode == AssetMode.None) {//第二次检测，如果还是None就跳过
                VaultMod.Instance.Logger.Warn($"Cannot determine asset mode for {member.Name} of type {valueType}. Skipped.");
                return;
            }

            object value = LoadValue(member, attribute);
            if (valueType.IsArray && value is System.Collections.IList list) {//IList<T>类型不能直接赋值给T[]，所以这里添加一个特判，对数组进行额外的转换处理
                var array = Array.CreateInstance(valueType.GetElementType(), list.Count);
                list.CopyTo(array, 0);
                value = array;
            }

            if (member is FieldInfo fieldInfo2) {
                fieldInfo2.SetValue(null, value);
            }
            else if (member is PropertyInfo propInfo2) {
                propInfo2.SetValue(null, value);
            }
        }

        /// <summary>
        /// 获取指定类型的默认值
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <returns>该类型的默认值</returns>
        private static object GetDefaultValue(Type type) {
            if (type == typeof(Texture2D)) {
                return VaultAsset.placeholder3.Value;
            }
            else if (type == typeof(Asset<Texture2D>)) {
                return VaultAsset.placeholder3;
            }
            else if (type == typeof(IList<Texture2D>)) {
                return new List<Texture2D> { VaultAsset.placeholder3.Value };
            }
            else if (type == typeof(IList<Asset<Texture2D>>)) {
                return new List<Asset<Texture2D>> { VaultAsset.placeholder3 };
            }
            else if (type.IsValueType) {
                return Activator.CreateInstance(type);
            }
            return null;
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

        internal static Effect LoadEffectValue(VaultLoadenAttribute attribute) => LoadEffect(attribute, AssetRequestMode.ImmediateLoad).Value;

        internal static MiscShaderData LoadMiscShader(VaultLoadenAttribute attribute) {
            MiscShaderData miscShader = new MiscShaderData(LoadEffect(attribute), attribute.EffectPassname);
            string effectName = attribute.Path.Split('/')[^1];
            string effectKey = attribute.Mod.Name + ":" + effectName;
            if (!GameShaders.Misc.TryGetValue(effectKey, out var value) || value == null) {
                GameShaders.Misc[effectKey] = miscShader;
            }
            return miscShader;
        }

        internal static Asset<Texture2D> LoadTexture(VaultLoadenAttribute attribute, AssetRequestMode assetRequestMode = AssetRequestMode.AsyncLoad) {
            if (attribute.Mod == null) {
                return VaultAsset.placeholder3;
            }
            if (!attribute.Mod.HasAsset(attribute.Path)) {
                VaultMod.Instance.Logger.Warn($"Texture asset not found: {attribute.Mod.Name}/{attribute.Path}. Using placeholder instead.");
                return VaultAsset.placeholder3;
            }
            return attribute.Mod.Assets.Request<Texture2D>(attribute.Path, assetRequestMode);
        }

        internal static Texture2D LoadTextureValue(VaultLoadenAttribute attribute) => LoadTexture(attribute, AssetRequestMode.ImmediateLoad).Value;

        /// <summary>
        /// 自动探测指定路径下存在多少个连续编号的资源文件
        /// 从startIndex开始迭代检测，直到找不到下一个序号的资源为止
        /// </summary>
        /// <param name="mod">目标模组实例</param>
        /// <param name="basePath">资源基础路径(不含序号后缀)</param>
        /// <param name="startIndex">起始序号，默认为0</param>
        /// <param name="maxProbe">最大探测数量上限，防止无限循环，默认为1000</param>
        /// <returns>探测到的资源文件数量</returns>
        internal static int ProbeAssetCount(Mod mod, string basePath, int startIndex = 0, int maxProbe = 1000) {
            if (mod == null || string.IsNullOrEmpty(basePath)) {
                return 0;
            }

            int count = 0;
            for (int i = startIndex; i < startIndex + maxProbe; i++) {
                string probePath = basePath + i;
                if (mod.HasAsset(probePath)) {
                    count++;
                }
                else {
                    //遇到第一个不存在的资源就停止探测
                    break;
                }
            }

            return count;
        }

        /// <summary>
        /// 检测指定路径的资源是否存在
        /// </summary>
        /// <param name="mod">目标模组实例</param>
        /// <param name="path">资源路径</param>
        /// <returns>资源是否存在</returns>
        internal static bool HasAssetSafe(Mod mod, string path) {
            if (mod == null || string.IsNullOrEmpty(path)) {
                return false;
            }
            return mod.HasAsset(path);
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
                pathParts[0] = pathParts[0][1..]; //去掉@
                if (ModLoader.TryGetMod(pathParts[0], out Mod newMod)) {
                    attribute.Mod = newMod;
                }
                else {
                    string modName = attribute.Mod != null ? attribute.Mod.Name : pathParts[0];
                    //改为记录调试日志而非抛出异常，支持弱联动
                    VaultMod.Instance.Logger.Debug($"Class {type.FullName} couldn't find Mod \"{pathParts[0]}\". Original Mod Name: \"{modName}\". " +
                        $"Class resources will use default values instead.");
                    //将资源对象设置为null，后续会使用默认值
                    attribute.Mod = null;
                    return;
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

            //使用成员名称构造资源路径
            string memberPath;
            if (classAttribute.PathConcatenation) {
                //启用路径扩展，成员名按下划线拆分，作为子目录
                var segments = member.Name.Split('_', StringSplitOptions.RemoveEmptyEntries);
                memberPath = classAttribute.Path.TrimEnd('/');
                foreach (var seg in segments) {
                    memberPath += "/" + seg;
                }
            }
            else {
                //默认规则直接拼接成员名
                memberPath = classAttribute.Path.EndsWith('/') ? classAttribute.Path + memberName : $"{classAttribute.Path}/{memberName}";
            }

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
            //检查类上是否有 VaultLoadenAttribute
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

            ProcessClassAssetsWithAttribute(type, classAttribute, load);
        }

        /// <summary>
        /// 处理类及其成员
        /// </summary>
        private static void ProcessClassAssetsWithAttribute(Type type, VaultLoadenAttribute attribute, bool load) {
            //避免在扫描一些使用了动态代码生成或者IL源码注入的模组时出现无限递归调用
            //这种情况有可能出现吗?首先得标记了VaultLoaden，才能进入这里的处理，然后还得出现动态生成的自循环互相嵌套类
            //如果真的发生了那种事，这行代码就会起作用，不管如何，我Fuck可能会这样干的混蛋
            if (!ProcessedTypes.Add(type)) {
                return;//已处理过，跳过
            }

            //类路径校验（只有 load 阶段做）
            if (load) {
                CheckClassAttributePath(type, attribute);
            }

            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

            //处理字段
            foreach (var field in type.GetFields(flags)) {
                ProcessClassMemberPassInto(field, type, attribute, load);
            }

            //处理属性
            foreach (var property in type.GetProperties(flags)) {
                ProcessClassMemberPassInto(property, type, attribute, load);
            }

            flags = BindingFlags.NonPublic | BindingFlags.Public;

            //递归处理嵌套类
            foreach (var nestedType in type.GetNestedTypes(flags)) {
                //检查嵌套类上是否有 VaultLoadenAttribute
                VaultLoadenAttribute subClassAttribute = VaultUtils.GetAttributeSafely<VaultLoadenAttribute>(nestedType, (phase, ex) => {
                    VaultMod.Instance.Logger.Warn($"Skipped nested class {nestedType.FullName} due to {phase} load error: {ex.Message}");
                });

                if (subClassAttribute != null) {
                    continue; //嵌套类如果有自己的 VaultLoadenAttribute，就交给独立流程，不要在递归嵌套类流程里处理
                }

                //继承外层类的 attribute，并拼接路径
                var nestedAttr = new VaultLoadenAttribute(
                    CombinePath(attribute.Path, nestedType.Name),
                    attribute.AssetMode,
                    attribute.EffectPassname,
                    attribute.StartIndex,
                    attribute.ArrayCount,
                    attribute.PathConcatenation,
                    attribute.Mod
                );

                ProcessClassAssetsWithAttribute(nestedType, nestedAttr, load);
            }
        }

        private static string CombinePath(string basePath, string nestedName) {
            if (string.IsNullOrEmpty(basePath)) {
                return nestedName + "/";
            }

            //确保 basePath 末尾只有一个 "/"
            if (!basePath.EndsWith('/')) {
                basePath += "/";
            }

            return basePath + nestedName + "/";
        }
    }
}
