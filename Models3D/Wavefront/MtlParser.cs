using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 轻量级 MTL 文本解析器
    /// <br/>支持: <c>newmtl</c> <c>Kd</c> <c>map_Kd</c> <c>d</c> <c>Tr</c>
    /// <br/>其它字段（Ka/Ks/Ns/illum/map_Bump 等）一律忽略
    /// </summary>
    public static class MtlParser
    {
        private static readonly char[] _whitespace = new char[] { ' ', '\t' };

        /// <summary>
        /// 从文本流中解析 MTL 数据
        /// </summary>
        /// <param name="reader">MTL 文件文本流</param>
        /// <param name="diagnostic">诊断收集器</param>
        /// <param name="source">用于诊断输出的源标识，例如相对路径</param>
        /// <returns>材质名到材质对象的字典</returns>
        public static Dictionary<string, ObjMaterial> Parse(TextReader reader, ObjDiagnostic diagnostic, string source) {
            Dictionary<string, ObjMaterial> result = new();
            if (reader == null) {
                diagnostic?.Error(source, 0, "MTL reader is null");
                return result;
            }

            diagnostic ??= new ObjDiagnostic();

            ObjMaterial current = null;
            string line;
            int lineNo = 0;
            while ((line = reader.ReadLine()) != null) {
                lineNo++;
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') {
                    continue;
                }

                string[] parts = trimmed.Split(_whitespace, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) {
                    continue;
                }

                string keyword = parts[0];
                switch (keyword) {
                    case "newmtl":
                        if (parts.Length < 2) {
                            diagnostic.Warn(source, lineNo, "newmtl missing material name");
                            current = null;
                            break;
                        }
                        string name = string.Join(" ", parts, 1, parts.Length - 1);
                        current = new ObjMaterial(name);
                        if (!result.ContainsKey(name)) {
                            result[name] = current;
                        }
                        else {
                            diagnostic.Warn(source, lineNo, $"Duplicate material name '{name}', later definition overrides earlier");
                            result[name] = current;
                        }
                        break;
                    case "Kd":
                        if (current == null) {
                            diagnostic.Warn(source, lineNo, "Kd appears before any newmtl, ignored");
                            break;
                        }
                        if (TryParseColor3(parts, 1, out Color kd)) {
                            current.DiffuseColor = new Color(kd.R, kd.G, kd.B, current.DiffuseColor.A);
                        }
                        else {
                            diagnostic.Warn(source, lineNo, "Malformed Kd directive");
                        }
                        break;
                    case "d":
                        if (current == null) {
                            diagnostic.Warn(source, lineNo, "d appears before any newmtl, ignored");
                            break;
                        }
                        if (parts.Length >= 2
                            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float dValue)) {
                            current.Opacity = MathHelper.Clamp(dValue, 0f, 1f);
                        }
                        else {
                            diagnostic.Warn(source, lineNo, "Malformed 'd' directive");
                        }
                        break;
                    case "Tr":
                        if (current == null) {
                            diagnostic.Warn(source, lineNo, "Tr appears before any newmtl, ignored");
                            break;
                        }
                        if (parts.Length >= 2
                            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float trValue)) {
                            current.Opacity = MathHelper.Clamp(1f - trValue, 0f, 1f);
                        }
                        else {
                            diagnostic.Warn(source, lineNo, "Malformed 'Tr' directive");
                        }
                        break;
                    case "map_Kd":
                        if (current == null) {
                            diagnostic.Warn(source, lineNo, "map_Kd appears before any newmtl, ignored");
                            break;
                        }
                        //取最后一个非选项 token 作为路径
                        //形如 `map_Kd -clamp on -s 1 1 1 path/to/tex.png`
                        string mapPath = ExtractMapPath(parts, 1);
                        if (string.IsNullOrEmpty(mapPath)) {
                            diagnostic.Warn(source, lineNo, "map_Kd missing texture path");
                        }
                        else {
                            current.DiffuseTexturePath = mapPath;
                        }
                        break;
                    default:
                        //忽略其它 MTL 字段
                        break;
                }
            }

            return result;
        }

        private static bool TryParseColor3(string[] parts, int offset, out Color result) {
            result = Color.White;
            if (parts.Length < offset + 3) {
                return false;
            }
            if (!float.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out float r)
                || !float.TryParse(parts[offset + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g)
                || !float.TryParse(parts[offset + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b)) {
                return false;
            }
            result = new Color(MathHelper.Clamp(r, 0f, 1f), MathHelper.Clamp(g, 0f, 1f), MathHelper.Clamp(b, 0f, 1f));
            return true;
        }

        /// <summary>
        /// 从 <c>map_Kd</c> 后续 tokens 中提取贴图路径
        /// <br/>跳过以 <c>-</c> 开头的选项及其参数（粗略处理）
        /// </summary>
        private static string ExtractMapPath(string[] parts, int offset) {
            //简化策略：从最后一个 token 倒着找第一个不以 '-' 开头且前一个不是 '-xxx' 选项的 token
            //大多数 map_Kd 写法会把路径放在最后；此处直接取末尾 token 已可覆盖常见情况
            for (int i = parts.Length - 1; i >= offset; i--) {
                string token = parts[i];
                if (token.Length == 0) {
                    continue;
                }
                if (token[0] == '-') {
                    continue;
                }
                //排除位于一个选项后的纯数值参数: 如 `-s 1 1 1` 中的 `1 1 1`
                //这里使用简单启发：如果是数值则继续向前找
                if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
                    continue;
                }
                return token;
            }
            return string.Empty;
        }
    }
}
