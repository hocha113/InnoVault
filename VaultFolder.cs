using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria.Audio;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// 标记一个 string 类型的字段或属性，其值将作为资源文件夹路径进行扫描
    /// <br/>此特性标记的成员必须位于 Mod 或其子类的实例中
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class VaultFolderAttribute : Attribute
    {

    }

    /// <summary>
    /// 管理通过 VaultFolderAttribute 扫描和加载的资源。
    /// </summary>
    public static class VaultFolderSystem
    {
        /// <summary>
        /// 存储所有自动加载的纹理资源。
        /// <br>Key: "{ModName}/{FileNameWithoutExtension}" (例如 "MyMod/Bosses/CoolBoss")</br>
        /// </summary>
        public static readonly Dictionary<string, Asset<Texture2D>> Textures = new();

        /// <summary>
        /// 存储所有自动加载的音效资源。
        /// <br>Key: "{ModName}/{FileNameWithoutExtension}" (例如 "MyMod/Sounds/Item/Equip_01")</br>
        /// </summary>
        public static readonly Dictionary<string, SoundStyle> Sounds = new();

        // 未来可以扩展，例如支持 Effect
        // public static readonly Dictionary<string, Asset<Effect>> Effects = new();

        /// <summary>
        /// 在Mod加载时由系统调用，执行扫描和加载。
        /// </summary>
        internal static void Load(Type type) {
            // 1. 寻找所有标记了特性的成员
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.IsDefined(typeof(VaultFolderAttribute)));
            Mod mod = VaultUtils.FindModByType(type, ModLoader.Mods);
            foreach (var member in members) {
                // 2. 验证成员的合法性
                ValidateMember(member, mod);

                // 3. 获取成员的值（即文件夹路径）
                string folderPath = GetMemberValue(member, mod) as string;
                if (string.IsNullOrWhiteSpace(folderPath)) {
                    continue; // 如果路径为空则跳过
                }

                // 4. 扫描并加载路径下的资源
                ScanAndLoadFromFolder(mod, folderPath);
            }
        }

        /// <summary>
        /// 在Mod卸载时由系统调用，清空所有字典
        /// </summary>
        internal static void UnLoad() {
            Textures.Clear();
            Sounds.Clear();
            // Effects.Clear();
        }

        private static void ScanAndLoadFromFolder(Mod mod, string folderPath) {
            // tModLoader 规范化路径
            folderPath = folderPath.Replace('\\', '/').Trim('/');

            // 使用 tModLoader 的 API 来枚举模组内的所有资源
            foreach (string assetPath in mod.RootContentSource.EnumerateAssets()) {
                if (assetPath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase)) {
                    string extension = Path.GetExtension(assetPath).ToLowerInvariant();
                    // 移除模组名和文件后缀，得到一个相对干净的 Key
                    // 例如 "MyMod/Assets/Textures/Items/Sword.png" -> "Assets/Textures/Items/Sword"
                    string keyPath = Path.ChangeExtension(assetPath, null);
                    string fullKey = $"{mod.Name}/{keyPath}";

                    switch (extension) {
                        case ".png":
                            if (!Textures.ContainsKey(fullKey)) {
                                Textures[fullKey] = mod.Assets.Request<Texture2D>(keyPath);
                            }
                            break;
                        case ".ogg":
                        case ".wav":
                            if (!Sounds.ContainsKey(fullKey)) {
                                Sounds[fullKey] = new SoundStyle(keyPath);
                            }
                            break;
                            // case ".xnb":
                            //     // XNB可以是任何东西，但通常用于Effect
                            //     if (!Effects.ContainsKey(fullKey))
                            //     {
                            //         Effects[fullKey] = mod.Assets.Request<Effect>(keyPath);
                            //     }
                            //     break;
                    }
                }
            }
        }

        private static void ValidateMember(MemberInfo member, Mod mod) {
            // 检查是否在 Mod 或其子类下 (这一步在 Load 方法的循环里已经隐式保证了)

            Type memberType;

            if (member is FieldInfo field) {
                memberType = field.FieldType;
            }
            else if (member is PropertyInfo property) {
                memberType = property.PropertyType;
                if (!property.CanRead) {
                    throw new InvalidOperationException($"[InnoVault] [VaultFolder] 标记的属性 '{member.Name}' 在类 '{mod.Name}' 中必须是可读的。");
                }
            }
            else {
                // 理论上不会发生，因为 AttributeUsage 已经限制
                return;
            }

            if (memberType != typeof(string)) {
                throw new InvalidOperationException($"[InnoVault] [VaultFolder] 标记的成员 '{member.Name}' 在类 '{mod.Name}' 中必须是 string 类型。");
            }
        }

        private static object GetMemberValue(MemberInfo member, Mod modInstance) {
            if (member is FieldInfo field) {
                return field.GetValue(field.IsStatic ? null : modInstance);
            }
            if (member is PropertyInfo property) {
                return property.GetValue(property.GetGetMethod(true).IsStatic ? null : modInstance);
            }
            return null;
        }
    }
}
