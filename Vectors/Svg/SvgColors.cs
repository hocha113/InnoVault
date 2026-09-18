using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace InnoVault.Vectors.Svg
{
    /// <summary>
    /// SVG / CSS 颜色字面量解析：<c>#rgb #rgba #rrggbb #rrggbbaa rgb() rgba() none transparent currentColor</c> 与全部 CSS 命名色
    /// </summary>
    public static class SvgColors
    {
        private static readonly Dictionary<string, uint> named = new(StringComparer.OrdinalIgnoreCase) {
            ["aliceblue"] = 0xF0F8FF, ["antiquewhite"] = 0xFAEBD7, ["aqua"] = 0x00FFFF, ["aquamarine"] = 0x7FFFD4, ["azure"] = 0xF0FFFF,
            ["beige"] = 0xF5F5DC, ["bisque"] = 0xFFE4C4, ["black"] = 0x000000, ["blanchedalmond"] = 0xFFEBCD, ["blue"] = 0x0000FF,
            ["blueviolet"] = 0x8A2BE2, ["brown"] = 0xA52A2A, ["burlywood"] = 0xDEB887, ["cadetblue"] = 0x5F9EA0, ["chartreuse"] = 0x7FFF00,
            ["chocolate"] = 0xD2691E, ["coral"] = 0xFF7F50, ["cornflowerblue"] = 0x6495ED, ["cornsilk"] = 0xFFF8DC, ["crimson"] = 0xDC143C,
            ["cyan"] = 0x00FFFF, ["darkblue"] = 0x00008B, ["darkcyan"] = 0x008B8B, ["darkgoldenrod"] = 0xB8860B, ["darkgray"] = 0xA9A9A9,
            ["darkgreen"] = 0x006400, ["darkgrey"] = 0xA9A9A9, ["darkkhaki"] = 0xBDB76B, ["darkmagenta"] = 0x8B008B, ["darkolivegreen"] = 0x556B2F,
            ["darkorange"] = 0xFF8C00, ["darkorchid"] = 0x9932CC, ["darkred"] = 0x8B0000, ["darksalmon"] = 0xE9967A, ["darkseagreen"] = 0x8FBC8F,
            ["darkslateblue"] = 0x483D8B, ["darkslategray"] = 0x2F4F4F, ["darkslategrey"] = 0x2F4F4F, ["darkturquoise"] = 0x00CED1, ["darkviolet"] = 0x9400D3,
            ["deeppink"] = 0xFF1493, ["deepskyblue"] = 0x00BFFF, ["dimgray"] = 0x696969, ["dimgrey"] = 0x696969, ["dodgerblue"] = 0x1E90FF,
            ["firebrick"] = 0xB22222, ["floralwhite"] = 0xFFFAF0, ["forestgreen"] = 0x228B22, ["fuchsia"] = 0xFF00FF, ["gainsboro"] = 0xDCDCDC,
            ["ghostwhite"] = 0xF8F8FF, ["gold"] = 0xFFD700, ["goldenrod"] = 0xDAA520, ["gray"] = 0x808080, ["green"] = 0x008000,
            ["greenyellow"] = 0xADFF2F, ["grey"] = 0x808080, ["honeydew"] = 0xF0FFF0, ["hotpink"] = 0xFF69B4, ["indianred"] = 0xCD5C5C,
            ["indigo"] = 0x4B0082, ["ivory"] = 0xFFFFF0, ["khaki"] = 0xF0E68C, ["lavender"] = 0xE6E6FA, ["lavenderblush"] = 0xFFF0F5,
            ["lawngreen"] = 0x7CFC00, ["lemonchiffon"] = 0xFFFACD, ["lightblue"] = 0xADD8E6, ["lightcoral"] = 0xF08080, ["lightcyan"] = 0xE0FFFF,
            ["lightgoldenrodyellow"] = 0xFAFAD2, ["lightgray"] = 0xD3D3D3, ["lightgreen"] = 0x90EE90, ["lightgrey"] = 0xD3D3D3, ["lightpink"] = 0xFFB6C1,
            ["lightsalmon"] = 0xFFA07A, ["lightseagreen"] = 0x20B2AA, ["lightskyblue"] = 0x87CEFA, ["lightslategray"] = 0x778899, ["lightslategrey"] = 0x778899,
            ["lightsteelblue"] = 0xB0C4DE, ["lightyellow"] = 0xFFFFE0, ["lime"] = 0x00FF00, ["limegreen"] = 0x32CD32, ["linen"] = 0xFAF0E6,
            ["magenta"] = 0xFF00FF, ["maroon"] = 0x800000, ["mediumaquamarine"] = 0x66CDAA, ["mediumblue"] = 0x0000CD, ["mediumorchid"] = 0xBA55D3,
            ["mediumpurple"] = 0x9370DB, ["mediumseagreen"] = 0x3CB371, ["mediumslateblue"] = 0x7B68EE, ["mediumspringgreen"] = 0x00FA9A, ["mediumturquoise"] = 0x48D1CC,
            ["mediumvioletred"] = 0xC71585, ["midnightblue"] = 0x191970, ["mintcream"] = 0xF5FFFA, ["mistyrose"] = 0xFFE4E1, ["moccasin"] = 0xFFE4B5,
            ["navajowhite"] = 0xFFDEAD, ["navy"] = 0x000080, ["oldlace"] = 0xFDF5E6, ["olive"] = 0x808000, ["olivedrab"] = 0x6B8E23,
            ["orange"] = 0xFFA500, ["orangered"] = 0xFF4500, ["orchid"] = 0xDA70D6, ["palegoldenrod"] = 0xEEE8AA, ["palegreen"] = 0x98FB98,
            ["paleturquoise"] = 0xAFEEEE, ["palevioletred"] = 0xDB7093, ["papayawhip"] = 0xFFEFD5, ["peachpuff"] = 0xFFDAB9, ["peru"] = 0xCD853F,
            ["pink"] = 0xFFC0CB, ["plum"] = 0xDDA0DD, ["powderblue"] = 0xB0E0E6, ["purple"] = 0x800080, ["rebeccapurple"] = 0x663399,
            ["red"] = 0xFF0000, ["rosybrown"] = 0xBC8F8F, ["royalblue"] = 0x4169E1, ["saddlebrown"] = 0x8B4513, ["salmon"] = 0xFA8072,
            ["sandybrown"] = 0xF4A460, ["seagreen"] = 0x2E8B57, ["seashell"] = 0xFFF5EE, ["sienna"] = 0xA0522D, ["silver"] = 0xC0C0C0,
            ["skyblue"] = 0x87CEEB, ["slateblue"] = 0x6A5ACD, ["slategray"] = 0x708090, ["slategrey"] = 0x708090, ["snow"] = 0xFFFAFA,
            ["springgreen"] = 0x00FF7F, ["steelblue"] = 0x4682B4, ["tan"] = 0xD2B48C, ["teal"] = 0x008080, ["thistle"] = 0xD8BFD8,
            ["tomato"] = 0xFF6347, ["turquoise"] = 0x40E0D0, ["violet"] = 0xEE82EE, ["wheat"] = 0xF5DEB3, ["white"] = 0xFFFFFF,
            ["whitesmoke"] = 0xF5F5F5, ["yellow"] = 0xFFFF00, ["yellowgreen"] = 0x9ACD32,
        };

        /// <summary>
        /// 解析颜色字面量
        /// </summary>
        /// <param name="text">颜色文本</param>
        /// <param name="color">解析结果；<c>none</c> / <c>transparent</c> 时为 <see cref="Color.Transparent"/></param>
        /// <param name="none">是否为 <c>none</c>（表示不画）</param>
        /// <param name="currentColor">遇到 <c>currentColor</c> 时使用的颜色</param>
        /// <returns>语法可识别返回 true</returns>
        public static bool TryParse(string text, out Color color, out bool none, Color? currentColor = null) {
            color = Color.White;
            none = false;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }
            string s = text.Trim();
            if (s.Equals("none", StringComparison.OrdinalIgnoreCase)) {
                none = true;
                color = Color.Transparent;
                return true;
            }
            if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase)) {
                color = Color.Transparent;
                return true;
            }
            if (s.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) {
                color = currentColor ?? Color.Black;
                return true;
            }
            if (s[0] == '#') {
                return TryParseHex(s.AsSpan(1), out color);
            }
            if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) {
                return TryParseRgb(s, out color);
            }
            if (named.TryGetValue(s, out uint rgb)) {
                color = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return true;
            }
            return false;
        }

        private static bool TryParseHex(ReadOnlySpan<char> hex, out Color color) {
            color = Color.White;
            int r, g, b, a = 255;
            switch (hex.Length) {
                case 3:
                case 4:
                    if (!Nibble(hex[0], out r) || !Nibble(hex[1], out g) || !Nibble(hex[2], out b)) {
                        return false;
                    }
                    r *= 17;
                    g *= 17;
                    b *= 17;
                    if (hex.Length == 4) {
                        if (!Nibble(hex[3], out a)) {
                            return false;
                        }
                        a *= 17;
                    }
                    break;
                case 6:
                case 8:
                    if (!Byte(hex.Slice(0, 2), out r) || !Byte(hex.Slice(2, 2), out g) || !Byte(hex.Slice(4, 2), out b)) {
                        return false;
                    }
                    if (hex.Length == 8 && !Byte(hex.Slice(6, 2), out a)) {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
            color = new Color(r, g, b, a);
            return true;
        }

        private static bool Nibble(char c, out int v) {
            v = c switch {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            return v >= 0;
        }

        private static bool Byte(ReadOnlySpan<char> two, out int v) {
            v = 0;
            if (!Nibble(two[0], out int hi) || !Nibble(two[1], out int lo)) {
                return false;
            }
            v = hi * 16 + lo;
            return true;
        }

        //rgb(r, g, b) / rgba(r, g, b, a) / rgb(r g b / a)，分量可为 0~255 或百分比，a 为 0~1 或百分比
        private static bool TryParseRgb(string s, out Color color) {
            color = Color.White;
            int open = s.IndexOf('(');
            int close = s.LastIndexOf(')');
            if (open < 0 || close <= open) {
                return false;
            }
            string[] parts = s.Substring(open + 1, close - open - 1).Split([',', ' ', '/', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) {
                return false;
            }
            if (!Component(parts[0], 255f, out float r) || !Component(parts[1], 255f, out float g) || !Component(parts[2], 255f, out float b)) {
                return false;
            }
            float a = 255f;
            if (parts.Length >= 4 && !Component(parts[3], 1f, out a)) {
                return false;
            }
            if (parts.Length >= 4) {
                a *= 255f;
            }
            color = new Color((int)MathF.Round(r), (int)MathF.Round(g), (int)MathF.Round(b), (int)MathF.Round(a));
            return true;
        }

        private static bool Component(string text, float full, out float value) {
            value = 0f;
            text = text.Trim();
            bool percent = text.EndsWith('%');
            if (percent) {
                text = text[..^1];
            }
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) {
                return false;
            }
            value = percent ? v / 100f * full : v;
            value = MathHelper.Clamp(value, 0f, full);
            return true;
        }
    }
}
