using Microsoft.Xna.Framework;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using Terraria.ObjectData;
using Terraria.Social;

namespace InnoVault
{
    /// <summary>
    /// 一个通用的方法库
    /// </summary>
    public static class VaultUtils
    {
        #region Math

        /// <summary>
        /// 表示缓动曲线的封装
        /// </summary>
        public class CurveEase
        {
            private Func<float, float> _function;

            /// <summary>
            /// 构造函数，初始化一个缓动曲线对象
            /// </summary>
            /// <param name="func">用于定义缓动曲线的函数</param>
            public CurveEase(Func<float, float> func) => _function = func;

            /// <summary>
            /// 根据传入的时间参数计算缓动曲线的值
            /// </summary>
            /// <param name="time">时间参数，通常在0到1之间</param>
            /// <returns>根据缓动函数计算出的结果值</returns>
            public float Ease(float time) => _function(time);
        }

        /// <summary>
        /// 二次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static readonly CurveEase EaseQuadIn = new CurveEase((float x) => { return x * x; });

        /// <summary>
        /// 二次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static readonly CurveEase EaseQuadOut = new CurveEase((float x) => { return 1f - EaseQuadIn.Ease(1f - x); });

        /// <summary>
        /// 二次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static readonly CurveEase EaseQuadInOut = new CurveEase((float x)
            => { return (x < 0.5f) ? 2f * x * x : -2f * x * x + 4f * x - 1f; });

        /// <summary>
        /// 三次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static readonly CurveEase EaseCubicIn = new CurveEase((float x) => { return x * x * x; });

        /// <summary>
        /// 三次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static readonly CurveEase EaseCubicOut = new CurveEase((float x) => { return 1f - EaseCubicIn.Ease(1f - x); });

        /// <summary>
        /// 三次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static readonly CurveEase EaseCubicInOut = new CurveEase((float x)
            => { return (x < 0.5f) ? 4f * x * x * x : 4f * x * x * x - 12f * x * x + 12f * x - 3f; });

        /// <summary>
        /// 四次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static readonly CurveEase EaseQuarticIn = new CurveEase((float x) => { return x * x * x * x; });

        /// <summary>
        /// 四次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static readonly CurveEase EaseQuarticOut = new CurveEase((float x) => { return 1f - EaseQuarticIn.Ease(1f - x); });

        /// <summary>
        /// 四次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static readonly CurveEase EaseQuarticInOut = new CurveEase((float x)
            => { return (x < 0.5f) ? 8f * x * x * x * x : -8f * x * x * x * x + 32f * x * x * x - 48f * x * x + 32f * x - 7f; });

        /// <summary>
        /// 五次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static readonly CurveEase EaseQuinticIn = new CurveEase((float x) => { return x * x * x * x * x; });

        /// <summary>
        /// 五次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static readonly CurveEase EaseQuinticOut = new CurveEase((float x) => { return 1f - EaseQuinticIn.Ease(1f - x); });

        /// <summary>
        /// 五次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static readonly CurveEase EaseQuinticInOut = new CurveEase((float x)
            => { return (x < 0.5f) ? 16f * x * x * x * x * x : 16f * x * x * x * x * x - 80f * x * x * x * x + 160f * x * x * x - 160f * x * x + 80f * x - 15f; });

        /// <summary>
        /// 圆形缓动的入场效果
        /// </summary>
        /// <remarks>该曲线模拟圆形运动，呈加速状态</remarks>
        public static readonly CurveEase EaseCircularIn = new CurveEase((float x) => { return 1f - (float)Math.Sqrt(1.0 - Math.Pow(x, 2)); });

        /// <summary>
        /// 圆形缓动的出场效果
        /// </summary>
        /// <remarks>该曲线模拟圆形运动，呈减速状态</remarks>
        public static readonly CurveEase EaseCircularOut = new CurveEase((float x) => { return (float)Math.Sqrt(1.0 - Math.Pow(x - 1.0, 2)); });

        /// <summary>
        /// 圆形缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速，模拟圆形运动</remarks>
        public static readonly CurveEase EaseCircularInOut = new CurveEase((float x)
            => { return (x < 0.5f) ? (1f - (float)Math.Sqrt(1.0 - Math.Pow(x * 2, 2))) * 0.5f : (float)((Math.Sqrt(1.0 - Math.Pow(-2 * x + 2, 2)) + 1) * 0.5); });

        #endregion

        #region System
        /// <summary>
        /// 检查指定玩家是否按下了鼠标键
        /// </summary>
        /// <param name="player">要检查的玩家</param>
        /// <param name="leftCed">是否检查左鼠标键，否则检测右鼠标键</param>
        /// <param name="netCed">是否进行网络同步检查</param>
        /// <returns>如果按下了指定的鼠标键，则返回true，否则返回false</returns>
        public static bool PressKey(this Player player, bool leftCed = true, bool netCed = true) {
            return (!netCed || Main.myPlayer == player.whoAmI) && (leftCed ? PlayerInput.Triggers.Current.MouseLeft : PlayerInput.Triggers.Current.MouseRight);
        }

        /// <summary>
        /// 根据当前环境实现网页重定向如果是在Steam环境中，则会通过Steam的内置浏览器打开网页，
        /// 否则使用默认浏览器打开指定的URL
        /// </summary>
        /// <param name="str">要重定向的网页URL</param>
        /// <param name="inSteam">是否在Steam环境下打开（默认为true）</param>
        public static void WebRedirection(this string str, bool inSteam = true) {
            if (SocialAPI.Mode == SocialMode.Steam && inSteam) {
                // 如果当前运行环境为Steam，并且指定在Steam中打开，则使用Steam内置浏览器
                SteamFriends.ActivateGameOverlayToWebPage(str);
            }
            else {
                // 否则使用系统默认浏览器打开网页
                Utils.OpenToURL(str);
            }
        }

        /// <summary>
        /// 在给定的 Mod 数组中查找包含指定类型的 Mod 实例
        /// </summary>
        /// <param name="type">要查找的类型</param>
        /// <param name="mods">Mod 实例的数组，用于搜索包含该类型的 Mod</param>
        /// <returns>如果找到包含该类型的 Mod，返回对应的 Mod 实例；否则返回 null</returns>
        public static Mod FindModByType(Type type, Mod[] mods) {
            foreach (var mod in mods) {
                Type[] fromModCodeTypes = AssemblyManager.GetLoadableTypes(mod.Code);
                if (fromModCodeTypes.Contains(type)) {
                    return mod;
                }
            }
            return null;
        }

        /// <summary>
        /// 尝试将指定类型与其所属的 Mod 映射关系添加到字典中
        /// 如果无法找到与该类型对应的 Mod，会抛出异常
        /// </summary>
        /// <param name="typeByModHas">存储类型到 Mod 映射关系的字典</param>
        /// <param name="type">需要映射的类型</param>
        /// <param name="mods">当前已加载的所有 Mod 列表</param>
        /// <exception cref="ArgumentNullException">当无法找到与类型对应的 Mod 时，抛出异常</exception>
        public static void AddTypeModAssociation(Dictionary<Type, Mod> typeByModHas, Type type, Mod[] mods) {
            Mod mod = FindModByType(type, mods);
            if (mod != null) {
                typeByModHas.Add(type, mod);
            }
            else {
                string errorText = "试图添加其所属模组映射时，所属模组为Null";
                string errorText2 = "Attempted to add its associated mod mapping, but the associated mod is null";
                throw new ArgumentNullException(nameof(type), Translation(errorText, errorText2));
            }
        }

        /// <summary>
        /// 获取所有已加载Mod的代码中的所有类型返回一个包含所有类型的数组，
        /// 可以用于反射操作或动态类型检测
        /// </summary>
        /// <returns>所有Mod代码中的可加载类型数组</returns>
        public static Type[] GetAnyModCodeType() {
            // 创建一个存储所有类型的列表
            List<Type> types = new List<Type>();
            Mod[] mods = ModLoader.Mods;

            // 使用 LINQ 将每个Mod的代码程序集中的所有可加载类型平铺到一个集合中
            types.AddRange(mods.SelectMany(mod => AssemblyManager.GetLoadableTypes(mod.Code)));
            return types.ToArray();
        }

        /// <summary>
        /// 获取指定基类的所有子类列表
        /// </summary>
        /// <param name="baseType">基类的类型</param>
        /// <returns>子类列表</returns>
        public static List<Type> GetSubclassTypeList(Type baseType) {
            List<Type> subclasses = [];
            Type[] allTypes = GetAnyModCodeType();

            foreach (Type type in allTypes) {
                if (type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type)) {
                    subclasses.Add(type);
                }
            }

            return subclasses;
        }

        /// <summary>
        /// 根据给定的类型列表，创建符合条件的类型实例，并将实例添加到输出列表中，该方法默认要求类型拥有无参构造
        /// </summary>
        public static List<T> GetSubclassInstances<T>(bool parameterless = true) {
            List<Type> inTypes = GetSubclassTypeList(typeof(T));
            List<T> outInds = [];
            foreach (Type type in inTypes) {
                if (type != typeof(T)) {
                    object obj = parameterless ? Activator.CreateInstance(type) : RuntimeHelpers.GetUninitializedObject(type);
                    if (obj is T inds) {
                        outInds.Add(inds);
                    }
                }
            }
            return outInds;
        }

        /// <summary>
        /// 获取当前程序集（Assembly）中实现了指定接口（通过接口名称 `lname` 指定）的所有类的实例列表
        /// </summary>
        /// <typeparam name="T">接口类型，用于检查类是否实现该接口</typeparam>
        /// <returns>一个包含所有实现了指定接口的类实例的列表</returns>
        public static List<T> GetSubInterface<T>() {
            string lname = typeof(T).Name;
            List<T> subInterface = new List<T>();
            Type[] allTypes = GetAnyModCodeType();

            foreach (Type type in allTypes) {
                if (type.IsClass && !type.IsAbstract && type.GetInterface(lname) != null) {
                    object obj = RuntimeHelpers.GetUninitializedObject(type);
                    if (obj is T instance) {
                        subInterface.Add(instance);
                    }
                }
            }

            return subInterface;
        }

        #endregion

        #region Game

        /// <summary>
        /// 在游戏中发送文本消息
        /// </summary>
        /// <param name="message">要发送的消息文本</param>
        /// <param name="colour">（可选）消息的颜色,默认为 null</param>
        public static void Text(string message, Color? colour = null) {
            Color newColor = (Color)(colour == null ? Color.White : colour);
            if (Main.netMode == NetmodeID.Server) {
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), (Color)(colour == null ? Color.White : colour));
                return;
            }
            Main.NewText(message, newColor);
        }

        /// <summary>
        /// 一个根据语言选项返回字符的方法
        /// </summary>
        public static string Translation(string Chinese = null, string English = null, string Spanish = null, string Russian = null) {
            string text = default(string);

            if (English == null) {
                English = "Invalid Character";
            }

            switch (Language.ActiveCulture.LegacyId) {
                case (int)GameCulture.CultureName.Chinese:
                    text = Chinese;
                    break;
                case (int)GameCulture.CultureName.Russian:
                    text = Russian;
                    break;
                case (int)GameCulture.CultureName.Spanish:
                    text = Spanish;
                    break;
                case (int)GameCulture.CultureName.English:
                    text = English;
                    break;
                default:
                    text = English;
                    break;
            }

            return text;
        }
        /// <summary>
        /// 按比例混合输出颜色
        /// </summary>
        /// <param name="percent"></param>
        /// <param name="colors"></param>
        /// <returns></returns>
        public static Color MultiStepColorLerp(float percent, params Color[] colors) {
            if (colors == null) {
                Text("MultiLerpColor: 空的颜色数组!");
                return Color.White;
            }
            float per = 1f / (colors.Length - 1f);
            float total = per;
            int currentID = 0;
            while (percent / total > 1f && currentID < colors.Length - 2) {
                total += per;
                currentID++;
            }
            return Color.Lerp(colors[currentID], colors[currentID + 1], (percent - (per * currentID)) / per);
        }

        /// <summary>
        /// 销毁关于傀儡的物块结构
        /// </summary>
        /// <param name="point16">输入这个傀儡所占图格的任意一点</param>
        public static void KillPuppet(Point16 point16) {
            if (SafeGetTopLeft(point16.X, point16.Y, out Point16 tilePos)) {
                TileObjectData data = TileObjectData.GetTileData(Main.tile[point16]);
                for (int i = 0; i < data.Width; i++) {
                    for (int j = 0; j < data.Height; j++) {
                        Point16 newPoint = point16 + new Point16(i, j);
                        Tile newTile = Main.tile[newPoint];
                        if (newTile.TileType == 378) {
                            newTile.TileType = 0;
                            newTile.HasTile = false;
                            WorldGen.SquareTileFrame(newPoint.X, newPoint.Y);
                        }
                    }
                }
            }
        }

        #endregion

        #region Net

        /// <summary>
        /// 判断是否处于客户端状态，如果是在单人或者服务端下将返回false
        /// </summary>
        public static bool isClient => Main.netMode == NetmodeID.MultiplayerClient;
        /// <summary>
        /// 判断是否处于服务端状态，如果是在单人或者客户端下将返回false
        /// </summary>
        public static bool isServer => Main.netMode == NetmodeID.Server;
        /// <summary>
        /// 仅判断是否处于单人状态，在单人模式下返回true
        /// </summary>
        public static bool isSinglePlayer => Main.netMode == NetmodeID.SinglePlayer;

        /// <summary>
        /// 检查一个 Projectile 对象是否属于当前客户端玩家拥有的，如果是，返回true
        /// </summary>
        public static bool IsOwnedByLocalPlayer(this Projectile projectile) => projectile.owner == Main.myPlayer;

        internal static void WritePoint16(this BinaryWriter writer, Point16 point16) {
            writer.Write(point16.X);
            writer.Write(point16.Y);
        }

        internal static Point16 ReadPoint16(this BinaryReader reader) => new Point16(reader.ReadInt16(), reader.ReadInt16());
        #endregion

        #region Tile
        /// <summary>
        /// 获取给定坐标的物块左上角位置，并判断该位置是否为多结构物块的左上角
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <returns>
        /// 如果物块存在并且位于一个多结构物块的左上角，返回其左上角坐标，否则返回null
        /// </returns>
        public static Point16? GetTopLeftOrNull(int i, int j) {
            // 获取给定坐标的物块
            Tile tile = Framing.GetTileSafely(i, j);

            // 如果没有物块，返回null
            if (!tile.HasTile)
                return null;

            // 获取物块的数据结构，如果为null则认为是单个物块
            TileObjectData data = TileObjectData.GetTileData(tile);

            // 如果是单个物块，直接返回当前坐标
            if (data == null)
                return new Point16(i, j);

            // 计算物块的帧位置偏移量
            int frameX = tile.TileFrameX % (data.Width * 18);
            int frameY = tile.TileFrameY % (data.Height * 18);

            // 计算左上角的位置
            int topLeftX = i - (frameX / 18);
            int topLeftY = j - (frameY / 18);

            // 返回左上角位置
            return new Point16(topLeftX, topLeftY);
        }

        /// <summary>
        /// 判断给定坐标是否为多结构物块的左上角位置，并输出左上角的坐标。
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <param name="point">输出的左上角坐标，如果不是左上角则为(0,0)</param>
        /// <returns>如果是左上角，返回true，否则返回false。</returns>
        public static bool IsTopLeft(int i, int j, out Point16 point) {
            // 使用合并后的函数获取左上角位置
            Point16? topLeft = GetTopLeftOrNull(i, j);

            // 如果没有有效的左上角坐标，返回false，并将输出参数设为(0, 0)
            if (!topLeft.HasValue) {
                point = new Point16(0, 0);
                return false;
            }

            // 获取左上角的实际坐标
            point = topLeft.Value;

            // 如果左上角位置与当前坐标相同，说明是左上角
            return point.X == i && point.Y == j;
        }

        /// <summary>
        /// 安全的获取多结构物块左上角的位置
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        public static bool SafeGetTopLeft(int i, int j, out Point16 point) {
            Point16? topLeft = GetTopLeftOrNull(i, j);
            if (topLeft.HasValue) {
                point = topLeft.Value;
                return true;
            }
            else {
                point = new Point16(0, 0);
                return false;
            }
        }

        /// <summary>
        /// 获取一个物块目标，输入世界物块坐标，自动考虑收界情况
        /// </summary>
        public static Tile GetTile(int i, int j) {
            return GetTile(new Vector2(i, j));
        }

        /// <summary>
        /// 获取一个物块目标，输入世界物块坐标，自动考虑收界情况
        /// </summary>
        public static Tile GetTile(Vector2 pos) {
            pos = PTransgressionTile(pos);
            return Main.tile[(int)pos.X, (int)pos.Y];
        }

        /// <summary>
        /// 将可能越界的方块坐标收值为非越界坐标
        /// </summary>
        public static Vector2 PTransgressionTile(Vector2 TileVr, int L = 0, int R = 0, int D = 0, int S = 0) {
            if (TileVr.X > Main.maxTilesX - R) {
                TileVr.X = Main.maxTilesX - R;
            }
            if (TileVr.X < 0 + L) {
                TileVr.X = 0 + L;
            }
            if (TileVr.Y > Main.maxTilesY - S) {
                TileVr.Y = Main.maxTilesY - S;
            }
            if (TileVr.Y < 0 + D) {
                TileVr.Y = 0 + D;
            }
            return new Vector2(TileVr.X, TileVr.Y);
        }
        #endregion
    }
}
