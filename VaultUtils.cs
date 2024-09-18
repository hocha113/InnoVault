using Microsoft.Xna.Framework;
using Steamworks;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Social;
using Terraria.ModLoader.Core;
using System.Linq;
using Terraria.DataStructures;
using Terraria.ObjectData;
using System.IO;

namespace InnoVault
{
    /// <summary>
    /// 一个通用的方法库
    /// </summary>
    public static class VaultUtils
    {
        /// <summary>
        /// 求一个点到另一个点的向量
        /// </summary>
        /// <param name="vr1"></param>
        /// <param name="vr2"></param>
        /// <returns></returns>
        public static Vector2 To(this Vector2 vr1, Vector2 vr2) => vr2 - vr1;

        #region System

        public static void WebRedirection(this string str, bool inSteam = true) {
            if (SocialAPI.Mode == SocialMode.Steam && inSteam) {
                SteamFriends.ActivateGameOverlayToWebPage(str);
            }
            else {
                Utils.OpenToURL(str);
            }
        }

        public static Type[] GetAnyModCodeType() {
            List<Type> types = new List<Type>();
            Mod[] mods = ModLoader.Mods;
            // 使用 LINQ 将所有类型平铺到一个集合中
            types.AddRange(mods.SelectMany(mod => AssemblyManager.GetLoadableTypes(mod.Code)));
            return types.ToArray();
        }

        /// <summary>
        /// 获取指定基类的所有子类列表
        /// </summary>
        /// <param name="baseType">基类的类型</param>
        /// <returns>子类列表</returns>
        public static List<Type> GetSubclasses(Type baseType) {
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
        public static List<T> HanderSubclass<T>(bool parameterless = true) {
            List<Type> inTypes = GetSubclasses(typeof(T));
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
        /// 获取当前程序集（Assembly）中实现了接口 `T` 的所有类的实例列表
        /// </summary>
        /// <typeparam name="T">要查找实现类的接口类型</typeparam>
        /// <returns>一个包含所有实现了指定接口 `T` 的类实例的列表</returns>
        public static List<T> GetSubInterface<T>() => GetSubInterface<T>(typeof(T).Name);

        /// <summary>
        /// 获取当前程序集（Assembly）中实现了指定接口（通过接口名称 `lname` 指定）的所有类的实例列表
        /// </summary>
        /// <typeparam name="T">接口类型，用于检查类是否实现该接口</typeparam>
        /// <param name="lname">接口的名称，用于匹配实现类</param>
        /// <returns>一个包含所有实现了指定接口的类实例的列表</returns>
        public static List<T> GetSubInterface<T>(string lname) {
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
