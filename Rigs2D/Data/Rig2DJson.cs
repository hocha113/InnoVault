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
    ///                 "stretch": "none|axis|uniform", "stretchMin", "stretchMax", "layer", "mirror", "scale", "dark", "tint", "alpha", "visible", "unlit", "frames", "framePad", "proximalNormalized",
    ///                 "bone2", "jointWeight", "bendScale", "frameBy": { "angle|relative|channel", "ref", "thresholds|thresholdsDeg", "map", "hysteresis|hysteresisDeg" } } ],
    ///   "solvers": [ { "name", "type", "bones": [ ... ], ...其余键都是该求解器的参数 } ],
    ///   "ribbons": [ { "name", "bones": [ ... ], "texture", "width", "widthEnd", "widthProfile": [ ... ], "uv": "stretch|tile|bone", "uvStops", "tileLength",
    ///                 "includeTip", "smooth", "layer", "tint", "dark", "alpha", "additive", "visible", "unlit",
    ///                 "bias": 数 | [ ... ], "miter", "miterLimit", "jointBulge", "capStart", "capStartU", "capEnd", "capEndU" } ],
    ///   "clips":   [ { "name", "duration", "loop", "tracks": [ { "bone", "interp": "linear|step", "rotation": [[t, v]], "offset": [[t, [x, y]]], "length": [[t, v]] } ] } ]
    /// }
    /// </code>
    /// 模板层（读取前由 <see cref="Rig2DJsonTemplate"/> 展开）：根级 <c>"vars": { … }</c> 常量；任意数组里的
    /// <c>{ "repeat": N, "var": "i", "values": { … }, "items": [ … ] }</c> 块按序号克隆并原地拼接；值里的 <c>{expr}</c> 占位符
    /// 整串时替换成带类型的值（<c>"targetIndex": "{i}"</c> 得到整数）、混在文字里做文本替换（<c>"coxa{i}"</c>）。
    /// <br/>读取遵循框架约定：格式错误不抛异常，记日志并返回 <see langword="null"/>
    /// </summary>
    public static class Rig2DJson
    {
        /// <summary>
        /// 从 JSON 文本解析定义；失败返回 <see langword="null"/>（已记日志）。返回值尚未 <see cref="Rig2DDefinition.Resolve"/>
        /// </summary>
        public static Rig2DDefinition Parse(string json, string sourceHint = null) {
            if (string.IsNullOrWhiteSpace(json)) {
                Rig2DPlatform.LogError($"[Rig2DJson:{sourceHint}]", "empty json text");
                return null;
            }
            JObject root;
            try {
                root = JObject.Parse(json);
            } catch (Exception ex) {
                Rig2DPlatform.LogError($"[Rig2DJson:{sourceHint}]", $"json parse failed: {ex.Message}");
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
            //模板层：vars / repeat / {表达式} 原地展开成平铺 JSON（root 会被改写）
            if (!Rig2DJsonTemplate.Expand(root, out string templateError)) {
                Rig2DPlatform.LogError($"[Rig2DJson:{sourceHint}]", $"template expansion failed: {templateError}");
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
                if (root["channels"] is JArray channels) {
                    foreach (JToken t in channels) {
                        if (t is JObject o) {
                            def.Channels.Add(ReadChannel(o));
                        }
                    }
                }
                if (root["channelGroups"] is JObject groups) {
                    foreach (JProperty g in groups.Properties()) {
                        List<string> names = [];
                        if (g.Value is JArray arr) {
                            foreach (JToken n in arr) {
                                if (n.Type == JTokenType.String) {
                                    names.Add((string)n);
                                }
                            }
                        }
                        def.ChannelGroups[g.Name] = names;
                    }
                }
                ReadPoses(root["poses"], def);
                ReadMoves(root["moves"], def);
                ReadGaits(root["gaits"], def);
                ReadHitboxes(root["hitboxes"], def);
                return def;
            } catch (Exception ex) {
                Rig2DPlatform.LogError($"[Rig2DJson:{sourceHint}]", $"definition read failed: {ex.Message}");
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
                Unlit = Bool(o, "unlit", false),
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
            p.Bone2 = Str(o, "bone2", null);
            p.JointWeight = Num(o, "jointWeight", 0.5f);
            p.BendScale = Num(o, "bendScale", 0f);
            if (o["frameBy"] is JObject fo) {
                p.FrameBy = ReadFrameBy(fo);
            }
            return p;
        }

        //"frameBy": { "angle" | "relative" | "channel": 骨名 / 通道名, "ref": 参照骨, "thresholds" | "thresholdsDeg": [...], "map": [...], "hysteresis" | "hysteresisDeg" }
        private static Piece2DFrameBy ReadFrameBy(JObject o) {
            Piece2DFrameBy f = new();
            if (o["channel"] != null) {
                f.Source = Piece2DFrameSource.Channel;
                f.Channel = Str(o, "channel", null);
            }
            else if (o["relative"] != null) {
                f.Source = Piece2DFrameSource.Relative;
                f.Bone = Str(o, "relative", null);
            }
            else {
                f.Source = Piece2DFrameSource.Angle;
                f.Bone = Str(o, "angle", null);
            }
            f.Ref = Str(o, "ref", null);
            bool deg = o["thresholdsDeg"] != null;
            float[] th = Floats(deg ? o["thresholdsDeg"] : o["thresholds"]) ?? [];
            if (deg) {
                for (int i = 0; i < th.Length; i++) {
                    th[i] = MathHelper.ToRadians(th[i]);
                }
            }
            Array.Sort(th);
            f.Thresholds = th;
            if (o["map"] is JArray map) {
                f.Map = new int[map.Count];
                for (int i = 0; i < map.Count; i++) {
                    f.Map[i] = IsNum(map[i]) ? (int)(float)map[i] : i;
                }
            }
            f.Hysteresis = o["hysteresisDeg"] != null ? MathHelper.ToRadians(Num(o, "hysteresisDeg", 0f)) : Num(o, "hysteresis", 0f);
            return f;
        }

        private static JObject FrameByToJson(Piece2DFrameBy f) {
            JObject o = new();
            switch (f.Source) {
                case Piece2DFrameSource.Channel:
                    o["channel"] = f.Channel;
                    break;
                case Piece2DFrameSource.Relative:
                    o["relative"] = f.Bone;
                    break;
                default:
                    o["angle"] = f.Bone;
                    break;
            }
            if (!string.IsNullOrEmpty(f.Ref)) {
                o["ref"] = f.Ref;
            }
            o["thresholds"] = new JArray(f.Thresholds ?? []);
            if (f.Map != null) {
                o["map"] = new JArray(f.Map);
            }
            if (f.Hysteresis != 0f) {
                o["hysteresis"] = f.Hysteresis;
            }
            return o;
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
                Unlit = Bool(o, "unlit", false),
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
            r.Uv = Str(o, "uv", "stretch").ToLowerInvariant() switch {
                "tile" => Ribbon2DUv.Tile,
                "bone" => Ribbon2DUv.Bone,
                _ => Ribbon2DUv.Stretch,
            };
            r.UvStops = Floats(o["uvStops"]) ?? [];
            r.BiasProfile = IsNum(o["bias"]) ? [(float)o["bias"]] : Floats(o["bias"]) ?? [];
            r.Miter = Bool(o, "miter", false);
            r.MiterLimit = Num(o, "miterLimit", 2.5f);
            r.JointBulge = Num(o, "jointBulge", 0f);
            r.CapStart = Num(o, "capStart", 0f);
            r.CapStartU = Num(o, "capStartU", 0f);
            r.CapEnd = Num(o, "capEnd", 0f);
            r.CapEndU = Num(o, "capEndU", 0f);
            return r;
        }

        private static Channel2DDef ReadChannel(JObject o) {
            Channel2DDef c = new() {
                Name = Str(o, "name", string.Empty),
                Type = Str(o, "type", "scalar").ToLowerInvariant() is "vector" or "vec2" ? Channel2DType.Vector : Channel2DType.Scalar,
            };
            if (Bool(o, "angle", false)) {
                c.Blend = Channel2DBlend.Angle;
            }
            string blend = Str(o, "blend", null);
            if (blend != null) {
                c.Blend = blend.ToLowerInvariant() switch {
                    "angle" => Channel2DBlend.Angle,
                    "arc" => Channel2DBlend.Arc,
                    _ => Channel2DBlend.Linear,
                };
            }
            JToken dt = o["default"];
            if (IsNum(dt)) {
                c.Default = new Vector2((float)dt, 0f);
            }
            else if (dt != null) {
                c.Default = ReadVector2(dt, Vector2.Zero);
            }
            else if (IsNum(o["defaultDeg"])) {
                c.Default = new Vector2(MathHelper.ToRadians((float)o["defaultDeg"]), 0f);
            }
            if (o["bind"] is JObject b) {
                c.Bind = ReadChannelBind(b);
            }
            return c;
        }

        private static Channel2DBind ReadChannelBind(JObject o) {
            Channel2DBind b = new() {
                Prop = Str(o, "prop", string.Empty),
                Mode = Str(o, "mode", "add").ToLowerInvariant() == "replace" ? Channel2DMode.Replace : Channel2DMode.Add,
                Rotate = Bool(o, "rotate", false),
                Offset = ReadVector2(o["offset"], Vector2.Zero),
                Scale = Num(o, "scale", 1f),
            };
            if (Str(o, "bone", null) is string bone) {
                b.Target = Channel2DTarget.Bone;
                b.Name = bone;
            }
            else if (Str(o, "solver", null) is string solver) {
                b.Target = Channel2DTarget.Solver;
                b.Name = solver;
            }
            else if (Str(o, "piece", null) is string piece) {
                b.Target = Channel2DTarget.Piece;
                b.Name = piece;
            }
            else if (Str(o, "ribbon", null) is string ribbon) {
                b.Target = Channel2DTarget.Ribbon;
                b.Name = ribbon;
            }
            else if (o["root"] != null) {
                b.Target = Channel2DTarget.Root;
                if (o["root"].Type == JTokenType.String) {
                    b.Prop = (string)o["root"];
                }
            }
            string space = Str(o, "space", null);
            if (space != null) {
                b.HasSpace = true;
                string s = space.Trim();
                if (s.StartsWith("bone:", StringComparison.OrdinalIgnoreCase)) {
                    b.Space = Channel2DSpace.Bone;
                    b.SpaceBone = s[5..].Trim();
                }
                else {
                    b.Space = s.ToLowerInvariant() switch {
                        "root" => Channel2DSpace.Root,
                        "bone" => Channel2DSpace.Bone,
                        "world" => Channel2DSpace.World,
                        _ => Channel2DSpace.Anchor,
                    };
                    b.SpaceBone = Str(o, "spaceBone", null);
                }
            }
            return b;
        }

        //poses：对象表（名 → 值表）或数组（每项带 name）；inherit 以外的键都是通道值，xxxDeg 按角度读成通道 xxx
        private static void ReadPoses(JToken token, Rig2DDefinition def) {
            if (token is JObject map) {
                foreach (JProperty p in map.Properties()) {
                    if (p.Value is JObject body) {
                        def.Poses.Add(ReadPose(p.Name, body, def));
                    }
                }
            }
            else if (token is JArray arr) {
                foreach (JToken t in arr) {
                    if (t is JObject body) {
                        def.Poses.Add(ReadPose(Str(body, "name", string.Empty), body, def));
                    }
                }
            }
        }

        private static Pose2DDef ReadPose(string name, JObject body, Rig2DDefinition def) {
            Pose2DDef pose = new() {
                Name = name ?? string.Empty,
                Inherit = Str(body, "inherit", null),
            };
            foreach (JProperty prop in body.Properties()) {
                if (prop.Name is "name" or "inherit") {
                    continue;
                }
                string key = prop.Name;
                bool degrees = false;
                if (key.EndsWith("Deg", StringComparison.Ordinal) && key.Length > 3 && !HasChannel(def, key)) {
                    key = key[..^3];
                    degrees = true;
                }
                if (IsNum(prop.Value)) {
                    float v = (float)prop.Value;
                    pose.Values[key] = new Vector2(degrees ? MathHelper.ToRadians(v) : v, 0f);
                }
                else if (prop.Value is JArray || prop.Value is JObject) {
                    pose.Values[key] = ReadVector2(prop.Value, Vector2.Zero);
                }
            }
            return pose;
        }

        //moves：对象表（名 → 体）或数组（每项带 name）
        private static void ReadMoves(JToken token, Rig2DDefinition def) {
            if (token is JObject map) {
                foreach (JProperty p in map.Properties()) {
                    if (p.Value is JObject body) {
                        def.Moves.Add(ReadMove(p.Name, body));
                    }
                }
            }
            else if (token is JArray arr) {
                foreach (JToken t in arr) {
                    if (t is JObject body) {
                        def.Moves.Add(ReadMove(Str(body, "name", string.Empty), body));
                    }
                }
            }
        }

        private static Move2DDef ReadMove(string name, JObject o) {
            Move2DDef m = new() {
                Name = name ?? string.Empty,
                Start = Str(o, "start", "@base"),
            };
            if (o["segments"] is JArray segs) {
                foreach (JToken t in segs) {
                    if (t is not JObject s) {
                        continue;
                    }
                    m.Segments.Add(new Move2DSegment {
                        Name = Str(s, "name", string.Empty),
                        To = Str(s, "to", null),
                        Frames = Num(s, "frames", 1f),
                        Ease = Str(s, "ease", "linear"),
                        Arrive = Bool(s, "arrive", false),
                    });
                }
            }
            if (o["groups"] is JObject groups) {
                foreach (JProperty g in groups.Properties()) {
                    if (g.Value is not JObject go) {
                        continue;
                    }
                    Move2DGroup mg = new() {
                        Lead = IsNum(go["lead"]) ? (float)go["lead"] : (IsNum(go["lag"]) ? -(float)go["lag"] : 0f),
                    };
                    if (go["resize"] is JObject rs) {
                        foreach (JProperty r in rs.Properties()) {
                            if (IsNum(r.Value)) {
                                mg.Resize[r.Name] = (float)r.Value;
                            }
                        }
                    }
                    if (go["ease"] is JObject es) {
                        foreach (JProperty e in es.Properties()) {
                            if (e.Value.Type == JTokenType.String) {
                                mg.Ease[e.Name] = (string)e.Value;
                            }
                        }
                    }
                    m.Groups[g.Name] = mg;
                }
            }
            if (o["arcs"] is JArray arcs) {
                foreach (JToken t in arcs) {
                    if (t is not JObject a) {
                        continue;
                    }
                    m.Arcs.Add(new Move2DArc {
                        Channel = Str(a, "channel", string.Empty),
                        Group = Str(a, "group", null),
                        Lift = Num(a, "lift", 0.45f),
                        Max = Num(a, "max", 72f),
                        Axis = Str(a, "axis", "x").ToLowerInvariant() == "y" ? 1 : 0,
                        Out = Str(a, "out", "y").ToLowerInvariant() == "x" ? 0 : 1,
                        Sign = Num(a, "sign", -1f),
                        Scale = Num(a, "scale", 1f),
                        AngleChannel = Str(a, "angle", null),
                        AngleAmount = Num(a, "angleAmount", 0f),
                    });
                }
            }
            if (o["accents"] is JArray accents) {
                foreach (JToken t in accents) {
                    if (t is not JObject a) {
                        continue;
                    }
                    Move2DAccent acc = new() {
                        Group = Str(a, "group", null),
                        After = Str(a, "after", string.Empty),
                        Span = Num(a, "span", 0f),
                        Over = Str(a, "over", null),
                        Curve = Str(a, "curve", "whip"),
                        Direction = Str(a, "direction", null),
                    };
                    if (a["channels"] is JObject cs) {
                        foreach (JProperty c in cs.Properties()) {
                            acc.Channels[c.Name] = IsNum(c.Value) ? new Vector2((float)c.Value, 0f) : ReadVector2(c.Value, Vector2.Zero);
                        }
                    }
                    m.Accents.Add(acc);
                }
            }
            if (o["events"] is JObject events) {
                foreach (JProperty e in events.Properties()) {
                    if (e.Value is not JObject eo) {
                        continue;
                    }
                    m.Events[e.Name] = new Move2DEvent {
                        Group = Str(eo, "group", null),
                        Segment = Str(eo, "segment", string.Empty),
                        AtEnd = Str(eo, "at", "end").ToLowerInvariant() != "start",
                        Offset = Num(eo, "offset", 0f),
                    };
                }
            }
            return m;
        }

        private static bool HasChannel(Rig2DDefinition def, string name) {
            foreach (Channel2DDef c in def.Channels) {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        private static Rig2DClip ReadClip(JObject o) {
            string name = Str(o, "name", null);
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            Rig2DClip clip = new(name, Num(o, "duration", 60f)) {
                Loop = Bool(o, "loop", true),
            };
            JArray tracks = o["tracks"] as JArray ?? [];
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
                            track.Offset.Add(new Rig2DKey<Vector2>((float)pair[0], ReadVector2(pair[1], Vector2.Zero), KeyEase(pair)));
                        }
                    }
                }
            }
            //通道轨：{ "channel", "keys": [[t, 值 或 [x, y], "缓动"?]] }
            if (o["channels"] is JArray chanTracks) {
                foreach (JToken t in chanTracks) {
                    if (t is not JObject co) {
                        continue;
                    }
                    string ch = Str(co, "channel", null);
                    if (string.IsNullOrEmpty(ch) || co["keys"] is not JArray keys) {
                        continue;
                    }
                    Rig2DChannelTrack track = clip.ChannelTrack(ch);
                    foreach (JToken k in keys) {
                        if (k is not JArray pair || pair.Count < 2 || !IsNum(pair[0])) {
                            continue;
                        }
                        Vector2 v = IsNum(pair[1]) ? new Vector2((float)pair[1], 0f) : ReadVector2(pair[1], Vector2.Zero);
                        track.Keys.Add(new Rig2DKey<Vector2>((float)pair[0], v, KeyEase(pair)));
                    }
                }
            }
            if (o["events"] is JObject events) {
                foreach (JProperty e in events.Properties()) {
                    if (IsNum(e.Value)) {
                        clip.Events[e.Name] = (float)e.Value;
                    }
                }
            }
            return clip;
        }

        //键数组第三个元素是到达本键那一段的缓动文本（可省）
        private static Rig2DEase KeyEase(JArray pair) => pair.Count >= 3 && pair[2].Type == JTokenType.String
            ? Rig2DEase.Parse((string)pair[2]) : Rig2DEase.Linear;

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
                    sink.Add(new Rig2DKey<float>((float)pair[0], v, KeyEase(pair)));
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
                if (p.Unlit) {
                    o["unlit"] = true;
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
                if (!string.IsNullOrEmpty(p.Bone2)) {
                    o["bone2"] = p.Bone2;
                    if (p.JointWeight != 0.5f) {
                        o["jointWeight"] = p.JointWeight;
                    }
                    if (p.BendScale != 0f) {
                        o["bendScale"] = p.BendScale;
                    }
                }
                if (p.FrameBy != null) {
                    o["frameBy"] = FrameByToJson(p.FrameBy);
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
                    if (r.UvStops != null && r.UvStops.Length > 0) {
                        o["uvStops"] = new JArray(r.UvStops);
                    }
                    if (r.BiasProfile != null && r.BiasProfile.Length > 0) {
                        o["bias"] = new JArray(r.BiasProfile);
                    }
                    if (r.Miter) {
                        o["miter"] = true;
                        if (r.MiterLimit != 2.5f) {
                            o["miterLimit"] = r.MiterLimit;
                        }
                    }
                    if (r.JointBulge != 0f) {
                        o["jointBulge"] = r.JointBulge;
                    }
                    if (r.CapStart > 0f) {
                        o["capStart"] = r.CapStart;
                        o["capStartU"] = r.CapStartU;
                    }
                    if (r.CapEnd > 0f) {
                        o["capEnd"] = r.CapEnd;
                        o["capEndU"] = r.CapEndU;
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
                    if (r.Unlit) {
                        o["unlit"] = true;
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
            if (def.Channels.Count > 0) {
                JArray channels = [];
                foreach (Channel2DDef c in def.Channels) {
                    channels.Add(ChannelToJson(c));
                }
                root["channels"] = channels;
            }
            if (def.ChannelGroups.Count > 0) {
                JObject groups = new();
                foreach (KeyValuePair<string, List<string>> kv in def.ChannelGroups) {
                    groups[kv.Key] = new JArray(kv.Value);
                }
                root["channelGroups"] = groups;
            }
            if (def.Poses.Count > 0) {
                JObject poses = new();
                foreach (Pose2DDef p in def.Poses) {
                    JObject body = new();
                    if (!string.IsNullOrEmpty(p.Inherit)) {
                        body["inherit"] = p.Inherit;
                    }
                    foreach (KeyValuePair<string, Vector2> kv in p.Values) {
                        Channel2DDef ch = null;
                        foreach (Channel2DDef c in def.Channels) {
                            if (c.Name == kv.Key) {
                                ch = c;
                                break;
                            }
                        }
                        body[kv.Key] = ch != null && ch.Type == Channel2DType.Vector ? Vec(kv.Value) : kv.Value.X;
                    }
                    poses[p.Name] = body;
                }
                root["poses"] = poses;
            }
            if (def.Moves.Count > 0) {
                JObject moves = new();
                foreach (Move2DDef m in def.Moves) {
                    moves[m.Name] = MoveToJson(m);
                }
                root["moves"] = moves;
            }
            if (def.Gaits.Count > 0) {
                JObject gaits = new();
                foreach (Gait2DDef g in def.Gaits) {
                    gaits[g.Name] = GaitToJson(g);
                }
                root["gaits"] = gaits;
            }
            if (def.Hitboxes.Count > 0) {
                JObject groups = new();
                foreach (KeyValuePair<string, List<Hitbox2DDef>> kv in def.Hitboxes) {
                    JArray arr = [];
                    foreach (Hitbox2DDef h in kv.Value) {
                        JObject ho = new() { ["bone"] = h.Bone, ["radius"] = h.Radius };
                        if (h.From != 0f) {
                            ho["from"] = h.From;
                        }
                        if (h.To != 1f) {
                            ho["to"] = h.To;
                        }
                        arr.Add(ho);
                    }
                    groups[kv.Key] = arr;
                }
                root["hitboxes"] = groups;
            }
            return root;
        }

        //"hitboxes": { "组名": [ { "bone", "radius", "from" 0, "to" 1 } ] }
        private static void ReadHitboxes(JToken token, Rig2DDefinition def) {
            if (token is not JObject map) {
                return;
            }
            foreach (JProperty p in map.Properties()) {
                if (p.Value is not JArray arr) {
                    continue;
                }
                List<Hitbox2DDef> list = [];
                foreach (JToken t in arr) {
                    if (t is JObject o) {
                        list.Add(new Hitbox2DDef {
                            Bone = Str(o, "bone", string.Empty),
                            Radius = Num(o, "radius", 8f),
                            From = Num(o, "from", 0f),
                            To = Num(o, "to", 1f),
                        });
                    }
                }
                def.Hitboxes[p.Name] = list;
            }
        }

        //═══════════════ 运动层 ═══════════════
        // "gaits": { "名": { "legs": [ { "channel", "angle", "phase", "offset" } ], "hip", "hipAxis": "x|y", "tilt",
        //   "modes": { "walk": { "stance", "stride", "cycleMin", "cycleMax", "speedSlow", "speedFast", "lift", "lifts": [],
        //     "bob", "bobOffset": 数 | "mid", "bobPow", "bobBase", "lean", "leanWave", "leanWaveOffset", "speedScaled",
        //     "reachBias", "reach": [], "phases": [], "centered", "farOffset", "skew", "toe" } },
        //   "heel", "heelSpan", "toeOffStart", "toeOffSpan", "toeOff", "swingToe", "liftPow", "liftStretch", "bobSpeed", "minCycle",
        //   "sinkLow", "raiseBoth", "raiseLow", "stepUp", "stepDown" } }

        private static void ReadGaits(JToken token, Rig2DDefinition def) {
            if (token is not JObject map) {
                return;
            }
            foreach (JProperty p in map.Properties()) {
                if (p.Value is not JObject o) {
                    continue;
                }
                Gait2DDef g = new() {
                    Name = p.Name,
                    Hip = Str(o, "hip", null),
                    HipAxis = Str(o, "hipAxis", "y").ToLowerInvariant() == "x" ? 0 : 1,
                    Tilt = Str(o, "tilt", null),
                };
                g.Heel = Num(o, "heel", g.Heel);
                g.HeelSpan = Num(o, "heelSpan", g.HeelSpan);
                g.ToeOffStart = Num(o, "toeOffStart", g.ToeOffStart);
                g.ToeOffSpan = Num(o, "toeOffSpan", g.ToeOffSpan);
                g.ToeOff = Num(o, "toeOff", g.ToeOff);
                g.SwingToe = Num(o, "swingToe", g.SwingToe);
                g.LiftPow = Num(o, "liftPow", g.LiftPow);
                g.LiftStretch = Num(o, "liftStretch", g.LiftStretch);
                g.BobSpeed = Num(o, "bobSpeed", g.BobSpeed);
                g.MinCycle = Num(o, "minCycle", g.MinCycle);
                g.SinkLow = Num(o, "sinkLow", g.SinkLow);
                g.RaiseBoth = Num(o, "raiseBoth", g.RaiseBoth);
                g.RaiseLow = Num(o, "raiseLow", g.RaiseLow);
                g.StepUp = Num(o, "stepUp", g.StepUp);
                g.StepDown = Num(o, "stepDown", g.StepDown);
                if (o["legs"] is JArray legs) {
                    foreach (JToken t in legs) {
                        if (t is JObject lo) {
                            g.Legs.Add(new Gait2DLeg {
                                Channel = Str(lo, "channel", string.Empty),
                                Angle = Str(lo, "angle", null),
                                Phase = Num(lo, "phase", 0f),
                                Offset = Num(lo, "offset", 0f),
                            });
                        }
                    }
                }
                if (o["modes"] is JObject modes) {
                    foreach (JProperty mp in modes.Properties()) {
                        if (mp.Value is JObject mo) {
                            g.Modes.Add(ReadGaitMode(mp.Name, mo));
                        }
                    }
                }
                else if (o["modes"] is JArray modeList) {
                    foreach (JToken t in modeList) {
                        if (t is JObject mo) {
                            g.Modes.Add(ReadGaitMode(Str(mo, "name", string.Empty), mo));
                        }
                    }
                }
                def.Gaits.Add(g);
            }
        }

        private static Gait2DMode ReadGaitMode(string name, JObject o) {
            Gait2DMode m = new() { Name = name };
            m.Stance = Num(o, "stance", m.Stance);
            m.Stride = Num(o, "stride", m.Stride);
            m.CycleMin = Num(o, "cycleMin", m.CycleMin);
            m.CycleMax = Num(o, "cycleMax", m.CycleMax);
            m.SpeedSlow = Num(o, "speedSlow", m.SpeedSlow);
            m.SpeedFast = Num(o, "speedFast", m.SpeedFast);
            m.Lift = Num(o, "lift", m.Lift);
            m.Lifts = Floats(o["lifts"]);
            m.Bob = Num(o, "bob", m.Bob);
            m.BobOffset = Str(o, "bobOffset", null) == "mid" ? float.NaN : Num(o, "bobOffset", m.BobOffset);
            m.BobPow = Num(o, "bobPow", m.BobPow);
            m.BobBase = Num(o, "bobBase", m.BobBase);
            m.Lean = Num(o, "lean", m.Lean);
            m.LeanWave = Num(o, "leanWave", m.LeanWave);
            m.LeanWaveOffset = Num(o, "leanWaveOffset", m.LeanWaveOffset);
            m.SpeedScaled = Bool(o, "speedScaled", m.SpeedScaled);
            m.ReachBias = Num(o, "reachBias", m.ReachBias);
            m.Reach = Floats(o["reach"]);
            m.Phases = Floats(o["phases"]);
            m.Centered = Bool(o, "centered", m.Centered);
            m.FarOffset = Num(o, "farOffset", m.FarOffset);
            m.Skew = Num(o, "skew", m.Skew);
            m.Toe = Num(o, "toe", m.Toe);
            return m;
        }

        private static float[] Floats(JToken t) {
            if (t is not JArray arr) {
                return null;
            }
            float[] v = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++) {
                v[i] = IsNum(arr[i]) ? (float)arr[i] : 0f;
            }
            return v;
        }

        private static JObject GaitToJson(Gait2DDef g) {
            Gait2DDef d = new();
            JObject o = new();
            JArray legs = [];
            foreach (Gait2DLeg l in g.Legs) {
                JObject lo = new() { ["channel"] = l.Channel, ["phase"] = l.Phase };
                if (!string.IsNullOrEmpty(l.Angle)) {
                    lo["angle"] = l.Angle;
                }
                if (l.Offset != 0f) {
                    lo["offset"] = l.Offset;
                }
                legs.Add(lo);
            }
            o["legs"] = legs;
            if (!string.IsNullOrEmpty(g.Hip)) {
                o["hip"] = g.Hip;
                if (g.HipAxis == 0) {
                    o["hipAxis"] = "x";
                }
            }
            if (!string.IsNullOrEmpty(g.Tilt)) {
                o["tilt"] = g.Tilt;
            }
            void Opt(string key, float v, float dv) {
                if (v != dv) {
                    o[key] = v;
                }
            }
            Opt("heel", g.Heel, d.Heel);
            Opt("heelSpan", g.HeelSpan, d.HeelSpan);
            Opt("toeOffStart", g.ToeOffStart, d.ToeOffStart);
            Opt("toeOffSpan", g.ToeOffSpan, d.ToeOffSpan);
            Opt("toeOff", g.ToeOff, d.ToeOff);
            Opt("swingToe", g.SwingToe, d.SwingToe);
            Opt("liftPow", g.LiftPow, d.LiftPow);
            Opt("liftStretch", g.LiftStretch, d.LiftStretch);
            Opt("bobSpeed", g.BobSpeed, d.BobSpeed);
            Opt("minCycle", g.MinCycle, d.MinCycle);
            Opt("sinkLow", g.SinkLow, d.SinkLow);
            Opt("raiseBoth", g.RaiseBoth, d.RaiseBoth);
            Opt("raiseLow", g.RaiseLow, d.RaiseLow);
            Opt("stepUp", g.StepUp, d.StepUp);
            Opt("stepDown", g.StepDown, d.StepDown);
            JObject modes = new();
            foreach (Gait2DMode m in g.Modes) {
                Gait2DMode dm = new();
                JObject mo = new();
                void M(string key, float v, float dv) {
                    if (v != dv) {
                        mo[key] = v;
                    }
                }
                M("stance", m.Stance, dm.Stance);
                M("stride", m.Stride, dm.Stride);
                M("cycleMin", m.CycleMin, dm.CycleMin);
                M("cycleMax", m.CycleMax, dm.CycleMax);
                M("speedSlow", m.SpeedSlow, dm.SpeedSlow);
                M("speedFast", m.SpeedFast, dm.SpeedFast);
                M("lift", m.Lift, dm.Lift);
                M("bob", m.Bob, dm.Bob);
                if (float.IsNaN(m.BobOffset)) {
                    mo["bobOffset"] = "mid";
                }
                else {
                    M("bobOffset", m.BobOffset, dm.BobOffset);
                }
                M("bobPow", m.BobPow, dm.BobPow);
                M("bobBase", m.BobBase, dm.BobBase);
                M("lean", m.Lean, dm.Lean);
                M("leanWave", m.LeanWave, dm.LeanWave);
                M("leanWaveOffset", m.LeanWaveOffset, dm.LeanWaveOffset);
                if (m.SpeedScaled != dm.SpeedScaled) {
                    mo["speedScaled"] = m.SpeedScaled;
                }
                M("reachBias", m.ReachBias, dm.ReachBias);
                if (m.Centered != dm.Centered) {
                    mo["centered"] = m.Centered;
                }
                M("farOffset", m.FarOffset, dm.FarOffset);
                M("skew", m.Skew, dm.Skew);
                M("toe", m.Toe, dm.Toe);
                if (m.Reach != null) {
                    mo["reach"] = new JArray(m.Reach);
                }
                if (m.Lifts != null) {
                    mo["lifts"] = new JArray(m.Lifts);
                }
                if (m.Phases != null) {
                    mo["phases"] = new JArray(m.Phases);
                }
                modes[m.Name] = mo;
            }
            o["modes"] = modes;
            return o;
        }

        private static JObject ChannelToJson(Channel2DDef c) {
            JObject o = new() { ["name"] = c.Name };
            if (c.Type == Channel2DType.Vector) {
                o["type"] = "vector";
                if (c.Default != Vector2.Zero) {
                    o["default"] = Vec(c.Default);
                }
            }
            else if (c.Default.X != 0f) {
                o["default"] = c.Default.X;
            }
            if (c.Blend != Channel2DBlend.Linear) {
                o["blend"] = c.Blend.ToString().ToLowerInvariant();
            }
            Channel2DBind b = c.Bind;
            if (b != null && b.Target != Channel2DTarget.None) {
                JObject bind = new();
                switch (b.Target) {
                    case Channel2DTarget.Bone: bind["bone"] = b.Name; break;
                    case Channel2DTarget.Solver: bind["solver"] = b.Name; break;
                    case Channel2DTarget.Piece: bind["piece"] = b.Name; break;
                    case Channel2DTarget.Ribbon: bind["ribbon"] = b.Name; break;
                    case Channel2DTarget.Root: bind["root"] = true; break;
                }
                bind["prop"] = b.Prop;
                if (b.Mode == Channel2DMode.Replace) {
                    bind["mode"] = "replace";
                }
                if (b.HasSpace) {
                    bind["space"] = b.Space == Channel2DSpace.Bone ? "bone:" + b.SpaceBone : b.Space.ToString().ToLowerInvariant();
                }
                if (b.Rotate) {
                    bind["rotate"] = true;
                }
                if (b.Offset != Vector2.Zero) {
                    bind["offset"] = Vec(b.Offset);
                }
                if (b.Scale != 1f) {
                    bind["scale"] = b.Scale;
                }
                o["bind"] = bind;
            }
            return o;
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
                        arr.Add(KeyJson(k.Time, k.Value, k.Ease));
                    }
                    t["rotation"] = arr;
                }
                if (track.Length.Count > 0) {
                    JArray arr = [];
                    foreach (Rig2DKey<float> k in track.Length) {
                        arr.Add(KeyJson(k.Time, k.Value, k.Ease));
                    }
                    t["length"] = arr;
                }
                if (track.Offset.Count > 0) {
                    JArray arr = [];
                    foreach (Rig2DKey<Vector2> k in track.Offset) {
                        arr.Add(KeyJson(k.Time, Vec(k.Value), k.Ease));
                    }
                    t["offset"] = arr;
                }
                tracks.Add(t);
            }
            o["tracks"] = tracks;
            if (clip.ChannelTracks.Count > 0) {
                JArray chans = [];
                foreach (Rig2DChannelTrack track in clip.ChannelTracks) {
                    JArray keys = [];
                    foreach (Rig2DKey<Vector2> k in track.Keys) {
                        keys.Add(KeyJson(k.Time, Vec(k.Value), k.Ease));
                    }
                    chans.Add(new JObject { ["channel"] = track.ChannelName, ["keys"] = keys });
                }
                o["channels"] = chans;
            }
            if (clip.Events.Count > 0) {
                JObject events = new();
                foreach (KeyValuePair<string, float> kv in clip.Events) {
                    events[kv.Key] = kv.Value;
                }
                o["events"] = events;
            }
            return o;
        }

        private static JArray KeyJson(float time, JToken value, Rig2DEase ease) {
            JArray a = new(time, value);
            if (ease.Kind != Rig2DEaseKind.Linear) {
                a.Add(ease.ToString());
            }
            return a;
        }

        private static JObject MoveToJson(Move2DDef m) {
            JObject o = new();
            if (!string.IsNullOrEmpty(m.Start) && m.Start != "@base") {
                o["start"] = m.Start;
            }
            JArray segs = [];
            foreach (Move2DSegment s in m.Segments) {
                JObject so = new() { ["name"] = s.Name, ["frames"] = s.Frames };
                if (!string.IsNullOrEmpty(s.To)) {
                    so["to"] = s.To;
                }
                if (!string.Equals(s.Ease, "linear", StringComparison.OrdinalIgnoreCase)) {
                    so["ease"] = s.Ease;
                }
                if (s.Arrive) {
                    so["arrive"] = true;
                }
                segs.Add(so);
            }
            o["segments"] = segs;
            if (m.Groups.Count > 0) {
                JObject groups = new();
                foreach (KeyValuePair<string, Move2DGroup> kv in m.Groups) {
                    JObject g = new();
                    if (kv.Value.Lead != 0f) {
                        g["lead"] = kv.Value.Lead;
                    }
                    if (kv.Value.Resize.Count > 0) {
                        JObject rs = new();
                        foreach (KeyValuePair<string, float> r in kv.Value.Resize) {
                            rs[r.Key] = r.Value;
                        }
                        g["resize"] = rs;
                    }
                    if (kv.Value.Ease.Count > 0) {
                        JObject es = new();
                        foreach (KeyValuePair<string, string> e in kv.Value.Ease) {
                            es[e.Key] = e.Value;
                        }
                        g["ease"] = es;
                    }
                    groups[kv.Key] = g;
                }
                o["groups"] = groups;
            }
            if (m.Arcs.Count > 0) {
                JArray arcs = [];
                foreach (Move2DArc a in m.Arcs) {
                    JObject ao = new() { ["channel"] = a.Channel, ["lift"] = a.Lift, ["max"] = a.Max };
                    if (!string.IsNullOrEmpty(a.Group)) {
                        ao["group"] = a.Group;
                    }
                    if (a.Axis != 0) {
                        ao["axis"] = "y";
                    }
                    if (a.Out != 1) {
                        ao["out"] = "x";
                    }
                    if (a.Sign != -1f) {
                        ao["sign"] = a.Sign;
                    }
                    if (a.Scale != 1f) {
                        ao["scale"] = a.Scale;
                    }
                    if (!string.IsNullOrEmpty(a.AngleChannel)) {
                        ao["angle"] = a.AngleChannel;
                        ao["angleAmount"] = a.AngleAmount;
                    }
                    arcs.Add(ao);
                }
                o["arcs"] = arcs;
            }
            if (m.Accents.Count > 0) {
                JArray accents = [];
                foreach (Move2DAccent a in m.Accents) {
                    JObject ao = new() { ["after"] = a.After, ["curve"] = a.Curve };
                    if (!string.IsNullOrEmpty(a.Group)) {
                        ao["group"] = a.Group;
                    }
                    if (a.Span > 0f) {
                        ao["span"] = a.Span;
                    }
                    if (!string.IsNullOrEmpty(a.Over)) {
                        ao["over"] = a.Over;
                    }
                    if (!string.IsNullOrEmpty(a.Direction)) {
                        ao["direction"] = a.Direction;
                    }
                    JObject cs = new();
                    foreach (KeyValuePair<string, Vector2> kv in a.Channels) {
                        cs[kv.Key] = kv.Value.Y == 0f ? kv.Value.X : Vec(kv.Value);
                    }
                    ao["channels"] = cs;
                    accents.Add(ao);
                }
                o["accents"] = accents;
            }
            if (m.Events.Count > 0) {
                JObject events = new();
                foreach (KeyValuePair<string, Move2DEvent> kv in m.Events) {
                    JObject eo = new() { ["segment"] = kv.Value.Segment, ["at"] = kv.Value.AtEnd ? "end" : "start" };
                    if (!string.IsNullOrEmpty(kv.Value.Group)) {
                        eo["group"] = kv.Value.Group;
                    }
                    if (kv.Value.Offset != 0f) {
                        eo["offset"] = kv.Value.Offset;
                    }
                    events[kv.Key] = eo;
                }
                o["events"] = events;
            }
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
