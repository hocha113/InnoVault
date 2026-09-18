using Microsoft.Xna.Framework;
using System;
using System.Globalization;

namespace InnoVault.Vectors
{
    /// <summary>
    /// SVG <c>path</c> 元素 <c>d</c> 属性的完整语法解析器：
    /// <c>M L H V C S Q T A Z</c> 及其相对小写形式、隐式重复参数、指数数字、紧凑负号（<c>1-2</c>）、<c>A</c> 的无分隔标志位（<c>0 01 50 50</c>）
    /// <br/>解析结果直接写入 <see cref="VectorPathBuilder"/>，遇到语法错误立即停止并给出位置，不会静默吞掉后半段
    /// </summary>
    public static class SvgPathParser
    {
        /// <summary>
        /// 解析 <paramref name="d"/> 并把指令追加到 <paramref name="builder"/>
        /// </summary>
        /// <returns>全部解析成功返回 true；失败时 <paramref name="error"/> 描述原因与字符位置，构造器保留错误之前的指令</returns>
        public static bool TryParse(string d, VectorPathBuilder builder, out string error) {
            error = null;
            if (builder == null) {
                error = "builder is null";
                return false;
            }
            if (string.IsNullOrEmpty(d)) {
                return true;
            }

            int i = 0;
            char command = '\0';
            while (true) {
                SkipSeparators(d, ref i);
                if (i >= d.Length) {
                    return true;
                }
                char c = d[i];
                if (IsCommand(c)) {
                    command = c;
                    i++;
                    if (command == 'Z' || command == 'z') {
                        builder.Close();
                        continue;
                    }
                }
                else if (command == '\0') {
                    error = $"expected a command letter at {i}, found '{c}'";
                    return false;
                }
                else if (IsNumberStart(c)) {
                    //隐式重复：M 的后续坐标对视为 L
                    if (command == 'M') {
                        command = 'L';
                    }
                    else if (command == 'm') {
                        command = 'l';
                    }
                    else if (command == 'Z' || command == 'z') {
                        error = $"number after Z at {i}";
                        return false;
                    }
                }
                else {
                    error = $"unexpected character '{c}' at {i}";
                    return false;
                }

                bool relative = char.IsLower(command);
                Vector2 cur = builder.CurrentPoint;
                int paramStart = i;
                switch (char.ToUpperInvariant(command)) {
                    case 'M': {
                        if (!TryReadPoint(d, ref i, cur, relative, out Vector2 p)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.MoveTo(p);
                        break;
                    }
                    case 'L': {
                        if (!TryReadPoint(d, ref i, cur, relative, out Vector2 p)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.LineTo(p);
                        break;
                    }
                    case 'H': {
                        if (!TryReadNumber(d, ref i, out float x)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.HorizontalTo(relative ? cur.X + x : x);
                        break;
                    }
                    case 'V': {
                        if (!TryReadNumber(d, ref i, out float y)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.VerticalTo(relative ? cur.Y + y : y);
                        break;
                    }
                    case 'C': {
                        if (!TryReadPoint(d, ref i, cur, relative, out Vector2 c1)
                            || !TryReadPoint(d, ref i, cur, relative, out Vector2 c2)
                            || !TryReadPoint(d, ref i, cur, relative, out Vector2 end)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.CubicTo(c1, c2, end);
                        break;
                    }
                    case 'S': {
                        if (!TryReadPoint(d, ref i, cur, relative, out Vector2 c2)
                            || !TryReadPoint(d, ref i, cur, relative, out Vector2 end)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.SmoothCubicTo(c2, end);
                        break;
                    }
                    case 'Q': {
                        if (!TryReadPoint(d, ref i, cur, relative, out Vector2 ctrl)
                            || !TryReadPoint(d, ref i, cur, relative, out Vector2 end)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.QuadTo(ctrl, end);
                        break;
                    }
                    case 'T': {
                        if (!TryReadPoint(d, ref i, cur, relative, out Vector2 end)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.SmoothQuadTo(end);
                        break;
                    }
                    case 'A': {
                        if (!TryReadNumber(d, ref i, out float rx)
                            || !TryReadNumber(d, ref i, out float ry)
                            || !TryReadNumber(d, ref i, out float rotDeg)
                            || !TryReadFlag(d, ref i, out bool largeArc)
                            || !TryReadFlag(d, ref i, out bool sweep)
                            || !TryReadPoint(d, ref i, cur, relative, out Vector2 end)) {
                            return Fail(command, paramStart, out error);
                        }
                        builder.ArcTo(rx, ry, MathHelper.ToRadians(rotDeg), largeArc, sweep, end);
                        break;
                    }
                    default:
                        error = $"unsupported command '{command}' at {paramStart}";
                        return false;
                }
            }
        }

        private static bool Fail(char command, int at, out string error) {
            error = $"malformed parameters for '{command}' at {at}";
            return false;
        }

        private static bool IsCommand(char c) => c switch {
            'M' or 'm' or 'Z' or 'z' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v'
            or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't' or 'A' or 'a' => true,
            _ => false,
        };

        private static bool IsNumberStart(char c) => char.IsDigit(c) || c == '-' || c == '+' || c == '.';

        private static void SkipSeparators(string d, ref int i) {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ',')) {
                i++;
            }
        }

        private static bool TryReadPoint(string d, ref int i, Vector2 cursor, bool relative, out Vector2 point) {
            point = Vector2.Zero;
            if (!TryReadNumber(d, ref i, out float x) || !TryReadNumber(d, ref i, out float y)) {
                return false;
            }
            point = relative ? cursor + new Vector2(x, y) : new Vector2(x, y);
            return true;
        }

        //标志位是单个 0 / 1 字符，允许紧贴后续数字
        private static bool TryReadFlag(string d, ref int i, out bool flag) {
            SkipSeparators(d, ref i);
            flag = false;
            if (i >= d.Length) {
                return false;
            }
            char c = d[i];
            if (c != '0' && c != '1') {
                return false;
            }
            flag = c == '1';
            i++;
            return true;
        }

        //数字：[+-]? (digits [. digits?] | . digits) ([eE] [+-]? digits)?
        private static bool TryReadNumber(string d, ref int i, out float value) {
            value = 0f;
            SkipSeparators(d, ref i);
            int begin = i;
            if (i < d.Length && (d[i] == '+' || d[i] == '-')) {
                i++;
            }
            int intDigits = 0;
            while (i < d.Length && char.IsDigit(d[i])) {
                i++;
                intDigits++;
            }
            int fracDigits = 0;
            if (i < d.Length && d[i] == '.') {
                int dot = i;
                i++;
                while (i < d.Length && char.IsDigit(d[i])) {
                    i++;
                    fracDigits++;
                }
                if (intDigits == 0 && fracDigits == 0) {
                    //孤立的小数点不是数字
                    i = dot;
                }
            }
            if (intDigits == 0 && fracDigits == 0) {
                i = begin;
                return false;
            }
            if (i < d.Length && (d[i] == 'e' || d[i] == 'E')) {
                int expStart = i;
                i++;
                if (i < d.Length && (d[i] == '+' || d[i] == '-')) {
                    i++;
                }
                int expDigits = 0;
                while (i < d.Length && char.IsDigit(d[i])) {
                    i++;
                    expDigits++;
                }
                if (expDigits == 0) {
                    //"1e" 后面没有数字：把 e 还回去，让上层把它当作非法字符报错
                    i = expStart;
                }
            }
            return float.TryParse(d.AsSpan(begin, i - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
