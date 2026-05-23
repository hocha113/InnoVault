using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 轻量级 OBJ 文本解析器，只产出纯数据 <see cref="ObjRawData"/>
    /// <br/>支持: <c>v</c> <c>vt</c> <c>vn</c> <c>f</c> <c>usemtl</c> <c>mtllib</c> <c>o</c> <c>g</c> <c>s</c> 注释
    /// <br/>不支持: 自由曲面（curv/surf 等）、点/线（p/l）、相对索引外的更高级语法
    /// </summary>
    public static class ObjParser
    {
        private static readonly char[] _whitespace = new char[] { ' ', '\t' };
        private static readonly char[] _faceSeparator = new char[] { '/' };

        /// <summary>
        /// 从文本流中解析 OBJ 数据
        /// </summary>
        /// <param name="reader">OBJ 文件文本流</param>
        /// <param name="diagnostic">诊断收集器，<see langword="null"/> 时会内部创建一个</param>
        /// <param name="source">用于诊断输出的源标识，例如相对路径</param>
        /// <param name="options">解析选项，仅影响 n-gon 三角化策略，<see langword="null"/> 时使用默认值</param>
        /// <returns>解析结果，永不为 <see langword="null"/></returns>
        public static ObjRawData Parse(TextReader reader, ObjDiagnostic diagnostic, string source, ObjImportOptions options) {
            ObjRawData data = new ObjRawData();
            if (reader == null) {
                diagnostic?.Error(source, 0, "OBJ reader is null");
                return data;
            }

            options ??= ObjImportOptions.Default;
            diagnostic ??= new ObjDiagnostic();

            string currentMaterial = string.Empty;
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
                    case "v":
                        if (TryParseVector3(parts, 1, out Vector3 position)) {
                            data.Positions.Add(position);
                        }
                        else {
                            diagnostic.Warn(source, lineNo, "Malformed 'v' directive");
                        }
                        break;
                    case "vt":
                        if (TryParseVector2(parts, 1, out Vector2 uv)) {
                            data.TexCoords.Add(uv);
                        }
                        else {
                            diagnostic.Warn(source, lineNo, "Malformed 'vt' directive");
                        }
                        break;
                    case "vn":
                        if (TryParseVector3(parts, 1, out Vector3 normal)) {
                            data.Normals.Add(normal);
                        }
                        else {
                            diagnostic.Warn(source, lineNo, "Malformed 'vn' directive");
                        }
                        break;
                    case "f":
                        ParseFace(parts, data, currentMaterial, diagnostic, source, lineNo, options);
                        break;
                    case "usemtl":
                        currentMaterial = parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : string.Empty;
                        break;
                    case "mtllib":
                        if (parts.Length > 1) {
                            for (int i = 1; i < parts.Length; i++) {
                                data.MaterialLibraries.Add(parts[i]);
                            }
                        }
                        break;
                    case "o":
                    case "g":
                    case "s":
                        // 第一版忽略对象/分组/平滑组
                        break;
                    default:
                        // 未知指令：仅在第一次出现时记录，避免诊断爆炸
                        // 这里直接跳过，OBJ 中常见的非核心指令很多
                        break;
                }
            }

            return data;
        }

        private static void ParseFace(string[] parts, ObjRawData data, string currentMaterial
            , ObjDiagnostic diagnostic, string source, int lineNo, ObjImportOptions options) {
            int count = parts.Length - 1;
            if (count < 3) {
                diagnostic.Warn(source, lineNo, $"Face has only {count} vertices, skipped");
                return;
            }

            ObjFaceVertex[] verts = new ObjFaceVertex[count];
            for (int i = 0; i < count; i++) {
                if (!TryParseFaceVertex(parts[i + 1], data, out verts[i])) {
                    diagnostic.Warn(source, lineNo, $"Malformed face vertex token '{parts[i + 1]}'");
                    return;
                }
            }

            if (count == 3) {
                data.Faces.Add(new ObjFace(verts, currentMaterial));
                return;
            }
            if (count == 4) {
                ObjFaceVertex[] tri1 = new ObjFaceVertex[] { verts[0], verts[1], verts[2] };
                ObjFaceVertex[] tri2 = new ObjFaceVertex[] { verts[0], verts[2], verts[3] };
                data.Faces.Add(new ObjFace(tri1, currentMaterial));
                data.Faces.Add(new ObjFace(tri2, currentMaterial));
                return;
            }

            if (!options.TriangulateNGons) {
                diagnostic.Warn(source, lineNo, $"Face with {count} vertices skipped (TriangulateNGons disabled)");
                return;
            }

            // 扇形三角化：(0, i, i+1) for i in [1, count-1)
            for (int i = 1; i < count - 1; i++) {
                ObjFaceVertex[] tri = new ObjFaceVertex[] { verts[0], verts[i], verts[i + 1] };
                data.Faces.Add(new ObjFace(tri, currentMaterial));
            }
        }

        private static bool TryParseFaceVertex(string token, ObjRawData data, out ObjFaceVertex vertex) {
            vertex = default;
            if (string.IsNullOrEmpty(token)) {
                return false;
            }

            string[] segs = token.Split(_faceSeparator);
            int pos = -1, uv = -1, n = -1;

            if (segs.Length >= 1 && !TryParseFaceIndex(segs[0], data.Positions.Count, out pos)) {
                return false;
            }
            if (segs.Length >= 2 && segs[1].Length > 0 && !TryParseFaceIndex(segs[1], data.TexCoords.Count, out uv)) {
                return false;
            }
            if (segs.Length >= 3 && segs[2].Length > 0 && !TryParseFaceIndex(segs[2], data.Normals.Count, out n)) {
                return false;
            }

            vertex = new ObjFaceVertex(pos, uv, n);
            return true;
        }

        /// <summary>
        /// 解析 OBJ 中的索引数字：1 基；负数代表"从末尾倒数"
        /// </summary>
        private static bool TryParseFaceIndex(string raw, int currentCount, out int zeroBased) {
            zeroBased = -1;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) {
                return false;
            }
            if (v == 0) {
                return false; // OBJ 索引从 1 开始，0 非法
            }
            if (v > 0) {
                zeroBased = v - 1;
            }
            else {
                zeroBased = currentCount + v; // currentCount 之后的负偏移
            }
            return zeroBased >= 0;
        }

        private static bool TryParseVector3(string[] parts, int offset, out Vector3 result) {
            result = Vector3.Zero;
            if (parts.Length < offset + 3) {
                return false;
            }
            if (!float.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[offset + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(parts[offset + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
                return false;
            }
            result = new Vector3(x, y, z);
            return true;
        }

        private static bool TryParseVector2(string[] parts, int offset, out Vector2 result) {
            result = Vector2.Zero;
            if (parts.Length < offset + 2) {
                // OBJ 允许 1D 纹理坐标，第二维默认为 0
                if (parts.Length == offset + 1
                    && float.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out float u1)) {
                    result = new Vector2(u1, 0f);
                    return true;
                }
                return false;
            }
            if (!float.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out float u)
                || !float.TryParse(parts[offset + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) {
                return false;
            }
            result = new Vector2(u, v);
            return true;
        }
    }
}
