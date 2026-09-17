using InnoVault.Rigs2D.Animation;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 骨架定义的 JSON 读写
    /// <br/>格式概要（全部键可省略，缺省即默认值；角度键优先读弧度，<c>xxxDeg</c> 后缀读角度）：
    /// <code>
    /// {
    ///   "name": "SeaShrimp", "snapDistance": 340,
    ///   "bones":  [ { "name", "parent", "offset": [x, y], "atTip", "rotation" | "rotationDeg", "inheritRotation", "length" } ],
    ///   "pieces": [ { "name", "bone", "texture", "proximal": [x, y], "axis" | "axisDeg" | "distal": [x, y], "axisLength",
    ///                 "stretch": "none|axis|uniform", "stretchMin", "stretchMax", "layer", "mirror", "scale", "dark", "tint", "alpha", "visible", "frames", "framePad", "proximalNormalized" } ],
    ///   "solvers": [ { "name", "type", "bones": [ ... ], ...其余键都是该求解器的参数 } ],
    ///   "ribbons": [ { "name", "bones": [ ... ], "texture", "width", "widthEnd", "widthProfile": [ ... ], "uv": "stretch|tile", "tileLength",
    ///                 "includeTip", "smooth", "layer", "tint", "dark", "alpha", "additive", "visible" } ],
    ///   "clips":   [ { "name", "duration", "loop", "tracks": [ { "bone", "interp": "linear|step", "rotation": [[t, v]], "offset": [[t, [x, y]]], "length": [[t, v]] } ] } ]
    /// }
    /// </code>
    /// 读取遵循框架约定：格式错误不抛异常，记日志并返回 <see langword="null"/>
    /// </summary>
    public static class Rig2DJson
    {
        /// <summary>
        /// 从 JSON 文本解析定义；失败返回 <see langword="null"/>（已记日志）。返回值尚未 <see cref="Rig2DDefinition.Resolve"/>
        /// </summary>
        public static Rig2DDefinition Parse(string json, string sourceHint = null) {
            if (string.IsNullOrWhiteSpace(json)) {
                VaultMod.LoggerError($"[Rig2DJson:{sourceHint}]", "empty json text");
                return null;
            }
            JObject root;
            try {
                root = JObject.Parse(json);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Rig2DJson:{sourceHint}]", $"json parse failed: {ex.Message}");
                return null;
            }
            return Parse(root, sourceHint);
        }

        /// <summary>
        /// 从已解析的 <see cref="JObject"/> 读取定义；失败返回 <see langword="null"/>
        /// </summary>
        public static Rig2DDefinition Parse(JObject root, string sourceHint = null) {
            if (root == null) {
                return null;
            }
            try {
                Rig2DDefinition def = new() {
                    Name = Str(root, "name", string.Empty),
                    SnapDistance = Num(root, "snapDistance", 340f),
                };

                if (root["bones"] is JArray bones) {
                    foreach (JToken t in bones) {
                        if (t is JObject o) {
                            def.Bones.Add(ReadBone(o));
                        }
                    }
                }
                if (root["pieces"] is JArray pieces) {
                    foreach (JToken t in pieces) {
                        if (t is JObject o) {
                            def.Pieces.Add(ReadPiece(o));
                        }
                    }
                }
                if (root["solvers"] is JArray solvers) {
                    foreach (JToken t in solvers) {
                        if (t is JObject o) {
                            def.Solvers.Add(ReadSolver(o));
                        }
                    }
                }
                if (root["ribbons"] is JArray ribbons) {
                    foreach (JToken t in ribbons) {
                        if (t is JObject o) {
                            def.Ribbons.Add(ReadRibbon(o));
                        }
                    }
                }
                if (root["clips"] is JArray clips) {
                    foreach (JToken t in clips) {
                        if (t is JObject o) {
                            Rig2DClip clip = ReadClip(o);
                            if (clip != null) {
                                def.Clips.Add(clip);
                            }
                        }
                    }
                }
                return def;
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Rig2DJson:{sourceHint}]", $"definition read failed: {ex.Message}");
                return null;
            }
        }

        private static Bone2DDef ReadBone(JObject o) => new() {
            Name = Str(o, "name", string.Empty),
            Parent = Str(o, "parent", null),
            Offset = ReadVector2(o["offset"], Vector2.Zero),
            AtParentTip = Bool(o, "atTip", false),
            Rotation = Angle(o, "rotation", 0f),
            InheritRotation = Bool(o, "inheritRotation", true),
            Length = Num(o, "length", 0f),
        };

        private static Piece2DDef ReadPiece(JObject o) {
            Piece2DDef p = new() {
                Name = Str(o, "name", null),
                Bone = Str(o, "bone", string.Empty),
                Texture = Str(o, "texture", string.Empty),
                Proximal = ReadVector2(o["proximal"], Vector2.Zero),
                AxisLength = Num(o, "axisLength", 0f),
                StretchMin = Num(o, "stretchMin", 0f),
                StretchMax = Num(o, "stretchMax", 0f),
                Layer = (int)Num(o, "layer", 0f),
                Mirror = Bool(o, "mirror", false),
                Dark = Num(o, "dark", 1f),
                Alpha = Num(o, "alpha", 1f),
                Visible = Bool(o, "visible", true),
                Frames = Math.Max(1, (int)Num(o, "frames", 1f)),
                FramePad = (int)Num(o, "framePad", 0f),
                ProximalNormalized = Bool(o, "proximalNormalized", false),
                Tint = ReadColor(o["tint"], Color.White),
            };
            if (o["distal"] != null) {
                p.SetDistal(ReadVector2(o["distal"], p.Proximal));
                if (o["axisLength"] != null) {
                    p.AxisLength = Num(o, "axisLength", p.AxisLength);
                }
            }
            else {
                p.Axis = Angle(o, "axis", 0f);
            }
            JToken scale = o["scale"];
            if (scale != null) {
                p.Scale = scale.Type is JTokenType.Float or JTokenType.Integer
                    ? new Vector2((float)scale)
                    : ReadVector2(scale, Vector2.One);
            }
            string stretch = Str(o, "stretch", "none");
            p.Stretch = stretch.ToLowerInvariant() switch {
                "axis" => Piece2DStretch.Axis,
                "uniform" => Piece2DStretch.Uniform,
                _ => Piece2DStretch.None,
            };
            return p;
        }

        private static readonly HashSet<string> solverReserved = new(StringComparer.Ordinal) { "name", "type", "bones" };

        private static Solver2DDef ReadSolver(JObject o) {
            Solver2DDef s = new() {
                Name = Str(o, "name", string.Empty),
                Type = Str(o, "type", string.Empty),
                Params = new JObject(),
            };
            if (o["bones"] is JArray arr) {
                foreach (JToken t in arr) {
                    if (t.Type == JTokenType.String) {
                        s.Bones.Add((string)t);
                    }
                }
            }
            else if (o["bones"]?.Type == JTokenType.String) {
                s.Bones.Add((string)o["bones"]);
            }
            foreach (JProperty prop in o.Properties()) {
                if (solverReserved.Contains(prop.Name)) {
                    continue;
                }
                s.Params[prop.Name] = prop.Value.DeepClone();
            }
            return s;
        }

        private static Ribbon2DDef ReadRibbon(JObject o) {
            Ribbon2DDef r = new() {
                Name = Str(o, "name", null),
                Texture = Str(o, "texture", string.Empty),
                Width = Num(o, "width", 16f),
                WidthEnd = Num(o, "widthEnd", float.NaN),
                TileLength = Num(o, "tileLength", 64f),
                IncludeTip = Bool(o, "includeTip", true),
                Smooth = Math.Max(0, (int)Num(o, "smooth", 0f)),
                Layer = (int)Num(o, "layer", 0f),
                Tint = ReadColor(o["tint"], Color.White),
                Dark = Num(o, "dark", 1f),
                Alpha = Num(o, "alpha", 1f),
                Additive = Bool(o, "additive", false),
                Visible = Bool(o, "visible", true),
            };
            if (o["bones"] is JArray arr) {
                foreach (JToken t in arr) {
                    if (t.Type == JTokenType.String) {
                        r.Bones.Add((string)t);
                    }
                }
            }
            else if (o["bones"]?.Type == JTokenType.String) {
                r.Bones.Add((string)o["bones"]);
            }
            if (o["widthProfile"] is JArray profile) {
                List<float> values = [];
                foreach (JToken t in profile) {
                    if (IsNum(t)) {
                        values.Add((float)t);
                    }
                }
                r.WidthProfile = values.ToArray();
            }
            r.Uv = Str(o, "uv", "stretch").ToLowerInvariant() == "tile" ? Ribbon2DUv.Tile : Ribbon2DUv.Stretch;
            return r;
        }

        private static Rig2DClip ReadClip(JObject o) {
            string name = Str(o, "name", null);
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            Rig2DClip clip = new(name, Num(o, "duration", 60f)) {
                Loop = Bool(o, "loop", true),
            };
            if (o["tracks"] is not JArray tracks) {
                return clip;
            }
            foreach (JToken t in tracks) {
                if (t is not JObject to) {
                    continue;
                }
                string bone = Str(to, "bone", null);
                if (string.IsNullOrEmpty(bone)) {
                    continue;
                }
                Rig2DInterpolation interp = Str(to, "interp", "linear").ToLowerInvariant() == "step"
                    ? Rig2DInterpolation.Step : Rig2DInterpolation.Linear;
                Rig2DTrack track = clip.Track(bone);
                track.Interpolation = interp;
                ReadFloatKeys(to["rotation"], track.Rotation, angle: true);
                ReadFloatKeys(to["rotationDeg"], track.Rotation, angle: true, degrees: true);
                ReadFloatKeys(to["length"], track.Length, angle: false);
                if (to["offset"] is JArray offs) {
                    foreach (JToken k in offs) {
                        if (k is JArray pair && pair.Count >= 2) {
                            track.Offset.Add(new Rig2DKey<Vector2>((float)pair[0], ReadVector2(pair[1], Vector2.Zero)));
                        }
                    }
                }
            }
            return clip;
        }

        private static void ReadFloatKeys(JToken token, List<Rig2DKey<float>> sink, bool angle, bool degrees = false) {
            if (token is not JArray arr) {
                return;
            }
            foreach (JToken k in arr) {
                if (k is JArray pair && pair.Count >= 2
                    && pair[0].Type is JTokenType.Float or JTokenType.Integer
                    && pair[1].Type is JTokenType.Float or JTokenType.Integer) {
                    float v = (float)pair[1];
                    if (angle && degrees) {
                        v = MathHelper.ToRadians(v);
                    }
                    sink.Add(new Rig2DKey<float>((float)pair[0], v));
                }
            }
        }

        //==================== 写出 ====================

        /// <summary>
        /// 把定义序列化为 <see cref="JObject"/>（角度以弧度写出，与读取一致）
        /// </summary>
        public static JObject ToJson(Rig2DDefinition def) {
            JObject root = new() {
                ["name"] = def.Name ?? string.Empty,
                ["snapDistance"] = def.SnapDistance,
            };
            JArray bones = [];
            foreach (Bone2DDef b in def.Bones) {
                JObject o = new() { ["name"] = b.Name };
                if (!string.IsNullOrEmpty(b.Parent)) {
                    o["parent"] = b.Parent;
                }
                if (b.Offset != Vector2.Zero) {
                    o["offset"] = Vec(b.Offset);
                }
                if (b.AtParentTip) {
                    o["atTip"] = true;
                }
                if (b.Rotation != 0f) {
                    o["rotation"] = b.Rotation;
                }
                if (!b.InheritRotation) {
                    o["inheritRotation"] = false;
                }
                if (b.Length != 0f) {
                    o["length"] = b.Length;
                }
                bones.Add(o);
            }
            root["bones"] = bones;

            JArray pieces = [];
            foreach (Piece2DDef p in def.Pieces) {
                JObject o = new();
                if (!string.IsNullOrEmpty(p.Name)) {
                    o["name"] = p.Name;
                }
                o["bone"] = p.Bone;
                o["texture"] = p.Texture;
                o["proximal"] = Vec(p.Proximal);
                o["axis"] = p.Axis;
                if (p.AxisLength != 0f) {
                    o["axisLength"] = p.AxisLength;
                }
                if (p.Stretch != Piece2DStretch.None) {
                    o["stretch"] = p.Stretch.ToString().ToLowerInvariant();
                }
                if (p.StretchMin != 0f) {
                    o["stretchMin"] = p.StretchMin;
                }
                if (p.StretchMax != 0f) {
                    o["stretchMax"] = p.StretchMax;
                }
                if (p.Layer != 0) {
                    o["layer"] = p.Layer;
                }
                if (p.Mirror) {
                    o["mirror"] = true;
                }
                if (p.Scale != Vector2.One) {
                    o["scale"] = Vec(p.Scale);
                }
                if (p.Dark != 1f) {
                    o["dark"] = p.Dark;
                }
                if (p.Tint != Color.White) {
                    o["tint"] = new JArray(p.Tint.R, p.Tint.G, p.Tint.B, p.Tint.A);
                }
                if (p.Alpha != 1f) {
                    o["alpha"] = p.Alpha;
                }
                if (!p.Visible) {
                    o["visible"] = false;
                }
                if (p.Frames > 1) {
                    o["frames"] = p.Frames;
                }
                if (p.FramePad != 0) {
                    o["framePad"] = p.FramePad;
                }
                if (p.ProximalNormalized) {
                    o["proximalNormalized"] = true;
                }
                pieces.Add(o);
            }
            root["pieces"] = pieces;

            JArray solvers = [];
            foreach (Solver2DDef s in def.Solvers) {
                JObject o = new() {
                    ["name"] = s.Name,
                    ["type"] = s.Type,
                    ["bones"] = new JArray(s.Bones),
                };
                if (s.Params != null) {
                    foreach (JProperty prop in s.Params.Properties()) {
                        if (!solverReserved.Contains(prop.Name)) {
                            o[prop.Name] = prop.Value.DeepClone();
                        }
                    }
                }
                solvers.Add(o);
            }
            root["solvers"] = solvers;

            if (def.Ribbons.Count > 0) {
                JArray ribbons = [];
                foreach (Ribbon2DDef r in def.Ribbons) {
                    JObject o = new();
                    if (!string.IsNullOrEmpty(r.Name)) {
                        o["name"] = r.Name;
                    }
                    o["bones"] = new JArray(r.Bones);
                    o["texture"] = r.Texture;
                    o["width"] = r.Width;
                    if (!float.IsNaN(r.WidthEnd)) {
                        o["widthEnd"] = r.WidthEnd;
                    }
                    if (r.WidthProfile != null && r.WidthProfile.Length > 0) {
                        o["widthProfile"] = new JArray(r.WidthProfile);
                    }
                    if (r.Uv != Ribbon2DUv.Stretch) {
                        o["uv"] = r.Uv.ToString().ToLowerInvariant();
                    }
                    if (r.TileLength != 64f) {
                        o["tileLength"] = r.TileLength;
                    }
                    if (!r.IncludeTip) {
                        o["includeTip"] = false;
                    }
                    if (r.Smooth != 0) {
                        o["smooth"] = r.Smooth;
                    }
                    if (r.Layer != 0) {
                        o["layer"] = r.Layer;
                    }
                    if (r.Tint != Color.White) {
                        o["tint"] = new JArray(r.Tint.R, r.Tint.G, r.Tint.B, r.Tint.A);
                    }
                    if (r.Dark != 1f) {
                        o["dark"] = r.Dark;
                    }
                    if (r.Alpha != 1f) {
                        o["alpha"] = r.Alpha;
                    }
                    if (r.Additive) {
                        o["additive"] = true;
                    }
                    if (!r.Visible) {
                        o["visible"] = false;
                    }
                    ribbons.Add(o);
                }
                root["ribbons"] = ribbons;
            }

            if (def.Clips.Count > 0) {
                JArray clips = [];
                foreach (Rig2DClip clip in def.Clips) {
                    clips.Add(ClipToJson(clip));
                }
                root["clips"] = clips;
            }
            return root;
        }

        /// <summary>
        /// 把定义序列化为带缩进的 JSON 文本
        /// </summary>
        public static string ToJsonText(Rig2DDefinition def) => ToJson(def).ToString(Formatting.Indented);

        private static JObject ClipToJson(Rig2DClip clip) {
            JObject o = new() {
                ["name"] = clip.Name,
                ["duration"] = clip.Duration,
                ["loop"] = clip.Loop,
            };
            JArray tracks = [];
            foreach (Rig2DTrack track in clip.Tracks) {
                JObject t = new() { ["bone"] = track.BoneName };
                if (track.Interpolation == Rig2DInterpolation.Step) {
                    t["interp"] = "step";
                }
                if (track.Rotation.Count > 0) {
                    JArray arr = [];
                    foreach (Rig2DKey<float> k in track.Rotation) {
                        arr.Add(new JArray(k.Time, k.Value));
                    }
                    t["rotation"] = arr;
                }
                if (track.Length.Count > 0) {
                    JArray arr = [];
                    foreach (Rig2DKey<float> k in track.Length) {
                        arr.Add(new JArray(k.Time, k.Value));
                    }
                    t["length"] = arr;
                }
                if (track.Offset.Count > 0) {
                    JArray arr = [];
                    foreach (Rig2DKey<Vector2> k in track.Offset) {
                        arr.Add(new JArray(k.Time, Vec(k.Value)));
                    }
                    t["offset"] = arr;
                }
                tracks.Add(t);
            }
            o["tracks"] = tracks;
            return o;
        }

        //==================== 基元读取 ====================

        /// <summary>
        /// 读取二维向量：接受 <c>[x, y]</c> 数组或 <c>{ "x": , "y": }</c> 对象，其余返回默认值
        /// </summary>
        public static Vector2 ReadVector2(JToken t, Vector2 fallback) {
            if (t == null) {
                return fallback;
            }
            if (t is JArray arr && arr.Count >= 2 && IsNum(arr[0]) && IsNum(arr[1])) {
                return new Vector2((float)arr[0], (float)arr[1]);
            }
            if (t is JObject o) {
                float x = Num(o, "x", fallback.X);
                float y = Num(o, "y", fallback.Y);
                return new Vector2(x, y);
            }
            return fallback;
        }

        /// <summary>
        /// 读取颜色：接受 <c>[r, g, b]</c> / <c>[r, g, b, a]</c>（0~255）或 <c>"#RRGGBB"</c> / <c>"#RRGGBBAA"</c>
        /// </summary>
        public static Color ReadColor(JToken t, Color fallback) {
            if (t == null) {
                return fallback;
            }
            if (t is JArray arr && arr.Count >= 3) {
                int r = (int)Math.Clamp((float)arr[0], 0f, 255f);
                int g = (int)Math.Clamp((float)arr[1], 0f, 255f);
                int b = (int)Math.Clamp((float)arr[2], 0f, 255f);
                int a = arr.Count >= 4 ? (int)Math.Clamp((float)arr[3], 0f, 255f) : 255;
                return new Color(r, g, b, a);
            }
            if (t.Type == JTokenType.String) {
                string hex = ((string)t).TrimStart('#');
                if (hex.Length is 6 or 8
                    && int.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
                    && int.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
                    && int.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b)) {
                    int a = 255;
                    if (hex.Length == 8 && int.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int pa)) {
                        a = pa;
                    }
                    return new Color(r, g, b, a);
                }
            }
            return fallback;
        }

        private static bool IsNum(JToken t) => t != null && t.Type is JTokenType.Float or JTokenType.Integer;

        private static float Num(JObject o, string key, float fallback) {
            JToken t = o[key];
            return IsNum(t) ? (float)t : fallback;
        }

        private static bool Bool(JObject o, string key, bool fallback) {
            JToken t = o[key];
            return t != null && t.Type == JTokenType.Boolean ? (bool)t : fallback;
        }

        private static string Str(JObject o, string key, string fallback) {
            JToken t = o[key];
            return t != null && t.Type == JTokenType.String ? (string)t : fallback;
        }

        private static float Angle(JObject o, string key, float fallback) {
            if (IsNum(o[key])) {
                return (float)o[key];
            }
            JToken deg = o[key + "Deg"];
            return IsNum(deg) ? MathHelper.ToRadians((float)deg) : fallback;
        }

        private static JArray Vec(Vector2 v) => new(v.X, v.Y);
    }
}
