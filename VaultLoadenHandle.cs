using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 自动资源加载处理器的抽象基类
    /// <br/>继承此类以扩展<see cref="VaultLoadenAttribute"/>标签系统支持的资源类型
    /// <br/>系统会自动扫描并注册所有继承此类的实例
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>工作流程</b>
    /// <list type="bullet">
    ///   <item>加载器实例在模块启动时自动创建并注册到<see cref="VaultLoadenHandleManager"/></item>
    ///   <item>在资源加载阶段，系统根据成员类型和标签匹配加载器</item>
    ///   <item>模块卸载时，加载器会被自动清理</item>
    /// </list>
    /// </para>
    /// </remarks>
    public abstract class VaultLoadenHandle
    {
        /// <summary>
        /// 此加载器所属的模块实例，由系统自动设置
        /// </summary>
        public Mod Mod { get; internal set; }
        /// <summary>
        /// 此加载器支持的目标类型
        /// <br/>系统根据此类型匹配需要加载的成员
        /// </summary>
        public abstract Type TargetType { get; }
        /// <summary>
        /// 加载器的优先级，数值越大优先级越高
        /// <br/>当多个加载器支持同一类型时，将优先使用高优先级的加载器
        /// <br/>未设置时，默认优先级为0，自动加载器通常使用此值
        /// </summary>
        public virtual int Priority => 0;
        /// <summary>
        /// 是否支持以数组列表的形式批量加载
        /// <br/>若返回<see langword="true"/>，系统会自动将数组类型的成员列表加载
        /// </summary>
        public virtual bool SupportArrayLoading => true;
        /// <summary>
        /// 是否在专用服务器上也执行加载
        /// <br/>默认<see langword="false"/>：如图像、特效、声音等一般只有客户端需要的资源，在服务器上不存在
        /// <br/>如果"服务器共用的资源"（加载器是架构类、配置表或者可控数据等），请写为<see langword="true"/>。
        /// 这样在 <c>PostSetupContent</c> 进行同一轮 <see cref="VaultLoadenAttribute"/> 扫描，
        /// 但只会跳过标记了服务器的成员，让这些成员在服务器上保持默认值
        /// <br/>若写为真：实现时请自己保证 <see cref="HandleLoad"/> 在 <c>Main.dedServ</c> 下不调用 GPU 资源或 <c>Assets.Request</c>
        /// </summary>
        public virtual bool LoadOnServer => false;
        /// <summary>
        /// 判断此加载器是否能处理指定的类型
        /// <br/>默认实现会检查类型是否是<see cref="TargetType"/>的完全匹配或派生类
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <returns>如果能处理则返回<see langword="true"/></returns>
        public virtual bool CanHandle(Type type) {
            if (type == null || TargetType == null) {
                return false;
            }
            return TargetType.IsAssignableFrom(type) || type == TargetType;
        }
        /// <summary>
        /// 判断此加载器是否能处理指定类型的数组列表元素
        /// <br/>用于处理数组成员时的类型匹配
        /// </summary>
        /// <param name="elementType">数组列表的元素类型</param>
        /// <returns>如果能处理则返回<see langword="true"/></returns>
        public virtual bool CanHandleArrayElement(Type elementType) {
            return SupportArrayLoading && CanHandle(elementType);
        }
        /// <summary>
        /// 加载资源的核心方法
        /// </summary>
        /// <param name="member">要加载资源的成员(字段或属性)</param>
        /// <param name="attribute">成员上的<see cref="VaultLoadenAttribute"/>标签</param>
        /// <returns>返回加载的资源对象</returns>
        public abstract object HandleLoad(MemberInfo member, VaultLoadenAttribute attribute);
        /// <summary>
        /// 获取该类型的默认值，用于当资源加载失败或模块未加载时
        /// <br/>默认返回<see langword="null"/>，值类型会返回其默认值
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <returns>该类型的默认值</returns>
        public virtual object GetDefaultValue(Type type) {
            if (type.IsValueType) {
                return Activator.CreateInstance(type);
            }
            return null;
        }
        /// <summary>
        /// 卸载资源时的清理逻辑
        /// <br/>默认不执行任何操作，子类可以重写实现自定义清理
        /// </summary>
        /// <param name="member">要卸载资源的成员</param>
        /// <param name="currentValue">当前的值</param>
        public virtual void HandleUnload(MemberInfo member, object currentValue) {
            //默认不执行任何操作，子类可以重写实现自定义清理
        }
        /// <summary>
        /// 加载器初始化时调用
        /// <br/>可以在此方法中进行一些初始化工作
        /// </summary>
        public virtual void OnInitialize() {
        }
        /// <summary>
        /// 加载器卸载时调用
        /// <br/>可以在此方法中进行一些清理工作
        /// </summary>
        public virtual void OnDispose() {
        }
    }

    /// <summary>
    /// 管理所有自动资源加载处理器的静态类
    /// <br/>负责加载器的注册、查找和生命周期管理
    /// </summary>
    public static class VaultLoadenHandleManager
    {
        private static readonly List<VaultLoadenHandle> _loaders = [];
        private static bool _initialized = false;
        /// <summary>
        /// 所有已注册的加载器实例(只读)
        /// </summary>
        public static IReadOnlyList<VaultLoadenHandle> Loaders => _loaders;
        /// <summary>
        /// 注册一个自定义加载器
        /// </summary>
        /// <param name="loader">要注册的加载器实例</param>
        public static void Register(VaultLoadenHandle loader) {
            if (loader == null) {
                return;
            }
            //检查是否已存在相同类型的加载器
            for (int i = 0; i < _loaders.Count; i++) {
                if (_loaders[i].GetType() == loader.GetType()) {
                    return;//已存在，不重复注册
                }
            }
            _loaders.Add(loader);
            //按优先级排序，高优先级在前
            _loaders.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
        /// <summary>
        /// 注销一个自定义加载器
        /// </summary>
        /// <param name="loader">要注销的加载器实例</param>
        public static void Unregister(VaultLoadenHandle loader) {
            if (loader == null) {
                return;
            }
            _loaders.Remove(loader);
        }
        /// <summary>
        /// 查找可以处理指定类型的加载器
        /// </summary>
        /// <param name="type">要处理的类型</param>
        /// <returns>找到的加载器，如果没有则返回<see langword="null"/></returns>
        public static VaultLoadenHandle FindLoader(Type type) {
            foreach (var loader in _loaders) {
                if (loader.CanHandle(type)) {
                    return loader;
                }
            }
            return null;
        }
        /// <summary>
        /// 查找可以处理指定数组元素类型的加载器
        /// </summary>
        /// <param name="elementType">数组列表的元素类型</param>
        /// <returns>找到的加载器，如果没有则返回<see langword="null"/></returns>
        public static VaultLoadenHandle FindArrayElementLoader(Type elementType) {
            foreach (var loader in _loaders) {
                if (loader.CanHandleArrayElement(elementType)) {
                    return loader;
                }
            }
            return null;
        }
        /// <summary>
        /// 某成员类型（本体类型 / 列表类型）是否由带有 <see cref="VaultLoadenHandle.LoadOnServer"/> 的加载器处理
        /// <br/>专用服务器上的 <see cref="VaultLoadenAttribute"/> 扫描依此决定跳过哪些成员
        /// </summary>
        /// <param name="type">成员本身的类型</param>
        /// <returns>加载器是否应当加载</returns>
        public static bool IsServerLoadable(Type type) {
            if (type == null) {
                return false;
            }
            VaultLoadenHandle loader = FindLoader(type);
            if (loader != null) {
                return loader.LoadOnServer;
            }
            Type elementType = VaultLoad.GetArrayElementType(type);
            if (elementType != null) {
                VaultLoadenHandle arrayLoader = FindArrayElementLoader(elementType);
                return arrayLoader != null && arrayLoader.LoadOnServer;
            }
            return false;
        }
        /// <summary>
        /// 初始化管理器：扫描并注册所有自定义加载器
        /// </summary>
        internal static void Initialize() {
            if (_initialized) {
                return;
            }
            _initialized = true;
            //扫描所有模块中继承VaultAssetLoader的类型并创建实例
            foreach (var type in VaultUtils.GetDerivedTypes<VaultLoadenHandle>()) {
                try {
                    var loader = (VaultLoadenHandle)Activator.CreateInstance(type);
                    loader.Mod = VaultUtils.FindModByType(type, ModLoader.Mods);
                    loader.OnInitialize();
                    Register(loader);
                    VaultMod.Instance?.Logger.Debug($"Registered custom asset loader: {type.FullName}");
                } catch (Exception ex) {
                    VaultMod.Instance?.Logger.Error($"Failed to create asset loader instance for type {type.FullName}: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 清理所有已注册的加载器
        /// </summary>
        internal static void Unload() {
            foreach (var loader in _loaders) {
                try {
                    loader.OnDispose();
                } catch (Exception ex) {
                    VaultMod.Instance?.Logger.Error($"Error unloading asset loader {loader.GetType().FullName}: {ex.Message}");
                }
            }
            _loaders.Clear();
            _initialized = false;
        }
    }
}
