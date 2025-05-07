using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System.Reflection;
using System;
using Terraria.Audio;
using Terraria.ModLoader;
using System.Linq;

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
        /// 纹理，即<see cref="Texture2D"/>类型
        /// </summary>
        Texture,
        /// <summary>
        /// 渲染类文件
        /// </summary>
        Effects,
        /// <summary>
        /// 声音，即<see cref="SoundStyle"/>类型
        /// </summary>
        Sound,
    }

    /// <summary>
    /// <br>一个指定用于加载操作的标签</br>
    /// <br>可以用于标记静态的属性或者字段，自动为其分配和管理资源的值</br>
    /// <br>资源类型会根据目标的声明类型自动指定</br>
    /// <br>如果目标是只读属性则无法加载</br>
    /// </summary>
    /// <param name="path"></param>
    /// <param name="assetMode"></param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class VaultLoadenAttribute(string path, AssetMode assetMode = AssetMode.None) : Attribute
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
    }

    /// <summary>
    /// 管理资源
    /// </summary>
    public static class VaultLoad
    {
        internal static void LoadAsset() {
            Type[] allModTypes = VaultUtils.GetAnyModCodeType();
            foreach (var t in allModTypes) {
                LoadenByTypeAsset(t);
            }
        }
        internal static void UnLoadAsset() {
            Type[] allModTypes = VaultUtils.GetAnyModCodeType();
            foreach (var t in allModTypes) {
                UnLoadenByTypeAsset(t);
            }
        }

        private static void LoadenByTypeAsset(Type type) {
            // 获取当前类的所有静态字段
            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (FieldInfo field in fields) {
                // 检查字段是否标记了 VaultLoadenAttribute
                VaultLoadenAttribute attribute;
                try {
                    attribute = field.GetCustomAttribute<VaultLoadenAttribute>();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Skipped field {field.Name} due to attribute load error: {ex.Message}");
                    continue;
                }

                if (attribute == null) {
                    continue;
                }
                // 通过检查后开始加载
                try {
                    FindattributeByMod(type, attribute);
                    LoadenByFieldAsset(field, attribute);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"Failed to load asset for field {field.Name} at path: {attribute.Path}. Error: {ex.Message}");
                }
            }

            // 获取当前类的所有静态属性
            var properties = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (PropertyInfo property in properties) {
                // 检查属性是否标记了 VaultLoadenAttribute
                VaultLoadenAttribute attribute;
                try {
                    attribute = property.GetCustomAttribute<VaultLoadenAttribute>();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Skipped property {property.Name} due to attribute load error: {ex.Message}");
                    continue;
                }

                if (attribute == null) {
                    continue;
                }
                // 检查属性是否有 setter
                if (!property.CanWrite || property.GetSetMethod(true) == null) {
                    VaultMod.Instance.Logger.Error($"Property {property.Name} is marked with VaultLoadenAttribute but has no setter.");
                    continue;
                }
                // 通过检查后开始加载
                try {
                    FindattributeByMod(type, attribute);
                    LoadenByPropertyAsset(property, attribute);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"Failed to load asset for property {property.Name} at path: {attribute.Path}. Error: {ex.Message}");
                }
            }
        }

        private static void FindattributeByMod(Type type, VaultLoadenAttribute attribute) {
            if (attribute.Mod != null) {
                return;// 如果已经手动指定了模组对象就不需要进行查找了
            }
            Mod mod = VaultUtils.FindModByType(type, ModLoader.Mods);
            attribute.Mod = mod;
        }

        private static void CheckAttributePath(VaultLoadenAttribute attribute) {
            // 校验和处理 attribute.Path
            string[] pathParts = attribute.Path.Split('/');
            if (pathParts.Length > 1 && pathParts[0] == attribute.Mod.Name) {
                // 如果路径以模组名开头，去掉模组名部分
                attribute.Path = string.Join("/", pathParts.Skip(1));
            }
        }

        private static void LoadenByFieldAsset(FieldInfo field, VaultLoadenAttribute attribute) {
            if (attribute.Mod == null) {
                VaultMod.Instance.Logger.Error($"Failed {field.Name} from Mod is Null");
                return;
            }

            CheckAttributePath(attribute);

            if (attribute.AssetMode == AssetMode.None) {
                if (field.FieldType == typeof(Asset<Texture2D>)) {
                    attribute.AssetMode = AssetMode.Texture;
                }
                else if (field.FieldType == typeof(Asset<Effect>)) {
                    attribute.AssetMode = AssetMode.Effects;
                }
                else if (field.FieldType == typeof(SoundStyle)) {
                    attribute.AssetMode = AssetMode.Sound;
                }
            }
            switch (attribute.AssetMode) {
                case AssetMode.Texture:
                    field.SetValue(null, attribute.Mod.Assets.Request<Texture2D>(attribute.Path));
                    break;
                case AssetMode.Effects:
                    field.SetValue(null, attribute.Mod.Assets.Request<Effect>(attribute.Path));
                    break;
                case AssetMode.Sound:
                    field.SetValue(null, new SoundStyle(attribute.Mod.Name + "/" + attribute.Path));
                    break;
            }
        }

        private static void LoadenByPropertyAsset(PropertyInfo property, VaultLoadenAttribute attribute) {
            if (attribute.Mod == null) {
                VaultMod.Instance.Logger.Error($"Property {property.Name} from Mod is Null");
                return;
            }

            CheckAttributePath(attribute);

            if (attribute.AssetMode == AssetMode.None) {
                if (property.PropertyType == typeof(Asset<Texture2D>)) {
                    attribute.AssetMode = AssetMode.Texture;
                }
                else if (property.PropertyType == typeof(Asset<Effect>)) {
                    attribute.AssetMode = AssetMode.Effects;
                }
                else if (property.PropertyType == typeof(SoundStyle)) {
                    attribute.AssetMode = AssetMode.Sound;
                }
            }
            switch (attribute.AssetMode) {
                case AssetMode.Texture:
                    property.SetValue(null, attribute.Mod.Assets.Request<Texture2D>(attribute.Path));
                    break;
                case AssetMode.Effects:
                    property.SetValue(null, attribute.Mod.Assets.Request<Effect>(attribute.Path));
                    break;
                case AssetMode.Sound:
                    property.SetValue(null, new SoundStyle(attribute.Mod.Name + "/" + attribute.Path));
                    break;
            }
        }

        private static void UnLoadenByTypeAsset(Type type) {
            // 获取当前类的所有静态字段
            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (FieldInfo field in fields) {
                // 检查字段是否标记了 VaultLoadenAttribute
                VaultLoadenAttribute attribute;
                try {
                    attribute = field.GetCustomAttribute<VaultLoadenAttribute>();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Skipped field {field.Name} due to attribute load error: {ex.Message}");
                    continue;
                }

                if (attribute == null) {
                    continue;
                }

                field.SetValue(null, null);
                attribute.Mod = null;
            }

            // 获取当前类的所有静态属性
            var properties = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (PropertyInfo property in properties) {
                // 检查属性是否标记了 VaultLoadenAttribute
                VaultLoadenAttribute attribute;
                try {
                    attribute = property.GetCustomAttribute<VaultLoadenAttribute>();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"Skipped property {property.Name} due to attribute load error: {ex.Message}");
                    continue;
                }

                if (attribute == null) {
                    continue;
                }

                // 仅对有 setter 的属性进行卸载
                if (property.CanWrite && property.GetSetMethod(true) != null) {
                    property.SetValue(null, null);
                }
                attribute.Mod = null;
            }
        }
    }
}
