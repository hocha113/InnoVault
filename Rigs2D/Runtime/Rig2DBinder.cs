using InnoVault.Rigs2D.Solvers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 声明式句柄绑定的执行者：按目标类型缓存一次反射出的绑定计划（成员、形状、名字模板），
    /// 之后每次（重）绑定只做名字查表与写回。只在实例创建与热重载时运行，不在帧热路径上
    /// </summary>
    internal static class Rig2DBinder
    {
        private enum Kind
        {
            Bone,
            Piece,
            Ribbon,
            Solver,
            Channel,
            Pose,
            Move,
        }

        private enum Shape
        {
            Scalar,
            Array1,
            Array2,
        }

        private sealed class Entry
        {
            public MemberInfo Member;
            public Kind Kind;
            public Shape Shape;
            /// <summary>标量 / 数组元素类型：下标为 <see cref="int"/>，求解器为具体子类</summary>
            public Type ElementType;
            public string[] Names;
            public int Count;
            public int Start;
            /// <summary>建计划时就能确定的声明错误（形状 / 类型不合），每次绑定都报</summary>
            public string Error;

            public string Display => (Member.DeclaringType?.Name ?? string.Empty) + "." + Member.Name;

            public Type MemberType => Member is FieldInfo f ? f.FieldType : ((PropertyInfo)Member).PropertyType;

            public object Get(object target) {
                if (Member is FieldInfo f) {
                    return f.GetValue(target);
                }
                PropertyInfo p = (PropertyInfo)Member;
                return p.GetGetMethod(true) != null ? p.GetValue(target) : null;
            }

            public bool TrySet(object target, object value) {
                if (Member is FieldInfo f) {
                    //实例 readonly 字段允许反射写入
                    f.SetValue(target, value);
                    return true;
                }
                MethodInfo setter = ((PropertyInfo)Member).GetSetMethod(true);
                if (setter == null) {
                    return false;
                }
                setter.Invoke(target, [value]);
                return true;
            }
        }

        private static readonly ConcurrentDictionary<Type, Entry[]> plans = new();

        internal static void ClearCache() => plans.Clear();

        /// <summary>
        /// 把 <paramref name="target"/> 上全部标记成员按 <paramref name="rig"/> 当前定义填好；返回是否全部命中，问题追加进 <paramref name="errors"/>
        /// </summary>
        internal static bool Apply(Rig2DInstance rig, object target, List<string> errors) {
            Entry[] plan = plans.GetOrAdd(target.GetType(), BuildPlan);
            bool ok = true;
            for (int i = 0; i < plan.Length; i++) {
                Entry e = plan[i];
                if (e.Error != null) {
                    errors.Add($"{e.Display}: {e.Error}");
                    ok = false;
                    continue;
                }
                try {
                    bool hit = e.Shape switch {
                        Shape.Scalar => ApplyScalar(rig, target, e, errors),
                        Shape.Array1 => ApplyArray1(rig, target, e, errors),
                        _ => ApplyArray2(rig, target, e, errors),
                    };
                    ok &= hit;
                } catch (Exception ex) {
                    errors.Add($"{e.Display}: write failed: {ex.Message}");
                    ok = false;
                }
            }
            return ok;
        }

        //==================== 计划 ====================

        private static Entry[] BuildPlan(Type type) {
            List<Entry> list = [];
            HashSet<string> seenProperties = [];
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            //逐层向上收：基类的私有成员 GetFields(Instance) 拿不到，得每层单独扫
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType) {
                foreach (FieldInfo f in t.GetFields(flags)) {
                    Add(list, f, f.FieldType, f.IsStatic);
                }
                foreach (PropertyInfo p in t.GetProperties(flags)) {
                    if (!seenProperties.Add(p.Name)) {
                        continue;//派生类的重写已收过
                    }
                    MethodInfo accessor = p.GetGetMethod(true) ?? p.GetSetMethod(true);
                    Add(list, p, p.PropertyType, accessor != null && accessor.IsStatic);
                }
            }
            return list.ToArray();
        }

        private static void Add(List<Entry> list, MemberInfo member, Type memberType, bool isStatic) {
            Rig2DBindAttribute attr;
            try {
                attr = member.GetCustomAttribute<Rig2DBindAttribute>(true);
            } catch (Exception ex) {
                Rig2DPlatform.LogError("[Rig2D:bind]", $"cannot read bind attribute on {member.DeclaringType?.Name}.{member.Name}: {ex.Message}");
                return;
            }
            if (attr == null) {
                return;
            }

            Entry e = new() {
                Member = member,
                Names = attr.Names.Length > 0 ? attr.Names : [member.Name],
                Count = attr.Count,
                Start = attr.Start,
            };
            switch (attr) {
                case Rig2DBoneAttribute:
                    e.Kind = Kind.Bone;
                    break;
                case Rig2DPieceAttribute:
                    e.Kind = Kind.Piece;
                    break;
                case Rig2DRibbonAttribute:
                    e.Kind = Kind.Ribbon;
                    break;
                case Rig2DSolverAttribute:
                    e.Kind = Kind.Solver;
                    break;
                case Rig2DChannelAttribute:
                    e.Kind = Kind.Channel;
                    break;
                case Rig2DPoseAttribute:
                    e.Kind = Kind.Pose;
                    break;
                case Rig2DMoveAttribute:
                    e.Kind = Kind.Move;
                    break;
                default:
                    e.Error = $"unsupported bind attribute {attr.GetType().Name}";
                    list.Add(e);
                    return;
            }
            if (isStatic) {
                e.Error = "static members cannot be bound (handles belong to one instance)";
                list.Add(e);
                return;
            }

            if (memberType.IsArray) {
                e.ElementType = memberType.GetElementType();
                int rank = memberType.GetArrayRank();
                if (rank == 1) {
                    e.Shape = Shape.Array1;
                }
                else if (rank == 2) {
                    e.Shape = Shape.Array2;
                }
                else {
                    e.Error = "only 1D / 2D arrays are supported";
                }
            }
            else {
                e.Shape = Shape.Scalar;
                e.ElementType = memberType;
            }

            if (e.Error == null) {
                if (e.Kind == Kind.Solver) {
                    if (!typeof(Rig2DSolver).IsAssignableFrom(e.ElementType)) {
                        e.Error = $"[Rig2DSolver] needs a Rig2DSolver-derived member (or array of it), got {memberType.Name}";
                    }
                }
                else if (e.Kind == Kind.Pose) {
                    if (e.ElementType != typeof(int) && e.ElementType != typeof(Animation.Rig2DPose)) {
                        e.Error = $"[Rig2DPose] needs int / Rig2DPose (or arrays of them), got {memberType.Name}";
                    }
                }
                else if (e.Kind == Kind.Move) {
                    if (e.ElementType != typeof(int) && e.ElementType != typeof(Animation.Rig2DMove)) {
                        e.Error = $"[Rig2DMove] needs int / Rig2DMove (or arrays of them), got {memberType.Name}";
                    }
                }
                else if (e.ElementType != typeof(int)) {
                    e.Error = $"[{attr.GetType().Name.Replace("Attribute", string.Empty)}] needs int / int[] / int[,], got {memberType.Name}";
                }
            }

            if (e.Error == null) {
                switch (e.Shape) {
                    case Shape.Scalar:
                        if (e.Count > 0) {
                            e.Error = "Count needs an array member";
                        }
                        else if (e.Names.Length != 1) {
                            e.Error = "a scalar takes exactly one name";
                        }
                        else if (member is PropertyInfo p && p.GetSetMethod(true) == null) {
                            e.Error = "property has no setter";
                        }
                        break;
                    case Shape.Array1:
                        if (e.Count > 0 && e.Names.Length != 1) {
                            e.Error = "a 1D array with Count takes exactly one template";
                        }
                        break;
                    default:
                        if (e.Count <= 0) {
                            e.Error = "a 2D array needs Count > 0 (rows = Count, columns = names)";
                        }
                        break;
                }
            }

            if (e.Error == null && e.Count > 0) {
                foreach (string name in e.Names) {
                    if (name == null || !name.Contains("{0}")) {
                        e.Error = $"template '{name}' has no {{0}} placeholder";
                        break;
                    }
                }
            }

            list.Add(e);
        }

        //==================== 执行 ====================

        private static string Expand(string template, int index) => string.Format(CultureInfo.InvariantCulture, template, index);

        private static bool Resolve(Rig2DInstance rig, Entry e, string name, string slot, List<string> errors, out object value) {
            switch (e.Kind) {
                case Kind.Bone: {
                    int i = rig.Bone(name);
                    value = i;
                    if (i < 0) {
                        errors.Add($"{slot}: bone '{name}' not found");
                        return false;
                    }
                    return true;
                }
                case Kind.Piece: {
                    int i = rig.Piece(name);
                    value = i;
                    if (i < 0) {
                        errors.Add($"{slot}: piece '{name}' not found");
                        return false;
                    }
                    return true;
                }
                case Kind.Ribbon: {
                    int i = rig.Ribbon(name);
                    value = i;
                    if (i < 0) {
                        errors.Add($"{slot}: ribbon '{name}' not found");
                        return false;
                    }
                    return true;
                }
                case Kind.Channel: {
                    int i = rig.Channels.Index(name);
                    value = i;
                    if (i < 0) {
                        errors.Add($"{slot}: channel '{name}' not found");
                        return false;
                    }
                    return true;
                }
                case Kind.Pose: {
                    int i = rig.Definition?.PoseIndex(name) ?? -1;
                    value = e.ElementType == typeof(int) ? i : rig.Pose(i);
                    if (i < 0) {
                        errors.Add($"{slot}: pose '{name}' not found");
                        return false;
                    }
                    return true;
                }
                case Kind.Move: {
                    int i = rig.Definition?.MoveIndex(name) ?? -1;
                    value = e.ElementType == typeof(int) ? i : rig.Definition?.MoveValue(i);
                    if (i < 0) {
                        errors.Add($"{slot}: move '{name}' not found");
                        return false;
                    }
                    return true;
                }
                default: {
                    Rig2DSolver s = rig.Solver(name);
                    if (s == null) {
                        value = null;
                        errors.Add($"{slot}: solver '{name}' not found");
                        return false;
                    }
                    if (!e.ElementType.IsInstanceOfType(s)) {
                        value = null;
                        errors.Add($"{slot}: solver '{name}' is {s.GetType().Name}, expected {e.ElementType.Name}");
                        return false;
                    }
                    value = s;
                    return true;
                }
            }
        }

        private static bool ApplyScalar(Rig2DInstance rig, object target, Entry e, List<string> errors) {
            bool hit = Resolve(rig, e, e.Names[0], e.Display, errors, out object value);
            if (!e.TrySet(target, value)) {
                errors.Add($"{e.Display}: no setter");
                return false;
            }
            return hit;
        }

        private static Array PrepareArray(object target, Entry e, List<string> errors, params int[] lengths) {
            Array arr = e.Get(target) as Array;
            bool reuse = arr != null && arr.Rank == lengths.Length && arr.GetType() == e.MemberType;
            for (int d = 0; reuse && d < lengths.Length; d++) {
                reuse = arr.GetLength(d) == lengths[d];
            }
            if (reuse) {
                return arr;
            }
            arr = Array.CreateInstance(e.ElementType, lengths);
            if (!e.TrySet(target, arr)) {
                errors.Add($"{e.Display}: existing array has the wrong size and the member has no setter");
                return null;
            }
            return arr;
        }

        private static bool ApplyArray1(Rig2DInstance rig, object target, Entry e, List<string> errors) {
            int n = e.Count > 0 ? e.Count : e.Names.Length;
            Array arr = PrepareArray(target, e, errors, n);
            if (arr == null) {
                return false;
            }
            bool ok = true;
            for (int i = 0; i < n; i++) {
                string name = e.Count > 0 ? Expand(e.Names[0], e.Start + i) : e.Names[i];
                ok &= Resolve(rig, e, name, $"{e.Display}[{i}]", errors, out object value);
                arr.SetValue(value, i);
            }
            return ok;
        }

        private static bool ApplyArray2(Rig2DInstance rig, object target, Entry e, List<string> errors) {
            int rows = e.Count;
            int cols = e.Names.Length;
            Array arr = PrepareArray(target, e, errors, rows, cols);
            if (arr == null) {
                return false;
            }
            bool ok = true;
            for (int i = 0; i < rows; i++) {
                for (int k = 0; k < cols; k++) {
                    string name = Expand(e.Names[k], e.Start + i);
                    ok &= Resolve(rig, e, name, $"{e.Display}[{i},{k}]", errors, out object value);
                    arr.SetValue(value, i, k);
                }
            }
            return ok;
        }
    }
}
