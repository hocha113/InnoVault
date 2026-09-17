using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 骨架 JSON 的模板展开器：在 <see cref="Rig2DJson.Parse(JObject, string)"/> 读取之前把 <c>vars</c> / <c>repeat</c> / <c>{表达式}</c>
    /// 展开成平铺 JSON，下游读取逻辑不感知模板。多肢骨架（十条腿、四条弧链）靠它在数据文件里只写一遍
    /// <br/>语法：
    /// <list type="bullet">
    /// <item>根级 <c>"vars": { "hipSide": 11, "reach": "{coxa+femur}" }</c>：全局常量（可引用先声明的常量），所有表达式可用；展开后从根移除</item>
    /// <item>任意数组里带 <c>"repeat"</c> 键的对象是模板块，展开后原地拼接进父数组：
    /// <c>{ "repeat": 10, "var": "i", "start": 0, "values": { "flank": [1, -1, …] }, "items": [ … ] }</c>；
    /// <c>values</c> 每项是数组（长度不足 count 报错）或标量（广播）；<c>repeat</c> 也可写成 <c>{ "count", "var", "start", "values" }</c> 对象</item>
    /// <item>占位符 <c>{expr}</c> 只替换 JSON 值、不替换键：整串是一个占位符时替换成带类型的值（整数 / 浮点 / 字串 / 布尔），
    /// 混在文字里时做文本替换（<c>"coxa{i}"</c>）；<c>{{</c> / <c>}}</c> 写字面花括号</item>
    /// <item>表达式：数字、变量、<c>+ - * / % //</c>（<c>//</c> 整除）、一元负号、括号；全整数且无 <c>/</c> 时结果为整数</item>
    /// <item>模板块可嵌套，作用域链式继承（内层可写 <c>{i*7+k}</c>）；展开后仍引用未定义变量的占位符报错并定位到所在字串</item>
    /// </list>
    /// 失败不抛：返回 <see langword="false"/> 与错误文本，调用方记日志。导出（<see cref="Rig2DJson.ToJson"/>）只写平铺快照，不还原模板
    /// </summary>
    public static class Rig2DJsonTemplate
    {
        /// <summary>
        /// 单次展开允许的最大条目数（防手滑写出巨量克隆）
        /// </summary>
        public const int MaxRepeatCount = 4096;

        private sealed class TemplateException(string message) : Exception(message)
        {
        }

        /// <summary>
        /// 变量作用域：查不到就沿父链找
        /// </summary>
        private sealed class Scope(Scope parent)
        {
            private readonly Scope parentScope = parent;
            private readonly Dictionary<string, object> vars = new(StringComparer.Ordinal);

            public void Set(string name, object value) => vars[name] = value;

            public bool TryGet(string name, out object value) {
                for (Scope s = this; s != null; s = s.parentScope) {
                    if (s.vars.TryGetValue(name, out value)) {
                        return true;
                    }
                }
                value = null;
                return false;
            }
        }

        /// <summary>
        /// 原地展开 <paramref name="root"/>；成功返回 <see langword="true"/>。失败时 <paramref name="error"/> 给出原因，
        /// <paramref name="root"/> 可能处于半展开状态，调用方不应再读它
        /// </summary>
        public static bool Expand(JObject root, out string error) {
            error = null;
            if (root == null) {
                return true;
            }
            try {
                Scope scope = new(null);
                if (root["vars"] is JObject vars) {
                    foreach (JProperty prop in vars.Properties()) {
                        scope.Set(prop.Name, ToScopeValue(SubstituteIfString(prop.Value, scope, $"vars.{prop.Name}"), $"vars.{prop.Name}"));
                    }
                    root.Remove("vars");
                }
                else if (root["vars"] != null) {
                    throw new TemplateException("'vars' must be an object");
                }
                foreach (JProperty prop in new List<JProperty>(root.Properties())) {
                    ExpandProperty(prop, scope);
                }
                return true;
            } catch (TemplateException ex) {
                error = ex.Message;
                return false;
            } catch (Exception ex) {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        //==================== 遍历 ====================

        private static void ExpandProperty(JProperty prop, Scope scope) {
            JToken value = prop.Value;
            switch (value) {
                case JArray arr:
                    ExpandArray(arr, scope);
                    break;
                case JObject obj:
                    if (obj["repeat"] != null) {
                        throw new TemplateException($"repeat block must be an array element (found under property '{prop.Name}')");
                    }
                    ExpandObject(obj, scope);
                    break;
                case JValue { Type: JTokenType.String } sv:
                    JToken replaced = Substitute(sv, scope);
                    if (!ReferenceEquals(replaced, sv)) {
                        prop.Value = replaced;
                    }
                    break;
            }
        }

        private static void ExpandObject(JObject obj, Scope scope) {
            foreach (JProperty prop in new List<JProperty>(obj.Properties())) {
                ExpandProperty(prop, scope);
            }
        }

        private static void ExpandArray(JArray arr, Scope scope) {
            int i = 0;
            while (i < arr.Count) {
                JToken elem = arr[i];
                if (elem is JObject block && block["repeat"] != null) {
                    List<JToken> expanded = ExpandRepeat(block, scope);
                    arr.RemoveAt(i);
                    for (int k = 0; k < expanded.Count; k++) {
                        arr.Insert(i + k, expanded[k]);
                    }
                    i += expanded.Count;
                    continue;
                }
                ExpandElement(arr, i, scope);
                i++;
            }
        }

        private static void ExpandElement(JArray arr, int index, Scope scope) {
            JToken elem = arr[index];
            switch (elem) {
                case JArray inner:
                    ExpandArray(inner, scope);
                    break;
                case JObject obj:
                    ExpandObject(obj, scope);
                    break;
                case JValue { Type: JTokenType.String } sv: {
                    JToken replaced = Substitute(sv, scope);
                    if (!ReferenceEquals(replaced, sv)) {
                        arr[index] = replaced;
                    }
                    break;
                }
            }
        }

        //==================== repeat ====================

        private static List<JToken> ExpandRepeat(JObject block, Scope outer) {
            JToken rep = block["repeat"];
            JToken countToken;
            string varName;
            JToken startToken;
            JObject values;
            if (rep is JObject repObj) {
                countToken = repObj["count"];
                varName = repObj["var"]?.Type == JTokenType.String ? (string)repObj["var"] : null;
                startToken = repObj["start"];
                values = repObj["values"] as JObject;
            }
            else {
                countToken = rep;
                varName = block["var"]?.Type == JTokenType.String ? (string)block["var"] : null;
                startToken = block["start"];
                values = block["values"] as JObject;
            }
            varName ??= "i";
            if (block["items"] is not JArray items) {
                throw new TemplateException($"repeat block (var '{varName}') needs an 'items' array");
            }

            int count = ToInt(Resolve(countToken, outer, "repeat count"), "repeat count");
            if (count < 0 || count > MaxRepeatCount) {
                throw new TemplateException($"repeat count {count} out of range 0..{MaxRepeatCount}");
            }
            int start = startToken == null ? 0 : ToInt(Resolve(startToken, outer, "repeat start"), "repeat start");

            List<(string Name, JToken Source)> valueSources = [];
            if (values != null) {
                foreach (JProperty prop in values.Properties()) {
                    if (prop.Value is JArray va) {
                        if (va.Count < count) {
                            throw new TemplateException($"values '{prop.Name}' has {va.Count} entries, repeat count is {count}");
                        }
                    }
                    else if (prop.Value is not JValue) {
                        throw new TemplateException($"values '{prop.Name}' must be an array or a scalar");
                    }
                    valueSources.Add((prop.Name, prop.Value));
                }
            }

            List<JToken> result = new(count * items.Count);
            for (int idx = 0; idx < count; idx++) {
                Scope scope = new(outer);
                scope.Set(varName, (long)(start + idx));
                foreach ((string name, JToken source) in valueSources) {
                    JToken raw = source is JArray va ? va[idx] : source;
                    scope.Set(name, ToScopeValue(SubstituteIfString(raw, scope, $"values.{name}[{idx}]"), $"values.{name}[{idx}]"));
                }
                foreach (JToken item in items) {
                    JToken clone = item.DeepClone();
                    if (clone is JObject nested && nested["repeat"] != null) {
                        result.AddRange(ExpandRepeat(nested, scope));
                        continue;
                    }
                    switch (clone) {
                        case JArray ca:
                            ExpandArray(ca, scope);
                            result.Add(ca);
                            break;
                        case JObject co:
                            ExpandObject(co, scope);
                            result.Add(co);
                            break;
                        case JValue { Type: JTokenType.String } sv:
                            result.Add(Substitute(sv, scope));
                            break;
                        default:
                            result.Add(clone);
                            break;
                    }
                }
            }
            return result;
        }

        //==================== 占位符替换 ====================

        private static JToken SubstituteIfString(JToken token, Scope scope, string where) {
            if (token is JValue { Type: JTokenType.String } sv) {
                return Substitute(sv, scope);
            }
            if (token is JArray or JObject) {
                throw new TemplateException($"{where} must be a scalar");
            }
            return token;
        }

        private static object Resolve(JToken token, Scope scope, string where) {
            if (token == null) {
                throw new TemplateException($"{where} is missing");
            }
            return ToScopeValue(SubstituteIfString(token, scope, where), where);
        }

        /// <summary>
        /// 把字串里的 <c>{expr}</c> 全部替换；整串就是一个占位符时返回带类型的 <see cref="JValue"/>，否则返回字串；无占位符时返回原对象
        /// </summary>
        private static JToken Substitute(JValue value, Scope scope) {
            string s = (string)value;
            if (string.IsNullOrEmpty(s) || (s.IndexOf('{') < 0 && s.IndexOf('}') < 0)) {
                return value;
            }
            StringBuilder text = new();
            object soleValue = null;
            int placeholders = 0;
            bool literalText = false;
            int i = 0;
            while (i < s.Length) {
                char c = s[i];
                if (c == '{') {
                    if (i + 1 < s.Length && s[i + 1] == '{') {
                        text.Append('{');
                        literalText = true;
                        i += 2;
                        continue;
                    }
                    int close = s.IndexOf('}', i + 1);
                    if (close < 0) {
                        throw new TemplateException($"unterminated '{{' in \"{s}\"");
                    }
                    string expr = s.Substring(i + 1, close - i - 1);
                    object v = Evaluate(expr, scope, s);
                    placeholders++;
                    soleValue = v;
                    text.Append(FormatValue(v));
                    i = close + 1;
                    continue;
                }
                if (c == '}') {
                    if (i + 1 < s.Length && s[i + 1] == '}') {
                        text.Append('}');
                        literalText = true;
                        i += 2;
                        continue;
                    }
                    throw new TemplateException($"stray '}}' in \"{s}\"");
                }
                text.Append(c);
                literalText = true;
                i++;
            }
            if (placeholders == 1 && !literalText) {
                return ToJValue(soleValue);
            }
            return new JValue(text.ToString());
        }

        private static JValue ToJValue(object v) => v switch {
            long l => new JValue(l),
            double d => new JValue(d),
            bool b => new JValue(b),
            string str => new JValue(str),
            _ => new JValue(v?.ToString() ?? string.Empty),
        };

        private static string FormatValue(object v) => v switch {
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            string str => str,
            _ => v?.ToString() ?? string.Empty,
        };

        private static object ToScopeValue(JToken token, string where) {
            if (token is not JValue jv) {
                throw new TemplateException($"{where} must be a scalar");
            }
            return jv.Type switch {
                JTokenType.Integer => Convert.ToInt64(jv.Value, CultureInfo.InvariantCulture),
                JTokenType.Float => Convert.ToDouble(jv.Value, CultureInfo.InvariantCulture),
                JTokenType.Boolean => (bool)jv,
                JTokenType.String => (string)jv,
                JTokenType.Null => throw new TemplateException($"{where} is null"),
                _ => jv.Value,
            };
        }

        private static int ToInt(object v, string where) => v switch {
            long l => checked((int)l),
            double d when Math.Abs(d - Math.Round(d)) < 1e-9 => (int)Math.Round(d),
            _ => throw new TemplateException($"{where} must be an integer, got {FormatValue(v)}"),
        };

        //==================== 表达式 ====================

        /// <summary>
        /// 递归下降求值：expr := term (('+'|'-') term)*；term := unary (('*'|'/'|'//'|'%') unary)*；unary := '-' unary | primary；
        /// primary := number | ident | '(' expr ')'
        /// </summary>
        private static object Evaluate(string expr, Scope scope, string context) {
            ExprParser p = new(expr, scope, context);
            object v = p.ParseExpr();
            p.ExpectEnd();
            return v;
        }

        private sealed class ExprParser(string src, Scope scope, string context)
        {
            private int pos;

            private TemplateException Error(string msg) => new($"{msg} in expression '{src}' (\"{context}\")");

            private void SkipSpaces() {
                while (pos < src.Length && char.IsWhiteSpace(src[pos])) {
                    pos++;
                }
            }

            private bool Peek(char c) {
                SkipSpaces();
                return pos < src.Length && src[pos] == c;
            }

            private bool Peek2(string s) {
                SkipSpaces();
                return pos + 1 < src.Length && src[pos] == s[0] && src[pos + 1] == s[1];
            }

            public void ExpectEnd() {
                SkipSpaces();
                if (pos < src.Length) {
                    throw Error($"unexpected '{src[pos]}'");
                }
            }

            public object ParseExpr() {
                object left = ParseTerm();
                while (true) {
                    if (Peek('+')) {
                        pos++;
                        left = Arith(left, ParseTerm(), '+');
                    }
                    else if (Peek('-')) {
                        pos++;
                        left = Arith(left, ParseTerm(), '-');
                    }
                    else {
                        return left;
                    }
                }
            }

            private object ParseTerm() {
                object left = ParseUnary();
                while (true) {
                    if (Peek2("//")) {
                        pos += 2;
                        left = Arith(left, ParseUnary(), 'i');
                    }
                    else if (Peek('*')) {
                        pos++;
                        left = Arith(left, ParseUnary(), '*');
                    }
                    else if (Peek('/')) {
                        pos++;
                        left = Arith(left, ParseUnary(), '/');
                    }
                    else if (Peek('%')) {
                        pos++;
                        left = Arith(left, ParseUnary(), '%');
                    }
                    else {
                        return left;
                    }
                }
            }

            private object ParseUnary() {
                if (Peek('-')) {
                    pos++;
                    object v = ParseUnary();
                    return v switch {
                        long l => -l,
                        double d => -d,
                        _ => throw Error("unary '-' needs a number"),
                    };
                }
                return ParsePrimary();
            }

            private object ParsePrimary() {
                SkipSpaces();
                if (pos >= src.Length) {
                    throw Error("unexpected end");
                }
                char c = src[pos];
                if (c == '(') {
                    pos++;
                    object v = ParseExpr();
                    if (!Peek(')')) {
                        throw Error("missing ')'");
                    }
                    pos++;
                    return v;
                }
                if (char.IsDigit(c) || c == '.') {
                    int startPos = pos;
                    bool isFloat = false;
                    while (pos < src.Length && (char.IsDigit(src[pos]) || src[pos] == '.')) {
                        isFloat |= src[pos] == '.';
                        pos++;
                    }
                    string num = src[startPos..pos];
                    if (isFloat) {
                        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) {
                            throw Error($"bad number '{num}'");
                        }
                        return d;
                    }
                    if (!long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) {
                        throw Error($"bad number '{num}'");
                    }
                    return l;
                }
                if (char.IsLetter(c) || c == '_') {
                    int startPos = pos;
                    while (pos < src.Length && (char.IsLetterOrDigit(src[pos]) || src[pos] == '_')) {
                        pos++;
                    }
                    string name = src[startPos..pos];
                    if (!scope.TryGet(name, out object value)) {
                        throw Error($"undefined variable '{name}'");
                    }
                    return value;
                }
                throw Error($"unexpected '{c}'");
            }

            private object Arith(object a, object b, char op) {
                if (a is string or bool || b is string or bool) {
                    throw Error("arithmetic on a non-numeric value");
                }
                if (a is long la && b is long lb && op != '/') {
                    switch (op) {
                        case '+':
                            return la + lb;
                        case '-':
                            return la - lb;
                        case '*':
                            return la * lb;
                        case '%':
                            if (lb == 0) {
                                throw Error("modulo by zero");
                            }
                            return la % lb;
                        default://整除
                            if (lb == 0) {
                                throw Error("division by zero");
                            }
                            return (long)Math.Floor(la / (double)lb);
                    }
                }
                double da = a is long al ? al : (double)a;
                double db = b is long bl ? bl : (double)b;
                switch (op) {
                    case '+':
                        return da + db;
                    case '-':
                        return da - db;
                    case '*':
                        return da * db;
                    case '/':
                        if (db == 0d) {
                            throw Error("division by zero");
                        }
                        return da / db;
                    case '%':
                        if (db == 0d) {
                            throw Error("modulo by zero");
                        }
                        return da % db;
                    default:
                        if (db == 0d) {
                            throw Error("division by zero");
                        }
                        return Math.Floor(da / db);
                }
            }
        }
    }
}
