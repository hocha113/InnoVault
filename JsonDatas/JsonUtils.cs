using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InnoVault.JsonDatas
{
    ///<summary>
    ///用于读写、合并与序列化 Json 数据的工具类<br/>
    ///该类遵循框架统一约定：失败路径不抛异常，转而记录日志并返回安全的回退值；<br/>
    ///文件写入采用原子写盘（先写临时文件再替换），并保留 <c>.bak</c> 作为额外恢复点
    ///</summary>
    public static class JsonUtils
    {
        //统一使用不带 BOM 的 UTF-8，避免在文件头部写入字节序标记污染 Json 文本
        private static readonly UTF8Encoding utf8NoBom = new(false);

        #region 合并
        ///<summary>
        ///将源Json对象合并到目标Json对象中（数组采用 <see cref="MergeArrayHandling.Union"/> 去重合并）<br/>
        ///此操作会直接修改目标对象
        ///</summary>
        public static void Merge(JObject source, JObject target) => Merge(source, target, MergeArrayHandling.Union);

        ///<summary>
        ///将源Json对象合并到目标Json对象中，并指定数组的合并策略<br/>
        ///此操作会直接修改目标对象
        ///</summary>
        public static void Merge(JObject source, JObject target, MergeArrayHandling arrayHandling) {
            if (source == null || target == null) {
                return;
            }
            target.Merge(source, new JsonMergeSettings {
                MergeArrayHandling = arrayHandling
            });
        }

        ///<summary>
        ///依次合并一组Json对象到一个新的对象中并返回（不修改入参），<see langword="null"/> 项会被跳过
        ///</summary>
        public static JObject MergeAll(IEnumerable<JObject> sources, MergeArrayHandling arrayHandling = MergeArrayHandling.Union) {
            JObject result = [];
            if (sources == null) {
                return result;
            }
            JsonMergeSettings settings = new() { MergeArrayHandling = arrayHandling };
            foreach (JObject source in sources) {
                if (source == null) {
                    continue;
                }
                result.Merge(source, settings);
            }
            return result;
        }

        ///<summary>
        ///读取并合并一组Json文件到一个新的对象中并返回，读取失败的文件会被跳过（仅记录日志）
        ///</summary>
        public static JObject MergeFiles(IEnumerable<string> paths, MergeArrayHandling arrayHandling = MergeArrayHandling.Union) {
            JObject result = [];
            if (paths == null) {
                return result;
            }
            JsonMergeSettings settings = new() { MergeArrayHandling = arrayHandling };
            foreach (string path in paths) {
                if (TryLoad(path, out JObject json)) {
                    result.Merge(json, settings);
                }
            }
            return result;
        }

        ///<summary>
        ///对Json对象进行深拷贝，返回独立的新对象，入参为 <see langword="null"/> 时返回 <see langword="null"/>
        ///</summary>
        public static JObject Clone(JObject source) => source?.DeepClone() as JObject;
        #endregion

        #region 读取
        ///<summary>
        ///从指定路径读取Json文件<br/>
        ///若文件不存在、读取或解析失败，则记录日志并返回 <see langword="null"/>（并尝试从 <c>.bak</c> 备份恢复）
        ///</summary>
        public static JObject Load(string path) {
            TryLoad(path, out JObject json);
            return json;
        }

        ///<summary>
        ///尝试从指定路径读取并解析Json对象<br/>
        ///主文件缺失或损坏时会自动尝试同路径下的 <c>.bak</c> 备份<br/>
        ///成功时输出对象并返回 <see langword="true"/>，否则输出 <see langword="null"/> 并返回 <see langword="false"/>
        ///</summary>
        public static bool TryLoad(string path, out JObject json) {
            json = null;
            if (string.IsNullOrEmpty(path)) {
                return false;
            }

            if (File.Exists(path)) {
                try {
                    json = JObject.Parse(File.ReadAllText(path, utf8NoBom));
                    return true;
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to load json file at {path}: {ex}");
                }
            }

            string backupPath = path + ".bak";
            if (File.Exists(backupPath)) {
                try {
                    json = JObject.Parse(File.ReadAllText(backupPath, utf8NoBom));
                    VaultMod.Instance.Logger.Warn($"[JsonUtils] Primary '{path}' missing or corrupt; recovered from backup '{backupPath}'.");
                    return true;
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to load backup json file at {backupPath}: {ex}");
                }
            }

            json = null;
            return false;
        }

        ///<summary>
        ///读取指定路径的Json对象，读取失败时返回 <paramref name="fallback"/> 的执行结果；<br/>
        ///当 <paramref name="fallback"/> 为 <see langword="null"/> 时返回一个空的 <see cref="JObject"/>
        ///</summary>
        public static JObject LoadOrDefault(string path, Func<JObject> fallback = null) {
            if (TryLoad(path, out JObject json)) {
                return json;
            }
            return fallback?.Invoke() ?? [];
        }
        #endregion

        #region 写入
        ///<summary>
        ///将Json对象保存到指定路径<br/>
        ///采用原子写盘（先写临时文件再替换，并保留 <c>.bak</c>），目录不存在会自动创建；失败时记录日志而不抛出
        ///</summary>
        public static void Save(string path, JObject json) {
            if (json == null) {
                return;
            }
            WriteAllTextAtomic(path, json.ToString(Formatting.Indented));
        }

        ///<summary>
        ///尝试将Json节点保存到指定路径，写入方式与 <see cref="Save"/> 一致<br/>
        ///成功返回 <see langword="true"/>，失败返回 <see langword="false"/>
        ///</summary>
        public static bool TrySave(string path, JToken json, bool indented = true) {
            if (json == null) {
                return false;
            }
            return WriteAllTextAtomic(path, json.ToString(indented ? Formatting.Indented : Formatting.None));
        }
        #endregion

        #region 序列化
        ///<summary>
        ///将任意对象序列化为Json字符串，失败时记录日志并返回 <see langword="null"/>
        ///</summary>
        public static string Serialize(object value, bool indented = true) {
            try {
                return JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[JsonUtils] Failed to serialize object of type {value?.GetType()}: {ex}");
                return null;
            }
        }

        ///<summary>
        ///尝试将任意对象序列化为Json字符串，成功返回 <see langword="true"/>
        ///</summary>
        public static bool TrySerialize(object value, out string json, bool indented = true) {
            json = Serialize(value, indented);
            return json != null;
        }

        ///<summary>
        ///将Json字符串反序列化为 <typeparamref name="T"/>，失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static T Deserialize<T>(string json, T fallback = default) {
            if (string.IsNullOrEmpty(json)) {
                return fallback;
            }
            try {
                return JsonConvert.DeserializeObject<T>(json);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to deserialize json to {typeof(T)}: {ex}");
                return fallback;
            }
        }

        ///<summary>
        ///尝试将Json字符串反序列化为 <typeparamref name="T"/>，成功返回 <see langword="true"/>
        ///</summary>
        public static bool TryDeserialize<T>(string json, out T value) {
            value = default;
            if (string.IsNullOrEmpty(json)) {
                return false;
            }
            try {
                value = JsonConvert.DeserializeObject<T>(json);
                return true;
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to deserialize json to {typeof(T)}: {ex}");
                return false;
            }
        }

        ///<summary>
        ///将对象序列化后原子写入指定路径（写盘方式与 <see cref="Save"/> 一致），序列化失败时不写文件
        ///</summary>
        public static void SaveObject<T>(string path, T value, bool indented = true) {
            string json = Serialize(value, indented);
            if (json == null) {
                return;
            }
            WriteAllTextAtomic(path, json);
        }

        ///<summary>
        ///尝试从指定路径读取并反序列化为 <typeparamref name="T"/><br/>
        ///主文件缺失或损坏时会自动尝试同路径下的 <c>.bak</c> 备份，成功返回 <see langword="true"/>
        ///</summary>
        public static bool TryLoadObject<T>(string path, out T value) {
            value = default;
            if (string.IsNullOrEmpty(path)) {
                return false;
            }

            if (File.Exists(path)) {
                try {
                    value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path, utf8NoBom));
                    return true;
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to load object from {path}: {ex}");
                }
            }

            string backupPath = path + ".bak";
            if (File.Exists(backupPath)) {
                try {
                    value = JsonConvert.DeserializeObject<T>(File.ReadAllText(backupPath, utf8NoBom));
                    VaultMod.Instance.Logger.Warn($"[JsonUtils] Primary '{path}' missing or corrupt; recovered object from backup '{backupPath}'.");
                    return true;
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to load object from backup {backupPath}: {ex}");
                }
            }

            value = default;
            return false;
        }
        #endregion

        #region 内部写盘实现
        //将文本原子地写入目标文件：先写 .tmp 并刷盘，再原子替换目标文件，避免写入中途被打断导致目标文件损坏
        private static bool WriteAllTextAtomic(string path, string contents) {
            string tempPath = path + ".tmp";
            try {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                using (FileStream stream = File.Create(tempPath)) {
                    byte[] bytes = utf8NoBom.GetBytes(contents);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                ReplaceFileAtomic(tempPath, path);
                return true;
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[JsonUtils] Failed to write json file at {path}: {ex}");
                TryDeleteFile(tempPath);
                return false;
            }
        }

        //将临时文件原子地替换为目标文件，并把旧文件保留为 .bak 作为额外的恢复点
        private static void ReplaceFileAtomic(string tempPath, string path) {
            if (!File.Exists(path)) {
                File.Move(tempPath, path);
                return;
            }

            string backupPath = path + ".bak";
            try {
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
            } catch {
                //个别文件系统不支持 File.Replace，退化为"旧文件改名 .bak + 新文件就位"
                TryDeleteFile(backupPath);
                File.Move(path, backupPath);
                File.Move(tempPath, path);
            }
        }

        //尽力删除一个文件，失败仅记录警告，不抛出
        private static void TryDeleteFile(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[JsonUtils] Failed to delete file {path}: {ex}");
            }
        }
        #endregion
    }
}
