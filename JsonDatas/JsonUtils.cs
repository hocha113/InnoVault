using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text;

namespace InnoVault.JsonDatas
{
    ///<summary>
    ///用于管理和合并Json文件的工具类
    ///</summary>
    public static class JsonUtils
    {
        ///<summary>
        ///将源Json对象合并到目标Json对象中
        ///此操作会直接修改目标对象
        ///</summary>
        public static void Merge(JObject source, JObject target) {
            if (source == null || target == null) {
                return;
            }
            target.Merge(source, new JsonMergeSettings {
                MergeArrayHandling = MergeArrayHandling.Union
            });
        }

        ///<summary>
        ///从指定路径读取Json文件
        ///若文件不存在则返回空
        ///</summary>
        public static JObject Load(string path) {
            if (!File.Exists(path)) {
                return null;
            }
            string jsonContent = File.ReadAllText(path, Encoding.UTF8);
            return JObject.Parse(jsonContent);
        }

        ///<summary>
        ///将Json对象保存到指定路径
        ///若目录不存在会自动创建
        ///</summary>
        public static void Save(string path, JObject json) {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, json.ToString(Formatting.Indented), Encoding.UTF8);
        }
    }
}
