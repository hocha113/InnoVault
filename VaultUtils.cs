using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
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
        /// 二次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static float EaseQuadIn(float x) => x * x;

        /// <summary>
        /// 二次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static float EaseQuadOut(float x) => 1f - EaseQuadIn(1f - x);

        /// <summary>
        /// 二次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static float EaseQuadInOut(float x) => (x < 0.5f) ? 2f * x * x : -2f * x * x + 4f * x - 1f;

        /// <summary>
        /// 三次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static float EaseCubicIn(float x) => x * x * x;

        /// <summary>
        /// 三次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static float EaseCubicOut(float x) => 1f - EaseCubicIn(1f - x);

        /// <summary>
        /// 三次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static float EaseCubicInOut(float x) => (x < 0.5f) ? 4f * x * x * x : 4f * x * x * x - 12f * x * x + 12f * x - 3f;

        /// <summary>
        /// 四次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static float EaseQuarticIn(float x) => x * x * x * x;

        /// <summary>
        /// 四次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static float EaseQuarticOut(float x) => 1f - EaseQuarticIn(1f - x);

        /// <summary>
        /// 四次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static float EaseQuarticInOut(float x) => (x < 0.5f) ? 8f * x * x * x * x : -8f * x * x * x * x + 32f * x * x * x - 48f * x * x + 32f * x - 7f;

        /// <summary>
        /// 五次缓动的入场效果
        /// </summary>
        /// <remarks>该曲线呈加速状态</remarks>
        public static float EaseQuinticIn(float x) => x * x * x * x * x;

        /// <summary>
        /// 五次缓动的出场效果
        /// </summary>
        /// <remarks>该曲线呈减速状态</remarks>
        public static float EaseQuinticOut(float x) => 1f - EaseQuinticIn(1f - x);

        /// <summary>
        /// 五次缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速</remarks>
        public static float EaseQuinticInOut(float x) => (x < 0.5f) ? 16f * x * x * x * x * x : 16f * x * x * x * x * x - 80f * x * x * x * x + 160f * x * x * x - 160f * x * x + 80f * x - 15f;

        /// <summary>
        /// 圆形缓动的入场效果
        /// </summary>
        /// <remarks>该曲线模拟圆形运动，呈加速状态</remarks>
        public static float EaseCircularIn(float x) => 1f - (float)Math.Sqrt(1.0 - Math.Pow(x, 2));

        /// <summary>
        /// 圆形缓动的出场效果
        /// </summary>
        /// <remarks>该曲线模拟圆形运动，呈减速状态</remarks>
        public static float EaseCircularOut(float x) => (float)Math.Sqrt(1.0 - Math.Pow(x - 1.0, 2));

        /// <summary>
        /// 圆形缓动的入场和出场效果
        /// </summary>
        /// <remarks>前半段为加速，后半段为减速，模拟圆形运动</remarks>
        public static float EaseCircularInOut(float x) => (x < 0.5f) ? (1f - (float)Math.Sqrt(1.0 - Math.Pow(x * 2, 2))) * 0.5f : (float)((Math.Sqrt(1.0 - Math.Pow(-2 * x + 2, 2)) + 1) * 0.5);

        /// <summary>
        /// 两点间简略取值
        /// </summary>
        /// <param name="vr1"></param>
        /// <param name="vr2"></param>
        /// <returns></returns>
        public static Vector2 To(this Vector2 vr1, Vector2 vr2) => vr2 - vr1;

        /// <summary>
        /// 将一个二维函数填充为三维
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static Vector3 ToVector3(this Vector2 vector) => new Vector3(vector.X, vector.Y, 0);

        /// <summary>
        /// 将一个二维函数填充为三维
        /// </summary>
        /// <param name="vector"></param>
        /// <param name="fullZ">需要填充的Z轴向量</param>
        /// <returns></returns>
        public static Vector3 ToVector3(this Vector2 vector, float fullZ) => new Vector3(vector.X, vector.Y, fullZ);

        /// <summary>
        /// 简单安全的获取一个单位向量，如果出现非法情况则会返回 <see cref="Vector2.Zero"/>
        /// </summary>
        public static Vector2 UnitVector(this Vector2 vr) => vr.SafeNormalize(Vector2.Zero);

        /// <summary>
        /// 获取一个垂直于该向量的单位向量
        /// </summary>
        public static Vector2 GetNormalVector(this Vector2 vr) {
            Vector2 nVr = new(vr.Y, -vr.X);
            return Vector2.Normalize(nVr);
        }

        /// <summary>
        /// 色彩混合
        /// </summary>
        /// <param name="percent"></param>
        /// <param name="colors"></param>
        /// <returns></returns>
        public static Color MultiStepColorLerp(float percent, params Color[] colors) {
            if (colors == null || colors.Length == 0) {
                return Color.White;
            }
            if (colors.Length == 1) {
                return colors[0];
            }

            percent = MathHelper.Clamp(percent, 0f, 1f);
            int lastIndex = colors.Length - 1;
            float per = 1f / lastIndex;

            int currentID = Math.Min((int)(percent / per), lastIndex - 1); // 防止索引溢出
            float lerpFactor = (percent - per * currentID) / per;

            return Color.Lerp(colors[currentID], colors[currentID + 1], lerpFactor);
        }

        /// <summary>
        /// 检测一个圆形是否与目标矩形相交
        /// </summary>
        /// <param name="circleCenter">圆心的坐标</param>
        /// <param name="radius">圆的半径</param>
        /// <param name="targetRectangle">目标矩形的边界框</param>
        /// <returns>返回 true 如果圆形与矩形相交，否则返回 false</returns>
        public static bool CircleIntersectsRectangle(Vector2 circleCenter, float radius, Rectangle targetRectangle) {
            // 计算矩形最近点到圆心的距离
            float nearestX = MathHelper.Clamp(circleCenter.X, targetRectangle.Left, targetRectangle.Right);
            float nearestY = MathHelper.Clamp(circleCenter.Y, targetRectangle.Top, targetRectangle.Bottom);

            // 检测最近点与圆心的距离是否小于等于半径
            float deltaX = circleCenter.X - nearestX;
            float deltaY = circleCenter.Y - nearestY;

            return (deltaX * deltaX + deltaY * deltaY) <= (radius * radius);
        }

        /// <summary>
        /// 创建一个矩形
        /// </summary>
        public static Rectangle GetRectangle(this Vector2 topLeft, int width, int height) => new Rectangle((int)topLeft.X, (int)topLeft.Y, width, height);
        /// <summary>
        /// 创建一个矩形
        /// </summary>
        public static Rectangle GetRectangle(this Vector2 topLeft, int size) => new Rectangle((int)topLeft.X, (int)topLeft.Y, size, size);
        /// <summary>
        /// 创建一个矩形
        /// </summary>
        public static Rectangle GetRectangle(this Vector2 topLeft, Vector2 size) => new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)size.X, (int)size.Y);

        #endregion

        #region System
        /// <summary>
        /// 生成乱码字符串
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public static string GenerateRandomString(int length) {
            const string characters = "!@#$%^&*()-_=+[]{}|;:'\",.<>/?`~0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            char[] result = new char[length];

            for (int i = 0; i < length; i++) {
                result[i] = characters[Main.rand.Next(characters.Length)];
            }

            return new string(result);
        }

        /// <summary>
        /// 按照给定的进度逐步显示文本的一部分
        /// </summary>
        /// <param name="text">要显示的完整文本</param>
        /// <param name="progress">显示进度，范围从 0.0f 到 1.0f，0.0f 表示没有字符，1.0f 表示完整显示</param>
        /// <returns>根据进度显示的部分文本</returns>
        public static string GetTextProgressively(string text, float progress) {
            if (string.IsNullOrEmpty(text)) {
                return string.Empty;
            }
            progress = MathHelper.Clamp(progress, 0.0f, 1.0f);
            int charCountToShow = (int)(text.Length * progress);
            return text.Substring(0, charCountToShow);
        }

        /// <summary>
        /// 将输入文本自动换行以适应指定的最大宽度
        /// </summary>
        /// <param name="text">原始文本</param>
        /// <param name="textSize">文本尺寸（通常由字体测量得出）</param>
        /// <param name="maxWidth">允许的最大宽度</param>
        /// <returns>已处理的自动换行文本</returns>
        public static string WrapTextToWidth(string text, Vector2 textSize, float maxWidth) {
            // 将文本转换为字符列表
            List<char> characters = text.ToList();
            List<char> wrappedText = new List<char>();
            float currentWidth = 0;
            float charWidth;

            // 遍历每个字符，处理宽度限制与换行逻辑
            foreach (char character in characters) {
                // 计算每个字符的平均宽度（假设等宽字体或字符均匀分布）
                charWidth = textSize.X / text.Length;

                // 遇到换行符，直接重置当前宽度并加入换行符
                if (character == '\n') {
                    wrappedText.Add(character);
                    currentWidth = 0;
                }
                else {
                    // 如果添加当前字符后宽度超出最大限制，插入换行符
                    if (currentWidth + charWidth > maxWidth) {
                        wrappedText.Add('\n');
                        currentWidth = 0;
                    }

                    // 添加字符并更新当前宽度
                    wrappedText.Add(character);
                    currentWidth += charWidth;
                }
            }

            // 返回处理后的文本
            return new string(wrappedText.ToArray());
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
        /// 根据<see cref="Item.useAmmo"/>映射到对应的物品id之上
        /// </summary>
        public static Dictionary<int, int> AmmoIDToItemIDMapping { get; private set; } = new Dictionary<int, int>
        {
            { AmmoID.FallenStar, ItemID.FallenStar },
            { AmmoID.Gel, ItemID.Gel },
            { AmmoID.Arrow, ItemID.WoodenArrow },
            { AmmoID.Coin, ItemID.CopperCoin },
            { AmmoID.Bullet, ItemID.MusketBall },
            { AmmoID.Sand, ItemID.SandBlock },
            { AmmoID.Dart, ItemID.PoisonDart },
            { AmmoID.Rocket, ItemID.RocketI },
            { AmmoID.Flare, ItemID.Flare },
            { AmmoID.Snowball, ItemID.Snowball },
            { AmmoID.StyngerBolt, ItemID.StyngerBolt },
            { AmmoID.CandyCorn, ItemID.CandyCorn },
            { AmmoID.JackOLantern, ItemID.JackOLantern },
            { AmmoID.Stake, ItemID.Stake },
            { AmmoID.NailFriendly, ItemID.Nail },
            { AmmoID.None, ItemID.MusketBall },
        };

        /// <summary>
        /// 让一个射弹安全的对应到弹药物品
        /// </summary>
        public static Dictionary<int, int> ProjectileToSafeAmmoMap { get; private set; } = new Dictionary<int, int>() {
            { ProjectileID.BoneArrow, ItemID.BoneArrow},
            { ProjectileID.MoonlordArrow, ItemID.MoonlordArrow},
            { ProjectileID.ChlorophyteArrow, ItemID.ChlorophyteArrow},
            { ProjectileID.CursedArrow, ItemID.CursedArrow},
            { ProjectileID.FlamingArrow, ItemID.FlamingArrow},
            { ProjectileID.FrostburnArrow, ItemID.FrostburnArrow},
            { ProjectileID.HellfireArrow, ItemID.HellfireArrow},
            { ProjectileID.HolyArrow, ItemID.HolyArrow},
            { ProjectileID.IchorArrow, ItemID.IchorArrow},
            { ProjectileID.JestersArrow, ItemID.JestersArrow},
            { ProjectileID.ShimmerArrow, ItemID.ShimmerArrow},
            { ProjectileID.UnholyArrow, ItemID.UnholyArrow},
            { ProjectileID.VenomArrow, ItemID.VenomArrow},
            { ProjectileID.WoodenArrowFriendly, ItemID.WoodenArrow},
            { ProjectileID.ChumBucket, ItemID.ChumBucket},
            { ProjectileID.ChlorophyteBullet, ItemID.ChlorophyteBullet},
            { ProjectileID.CrystalBullet, ItemID.CrystalBullet},
            { ProjectileID.CursedBullet, ItemID.CursedBullet},
            { ProjectileID.ExplosiveBullet, ItemID.ExplodingBullet},
            { ProjectileID.GoldenBullet, ItemID.GoldenBullet},
            { ProjectileID.BulletHighVelocity, ItemID.HighVelocityBullet},
            { ProjectileID.IchorBullet, ItemID.IchorBullet},
            { ProjectileID.MoonlordBullet, ItemID.MoonlordBullet},
            { ProjectileID.MeteorShot, ItemID.MeteorShot},
            { ProjectileID.Bullet, ItemID.MusketBall},
            { ProjectileID.NanoBullet, ItemID.NanoBullet},
            { ProjectileID.PartyBullet, ItemID.PartyBullet},
            { ProjectileID.SilverBullet, ItemID.SilverBullet},
            { ProjectileID.VenomBullet, ItemID.VenomBullet},
            { ProjectileID.SnowBallFriendly, ItemID.Snowball},
        };

        /// <summary>
        /// 火箭弹药映射字典
        /// </summary>
        public static Dictionary<int, int> RocketAmmoMap { get; private set; } = new Dictionary<int, int>() {
            { ItemID.RocketI, ProjectileID.RocketI },
            { ItemID.RocketII, ProjectileID.RocketII },
            { ItemID.RocketIII, ProjectileID.RocketIII },
            { ItemID.RocketIV, ProjectileID.RocketIV },
            { ItemID.ClusterRocketI, ProjectileID.ClusterRocketI },
            { ItemID.ClusterRocketII, ProjectileID.ClusterRocketII },
            { ItemID.DryRocket, ProjectileID.DryRocket },
            { ItemID.WetRocket, ProjectileID.WetRocket },
            { ItemID.HoneyRocket, ProjectileID.HoneyRocket },
            { ItemID.LavaRocket, ProjectileID.LavaRocket },
            { ItemID.MiniNukeI, ProjectileID.MiniNukeRocketI },
            { ItemID.MiniNukeII, ProjectileID.MiniNukeRocketII },
        };

        /// <summary>
        /// 雪人类弹药映射字典
        /// </summary>
        public static Dictionary<int, int> SnowmanCannonAmmoMap { get; private set; } = new Dictionary<int, int>() {
            { ItemID.RocketI, ProjectileID.RocketSnowmanI },
            { ItemID.RocketII, ProjectileID.RocketSnowmanII },
            { ItemID.RocketIII, ProjectileID.RocketSnowmanIII },
            { ItemID.RocketIV, ProjectileID.RocketSnowmanIV },
            { ItemID.ClusterRocketI, ProjectileID.ClusterSnowmanRocketI },
            { ItemID.ClusterRocketII, ProjectileID.ClusterSnowmanRocketII },
            { ItemID.DryRocket, ProjectileID.DrySnowmanRocket },
            { ItemID.WetRocket, ProjectileID.WetSnowmanRocket },
            { ItemID.HoneyRocket, ProjectileID.HoneySnowmanRocket },
            { ItemID.LavaRocket, ProjectileID.LavaSnowmanRocket },
            { ItemID.MiniNukeI, ProjectileID.MiniNukeSnowmanRocketI },
            { ItemID.MiniNukeII, ProjectileID.MiniNukeSnowmanRocketII },
        };

        /// <summary>
        /// 获取生成源
        /// </summary>
        public static IEntitySource FromObjectGetParent(this object obj) {
            if (obj is Projectile projectile) {
                return projectile.GetSource_FromAI();
            }
            if (obj is NPC nPC) {
                return nPC.GetSource_FromAI();
            }
            if (obj is Player player) {
                return player.GetSource_FromAI();
            }
            if (obj is Item item) {
                return item.GetSource_FromAI();
            }
            return new EntitySource_Parent(Main.LocalPlayer, "NullSource");
        }

        /// <summary>
        /// 寻找距离指定位置最近的NPC
        /// </summary>
        /// <param name="origin">开始搜索的位置</param>
        /// <param name="maxDistanceToCheck">搜索NPC的最大距离</param>
        /// <param name="ignoreTiles">在检查障碍物时是否忽略瓦片</param>
        /// <param name="bossPriority">是否优先选择Boss</param>
        /// <param name="onHitNPCs">排除的NPC列表</param>
        /// <param name="chasedByNPC">额外的条件过滤，用于判断NPC是否应被考虑，如果该委托不为<see langword="null"/> 那么它的返回值将覆盖其他的筛选结果</param>
        /// <returns>距离最近的NPC</returns>
        public static NPC FindClosestNPC(this Vector2 origin, float maxDistanceToCheck, bool ignoreTiles = true
            , bool bossPriority = false, IEnumerable<NPC> onHitNPCs = null, Func<NPC, bool> chasedByNPC = null) {
            NPC closestTarget = null;
            float distance = maxDistanceToCheck;
            bool bossFound = false;

            foreach (var npc in Main.npc) {
                bool canChased = npc.CanBeChasedBy();
                if (onHitNPCs != null && onHitNPCs.Contains(npc)) {
                    canChased = false;
                }

                // Boss优先选择逻辑
                if (bossPriority && bossFound && !npc.boss && npc.type != NPCID.WallofFleshEye) {
                    canChased = false;
                }

                if (chasedByNPC != null) {
                    canChased = chasedByNPC.Invoke(npc);
                }

                if (!canChased) {
                    continue;
                }

                // 计算NPC与起点的距离
                float extraDistance = (npc.width / 2f) + (npc.height / 2f);
                float actualDistance = Vector2.Distance(origin, npc.Center);

                // 检查瓦片阻挡
                bool canHit = ignoreTiles || Collision.CanHit(origin, 1, 1, npc.Center, 1, 1);

                // 更新最近目标
                if (actualDistance < distance + extraDistance && canHit) {
                    if (bossPriority && (npc.boss || npc.type == NPCID.WallofFleshEye)) {
                        bossFound = true;
                    }
                    distance = actualDistance;
                    closestTarget = npc;
                }
            }

            return closestTarget;
        }

        /// <summary>
        /// 检测玩家是否有效且正常存活
        /// </summary>
        /// <returns>返回 true 表示活跃，返回 false 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this Player player) => player != null && player.active && !player.dead;

        /// <summary>
        /// 检测弹幕是否有效且正常存活
        /// </summary>
        /// <returns>返回 true 表示活跃，返回 false 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this Projectile projectile) => projectile != null && projectile.active && projectile.timeLeft > 0;

        /// <summary>
        /// 检测NPC是否有效且正常存活
        /// </summary>
        /// <returns>返回 true 表示活跃，返回 false 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this NPC npc) => npc != null && npc.active && npc.timeLeft > 0;

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
        /// 让一个NPC可以正常的掉落物品而不触发其他死亡事件，只应该在非服务端上调用该方法
        /// </summary>
        /// <param name="npc"></param>
        public static void DropItem(this NPC npc) {
            DropAttemptInfo dropAttemptInfo = default;
            dropAttemptInfo.player = Main.LocalPlayer;
            dropAttemptInfo.npc = npc;
            dropAttemptInfo.IsExpertMode = Main.expertMode;
            dropAttemptInfo.IsMasterMode = Main.masterMode;
            dropAttemptInfo.IsInSimulation = false;
            dropAttemptInfo.rng = Main.rand;
            DropAttemptInfo info = dropAttemptInfo;
            Main.ItemDropSolver.TryDropping(info);
        }

        /// <summary>
        /// 处理对物品集合的堆叠添加
        /// </summary>
        /// <param name="itemList"></param>
        /// <param name="itemToAdd"></param>
        public static void MergeItemStacks(List<Item> itemList, Item itemToAdd) {
            // 查找是否有相同类型的物品
            var existingItem = itemList.FirstOrDefault(i => i.type == itemToAdd.type);

            if (existingItem != null) {
                // 合并物品堆叠数
                int totalStack = existingItem.stack + itemToAdd.stack;
                int maxStack = existingItem.maxStack;

                // 如果堆叠超过最大堆叠数，分配新的堆叠物品
                while (totalStack > maxStack) {
                    existingItem.stack = maxStack;
                    totalStack -= maxStack;

                    // 创建新的物品并添加剩余堆叠
                    var newItem = itemToAdd.Clone(); // 确保不会修改原物品
                    newItem.stack = Math.Min(totalStack, newItem.maxStack);
                    itemList.Add(newItem);
                }

                // 最后将剩余堆叠数添加到现有物品
                if (totalStack > 0) {
                    existingItem.stack = totalStack;
                }
            }
            else {
                // 没有相同类型物品，直接添加
                itemList.Add(itemToAdd);
            }
        }

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
        /// 发送游戏文本并记录日志内容
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="mod"></param>
        public static void LoggerDomp(this object obj, Mod mod = null) {
            string text;
            if (obj == null) {
                text = "ERROR is Null";
            }
            else {
                text = obj.ToString();
            }

            Text(text);

            if (mod != null) {
                mod.Logger.Info(text);
            }
            else {
                VaultMod.Instance.Logger.Info(text);
            }
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

        /// <summary>
        /// 获取玩家对象一个稳定的中心位置，考虑斜坡矫正与坐骑矫正，适合用于处理手持弹幕的位置获取
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public static Vector2 GetPlayerStabilityCenter(this Player player) => player.MountedCenter.Floor() + new Vector2(0, player.gfxOffY);

        /// <summary>
        /// 计算并获取物品的前缀附加属性
        /// 根据物品的前缀ID，确定前缀所提供的各种属性加成，包括伤害倍率、击退倍率、使用时间倍率、尺寸倍率、射速倍率、法力消耗倍率以及暴击加成，
        /// 并根据这些加成计算出前缀的总体强度。对于模组的前缀，使用自定义的逻辑处理属性加成
        /// </summary>
        /// <param name="item">带有前缀的物品实例。</param>
        /// <returns>
        /// 返回包含前缀附加属性的结构体<see cref="PrefixState"/>，
        /// 该结构体中包括前缀ID以及计算得到的属性加成与前缀强度
        /// </returns>
        public static PrefixState GetPrefixState(this Item item) {
            int prefixID = item.prefix;

            PrefixState additionStruct = new PrefixState();

            float strength;
            float damageMult = 1f;
            float knockbackMult = 1f;
            float useTimeMult = 1f;
            float scaleMult = 1f;
            float shootSpeedMult = 1f;
            float manaMult = 1f;
            int critBonus = 0;

            if (prefixID >= PrefixID.Count && prefixID < PrefixLoader.PrefixCount) {
                additionStruct.isModPreFix = true;
                PrefixLoader.GetPrefix(prefixID).SetStats(ref damageMult, ref knockbackMult
                    , ref useTimeMult, ref scaleMult, ref shootSpeedMult, ref manaMult, ref critBonus);
            }
            else {
                additionStruct.isModPreFix = false;
                switch (prefixID) {
                    case 1:
                        scaleMult = 1.12f;
                        break;
                    case 2:
                        scaleMult = 1.18f;
                        break;
                    case 3:
                        damageMult = 1.05f;
                        critBonus = 2;
                        scaleMult = 1.05f;
                        break;
                    case 4:
                        damageMult = 1.1f;
                        scaleMult = 1.1f;
                        knockbackMult = 1.1f;
                        break;
                    case 5:
                        damageMult = 1.15f;
                        break;
                    case 6:
                        damageMult = 1.1f;
                        break;
                    case 81:
                        knockbackMult = 1.15f;
                        damageMult = 1.15f;
                        critBonus = 5;
                        useTimeMult = 0.9f;
                        scaleMult = 1.1f;
                        break;
                    case 7:
                        scaleMult = 0.82f;
                        break;
                    case 8:
                        knockbackMult = 0.85f;
                        damageMult = 0.85f;
                        scaleMult = 0.87f;
                        break;
                    case 9:
                        scaleMult = 0.9f;
                        break;
                    case 10:
                        damageMult = 0.85f;
                        break;
                    case 11:
                        useTimeMult = 1.1f;
                        knockbackMult = 0.9f;
                        scaleMult = 0.9f;
                        break;
                    case 12:
                        knockbackMult = 1.1f;
                        damageMult = 1.05f;
                        scaleMult = 1.1f;
                        useTimeMult = 1.15f;
                        break;
                    case 13:
                        knockbackMult = 0.8f;
                        damageMult = 0.9f;
                        scaleMult = 1.1f;
                        break;
                    case 14:
                        knockbackMult = 1.15f;
                        useTimeMult = 1.1f;
                        break;
                    case 15:
                        knockbackMult = 0.9f;
                        useTimeMult = 0.85f;
                        break;
                    case 16:
                        damageMult = 1.1f;
                        critBonus = 3;
                        break;
                    case 17:
                        useTimeMult = 0.85f;
                        shootSpeedMult = 1.1f;
                        break;
                    case 18:
                        useTimeMult = 0.9f;
                        shootSpeedMult = 1.15f;
                        break;
                    case 19:
                        knockbackMult = 1.15f;
                        shootSpeedMult = 1.05f;
                        break;
                    case 20:
                        knockbackMult = 1.05f;
                        shootSpeedMult = 1.05f;
                        damageMult = 1.1f;
                        useTimeMult = 0.95f;
                        critBonus = 2;
                        break;
                    case 21:
                        knockbackMult = 1.15f;
                        damageMult = 1.1f;
                        break;
                    case 82:
                        knockbackMult = 1.15f;
                        damageMult = 1.15f;
                        critBonus = 5;
                        useTimeMult = 0.9f;
                        shootSpeedMult = 1.1f;
                        break;
                    case 22:
                        knockbackMult = 0.9f;
                        shootSpeedMult = 0.9f;
                        damageMult = 0.85f;
                        break;
                    case 23:
                        useTimeMult = 1.15f;
                        shootSpeedMult = 0.9f;
                        break;
                    case 24:
                        useTimeMult = 1.1f;
                        knockbackMult = 0.8f;
                        break;
                    case 25:
                        useTimeMult = 1.1f;
                        damageMult = 1.15f;
                        critBonus = 1;
                        break;
                    case 58:
                        useTimeMult = 0.85f;
                        damageMult = 0.85f;
                        break;
                    case 26:
                        manaMult = 0.85f;
                        damageMult = 1.1f;
                        break;
                    case 27:
                        manaMult = 0.85f;
                        break;
                    case 28:
                        manaMult = 0.85f;
                        damageMult = 1.15f;
                        knockbackMult = 1.05f;
                        break;
                    case 83:
                        knockbackMult = 1.15f;
                        damageMult = 1.15f;
                        critBonus = 5;
                        useTimeMult = 0.9f;
                        manaMult = 0.9f;
                        break;
                    case 29:
                        manaMult = 1.1f;
                        break;
                    case 30:
                        manaMult = 1.2f;
                        damageMult = 0.9f;
                        break;
                    case 31:
                        knockbackMult = 0.9f;
                        damageMult = 0.9f;
                        break;
                    case 32:
                        manaMult = 1.15f;
                        damageMult = 1.1f;
                        break;
                    case 33:
                        manaMult = 1.1f;
                        knockbackMult = 1.1f;
                        useTimeMult = 0.9f;
                        break;
                    case 34:
                        manaMult = 0.9f;
                        knockbackMult = 1.1f;
                        useTimeMult = 1.1f;
                        damageMult = 1.1f;
                        break;
                    case 35:
                        manaMult = 1.2f;
                        damageMult = 1.15f;
                        knockbackMult = 1.15f;
                        break;
                    case 52:
                        manaMult = 0.9f;
                        damageMult = 0.9f;
                        useTimeMult = 0.9f;
                        break;
                    case 84:
                        knockbackMult = 1.17f;
                        damageMult = 1.17f;
                        critBonus = 8;
                        break;
                    case 36:
                        critBonus = 3;
                        break;
                    case 37:
                        damageMult = 1.1f;
                        critBonus = 3;
                        knockbackMult = 1.1f;
                        break;
                    case 38:
                        knockbackMult = 1.15f;
                        break;
                    case 53:
                        damageMult = 1.1f;
                        break;
                    case 54:
                        knockbackMult = 1.15f;
                        break;
                    case 55:
                        knockbackMult = 1.15f;
                        damageMult = 1.05f;
                        break;
                    case 59:
                        knockbackMult = 1.15f;
                        damageMult = 1.15f;
                        critBonus = 5;
                        break;
                    case 60:
                        damageMult = 1.15f;
                        critBonus = 5;
                        break;
                    case 61:
                        critBonus = 5;
                        break;
                    case 39:
                        damageMult = 0.7f;
                        knockbackMult = 0.8f;
                        break;
                    case 40:
                        damageMult = 0.85f;
                        break;
                    case 56:
                        knockbackMult = 0.8f;
                        break;
                    case 41:
                        knockbackMult = 0.85f;
                        damageMult = 0.9f;
                        break;
                    case 57:
                        knockbackMult = 0.9f;
                        damageMult = 1.18f;
                        break;
                    case 42:
                        useTimeMult = 0.9f;
                        break;
                    case 43:
                        damageMult = 1.1f;
                        useTimeMult = 0.9f;
                        break;
                    case 44:
                        useTimeMult = 0.9f;
                        critBonus = 3;
                        break;
                    case 45:
                        useTimeMult = 0.95f;
                        break;
                    case 46:
                        critBonus = 3;
                        useTimeMult = 0.94f;
                        damageMult = 1.07f;
                        break;
                    case 47:
                        useTimeMult = 1.15f;
                        break;
                    case 48:
                        useTimeMult = 1.2f;
                        break;
                    case 49:
                        useTimeMult = 1.08f;
                        break;
                    case 50:
                        damageMult = 0.8f;
                        useTimeMult = 1.15f;
                        break;
                    case 51:
                        knockbackMult = 0.9f;
                        useTimeMult = 0.9f;
                        damageMult = 1.05f;
                        critBonus = 2;
                        break;
                }
            }

            strength = 1f * damageMult * (2f - useTimeMult) * (2f - manaMult) * scaleMult
                * knockbackMult * shootSpeedMult * (1f + critBonus * 0.02f);
            if (prefixID == 62 || prefixID == 69 || prefixID == 73 || prefixID == 77)
                strength *= 1.05f;

            if (prefixID == 63 || prefixID == 70 || prefixID == 74 || prefixID == 78 || prefixID == 67)
                strength *= 1.1f;

            if (prefixID == 64 || prefixID == 71 || prefixID == 75 || prefixID == 79 || prefixID == 66)
                strength *= 1.15f;

            if (prefixID == 65 || prefixID == 72 || prefixID == 76 || prefixID == 80 || prefixID == 68)
                strength *= 1.2f;

            additionStruct.prefixID = prefixID;
            additionStruct.damageMult = damageMult;
            additionStruct.knockbackMult = knockbackMult;
            additionStruct.useTimeMult = useTimeMult;
            additionStruct.scaleMult = scaleMult;
            additionStruct.shootSpeedMult = shootSpeedMult;
            additionStruct.manaMult = manaMult;
            additionStruct.critBonus = critBonus;
            additionStruct.strength = strength;

            return additionStruct;
        }

        /// <summary>
        /// 获取玩家当前射击状态，包括武器的伤害、击退、弹药类型等信息
        /// 该方法根据玩家当前装备的武器以及所用的弹药类型来计算并返回完整的射击状态
        /// </summary>
        /// <param name="player">玩家实例，代表调用该方法的玩家对象</param>
        /// <param name="shootKey">一个可选的标识符，用于区分不同的射击事件，默认为 "Null" 表示不使用特定键值</param>
        /// <returns>返回一个包含射击相关信息的 <see cref="ShootState"/> 结构体</returns>
        public static ShootState GetShootState(this Player player, string shootKey = "Null") {
            ShootState shootState = new();
            Item item = player.GetItem();
            if (item.useAmmo == AmmoID.None) {
                shootState.WeaponDamage = player.GetWeaponDamage(item);
                shootState.WeaponKnockback = item.knockBack;
                shootState.AmmoTypes = item.shoot;
                shootState.ShootSpeed = item.shootSpeed;
                shootState.UseAmmoItemType = ItemID.None;
                shootState.HasAmmo = false;
                if (shootState.AmmoTypes == 0 || shootState.AmmoTypes == 10) {
                    shootState.AmmoTypes = ProjectileID.Bullet;
                }
                return shootState;
            }
            shootState.HasAmmo = player.PickAmmo(item, out shootState.AmmoTypes, out shootState.ShootSpeed
                , out shootState.WeaponDamage, out shootState.WeaponKnockback, out shootState.UseAmmoItemType, true);
            if (shootKey == "Null") {
                shootKey = null;
            }
            shootState.Source = new EntitySource_ItemUse_WithAmmo(player, item, shootState.UseAmmoItemType, shootKey);
            return shootState;
        }

        /// <summary>
        /// 获取玩家当前的弹药状态，包括所有有效的弹药类型、对应的物品、以及物品的数量等信息
        /// </summary>
        /// <param name="player">玩家实例，代表调用该方法的玩家对象</param>
        /// <param name="assignAmmoType">指定的弹药类型，默认为 0，表示不限制弹药类型。如果指定某个类型，则只返回该类型的弹药</param>
        /// <param name="numSort">是否对弹药物品进行排序，默认为 false。如果为 true，则按照物品堆叠数量降序排序</param>
        /// <returns>返回一个包含当前弹药状态信息的 <see cref="AmmoState"/> 结构体</returns>
        public static AmmoState GetAmmoState(this Player player, int assignAmmoType = 0, bool numSort = false) {
            AmmoState ammoState = new();
            int num = 0;  // 当前弹药总数量
            List<Item> itemInds = new();  // 存储有效的弹药物品
            List<int> itemTypes = new();  // 存储弹药物品的类型
            List<int> itemShootTypes = new();  // 存储弹药发射类型（即射击类型）

            // 遍历玩家背包中的所有物品
            foreach (Item item in player.inventory) {
                // 跳过没有弹药的物品
                if (item.ammo == AmmoID.None) {
                    continue;
                }

                // 如果指定了弹药类型并且物品的弹药类型不匹配，跳过该物品
                if (assignAmmoType != 0 && item.ammo != assignAmmoType) {
                    continue;
                }

                // 添加物品类型、射击类型及堆叠数量
                itemTypes.Add(item.type);
                itemShootTypes.Add(item.shoot);
                num += item.stack;  // 累加物品的堆叠数量
            }

            // 遍历玩家快捷栏中的物品（位置从54到57）
            for (int i = 54; i < 58; i++) {
                Item item = player.inventory[i];
                // 如果指定了弹药类型并且物品的弹药类型不匹配，或者物品没有弹药，跳过
                if ((assignAmmoType != 0 && item.ammo != assignAmmoType) || item.ammo == AmmoID.None) {
                    continue;
                }
                itemInds.Add(player.inventory[i]);
            }

            // 遍历玩家背包的前54个物品
            for (int i = 0; i < 54; i++) {
                Item item = player.inventory[i];
                // 如果指定了弹药类型并且物品的弹药类型不匹配，或者物品没有弹药，跳过
                if ((assignAmmoType != 0 && item.ammo != assignAmmoType) || item.ammo == AmmoID.None) {
                    continue;
                }
                itemInds.Add(player.inventory[i]);
            }

            // 如果需要排序，则按堆叠数量降序排序
            if (numSort) {
                itemInds = itemInds.OrderByDescending(item => item.stack).ToList();
            }

            // 设置返回的弹药状态信息
            ammoState.ValidProjectileIDs = itemShootTypes.ToArray();  // 有效的弹药发射类型
            ammoState.CurrentItems = itemInds.ToArray();  // 当前有效的弹药物品
            ammoState.ValidItemIDs = itemTypes.ToArray();  // 有效的弹药物品类型
            ammoState.CurrentAmount = num;  // 当前弹药总数量

            // 设置最大和最小数量的物品
            if (itemInds.Count > 0) {
                ammoState.MaxAmountItem = itemInds[0];  // 最大数量的物品
                ammoState.MinAmountItem = itemInds[itemInds.Count - 1];  // 最小数量的物品
            }
            else {
                ammoState.MaxAmountItem = new Item();  // 若无有效物品，返回空物品
                ammoState.MinAmountItem = new Item();  // 若无有效物品，返回空物品
            }

            return ammoState;
        }

        /// <summary>
        /// 获取玩家选中的物品实例
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public static Item GetItem(this Player player) => Main.mouseItem.IsAir ? player.inventory[player.selectedItem] : Main.mouseItem;

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

        /// <summary>
        /// 生成Boss级实体，考虑网络状态
        /// </summary>
        /// <param name="player">触发生成的玩家实例</param>
        /// <param name="bossType">要生成的 Boss 的类型</param>
        /// <param name="obeyLocalPlayerCheck">是否要遵循本地玩家检查</param>
        public static void SpawnBossNetcoded(Player player, int bossType, bool obeyLocalPlayerCheck = true) {
            if (player.whoAmI == Main.myPlayer || !obeyLocalPlayerCheck) {
                // 如果使用物品的玩家是客户端
                // （在此明确排除了服务器端）

                _ = SoundEngine.PlaySound(SoundID.Roar, player.position);

                if (Main.netMode != NetmodeID.MultiplayerClient) {
                    // 如果玩家不在多人游戏中，直接生成 Boss
                    NPC.SpawnOnPlayer(player.whoAmI, bossType);
                }
                else {
                    // 如果玩家在多人游戏中，请求生成
                    // 仅当 NPCID.Sets.MPAllowedEnemies[type] 为真时才有效，需要在 NPC 代码中设置

                    NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: bossType);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="modPacket"></param>
        /// <param name="point16"></param>
        public static void WritePoint16(this ModPacket modPacket, Point16 point16) {
            modPacket.Write(point16.X);
            modPacket.Write(point16.Y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="point16"></param>
        public static void WritePoint16(this BinaryWriter writer, Point16 point16) {
            writer.Write(point16.X);
            writer.Write(point16.Y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static Point16 ReadPoint16(this BinaryReader reader) => new Point16(reader.ReadInt16(), reader.ReadInt16());
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
        /// 判断给定坐标是否为多结构物块的左上角位置，并输出左上角的坐标
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <param name="point">输出的左上角坐标，如果不是左上角则为(0,0)</param>
        /// <returns>如果是左上角，返回true，否则返回false </returns>
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
        /// 安全的获取多结构物块左上角的位置，给定一个物块坐标，自动寻找到该坐标对应的左上原点位置输出
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="point"></param>
        /// <returns>如果没能找到，则输出(0,0)，并返回<see langword="false"/></returns>
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

        #region Draw
        /// <summary>
        /// 绘制具有旋转边框效果的纹理
        /// 该方法通过两层旋转光圈模拟出一种动态发光的边缘效果
        /// </summary>
        /// <param name="spriteBatch">用于绘制的 SpriteBatch 对象</param>
        /// <param name="texture">要绘制的纹理</param>
        /// <param name="drawTimer">用于控制旋转动画的计时器（通常递增）</param>
        /// <param name="position">纹理绘制的位置</param>
        /// <param name="sourceRectangle">纹理裁剪区域（可选）</param>
        /// <param name="color">绘制颜色</param>
        /// <param name="rotation">纹理的旋转角度（弧度制）</param>
        /// <param name="origin">纹理的原点</param>
        /// <param name="scale">纹理的缩放比例</param>
        /// <param name="effects">纹理的 SpriteEffects <see cref="SpriteEffects.None"/></param>
        public static void DrawRotatingMarginEffect(SpriteBatch spriteBatch, Texture2D texture, int drawTimer, Vector2 position,
            Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects = SpriteEffects.None) {
            // 计算全局时间因子（用于同步动画效果）
            float globalTime = Main.GlobalTimeWrappedHourly;
            // 计算旋转计时器，用于控制旋转速度
            float timer = drawTimer / 240f + globalTime * 0.04f;

            // 控制时间的周期性变化，使光圈明暗有呼吸感
            float timeFactor = globalTime % 4f;
            timeFactor /= 2f;
            if (timeFactor >= 1f)
                timeFactor = 2f - timeFactor;
            timeFactor = timeFactor * 0.5f + 0.5f;

            // 外层旋转光圈效果（间隔0.25，较大的偏移）
            for (float offset = 0f; offset < 1f; offset += 0.25f) {
                float radians = (offset + timer) * MathHelper.TwoPi;
                Vector2 offsetPosition = position + new Vector2(0f, 8f).RotatedBy(radians) * timeFactor;
                Color transparentColor = new Color(color.R, color.G, color.B, 50); // 较透明的颜色
                spriteBatch.Draw(texture, offsetPosition, sourceRectangle, transparentColor, rotation, origin, scale, effects, 0f);
            }

            // 内层旋转光圈效果（间隔0.34，较小的偏移）
            for (float offset = 0f; offset < 1f; offset += 0.34f) {
                float radians = (offset + timer) * MathHelper.TwoPi;
                Vector2 offsetPosition = position + new Vector2(0f, 4f).RotatedBy(radians) * timeFactor;
                Color semiTransparentColor = new Color(color.R, color.G, color.B, 77); // 半透明的颜色
                spriteBatch.Draw(texture, offsetPosition, sourceRectangle, semiTransparentColor, rotation, origin, scale, effects, 0f);
            }
        }

        #endregion

        #region UI
        /// <summary>
        /// 绘制一个带有边框的矩形，支持缩放及缩放中心自定义
        /// </summary>
        /// <param name="spriteBatch">用于绘制的SpriteBatch对象</param>
        /// <param name="borderTexture">边框纹理，用于绘制矩形边框</param>
        /// <param name="borderWidth">边框的宽度（像素）</param>
        /// <param name="rectangle">描述这个方框的矩形构造体</param>
        /// <param name="borderColor">边框的颜色</param>
        /// <param name="borderCenterColor">矩形内部区域的颜色</param>
        /// <param name="scale">整体缩放比例（默认为1，即不缩放）</param>
        /// <param name="scaleCenter">缩放中心，决定缩放时的参照点（默认为矩形中心）</param>
        public static void DrawBorderedRectangle(SpriteBatch spriteBatch, Texture2D borderTexture, int borderWidth
            , Rectangle rectangle, Color borderColor, Color borderCenterColor, float scale = 1, Vector2 scaleCenter = default)
            => DrawBorderedRectangle(spriteBatch, borderTexture, borderWidth, rectangle.TopLeft(), rectangle.Width, rectangle.Height, borderColor, borderCenterColor, scale, scaleCenter);

        /// <summary>
        /// 绘制一个带有边框的矩形，支持缩放及缩放中心自定义
        /// </summary>
        /// <param name="spriteBatch">用于绘制的SpriteBatch对象</param>
        /// <param name="borderTexture">边框纹理，用于绘制矩形边框</param>
        /// <param name="borderWidth">边框的宽度（像素）</param>
        /// <param name="drawPosition">绘制矩形的起始位置（左上角坐标）</param>
        /// <param name="drawWidth">矩形的宽度（未缩放）</param>
        /// <param name="drawHeight">矩形的高度（未缩放）</param>
        /// <param name="borderColor">边框的颜色</param>
        /// <param name="borderCenterColor">矩形内部区域的颜色</param>
        /// <param name="scale">整体缩放比例（默认为1，即不缩放）</param>
        /// <param name="scaleCenter">缩放中心，决定缩放时的参照点（默认为矩形中心）</param>
        public static void DrawBorderedRectangle(SpriteBatch spriteBatch, Texture2D borderTexture, int borderWidth
            , Vector2 drawPosition, int drawWidth, int drawHeight, Color borderColor, Color borderCenterColor, float scale = 1, Vector2 scaleCenter = default) {
            if (scaleCenter == default) {
                scaleCenter = new Vector2(0.5f, 0.5f);
            }
            // 计算缩放后的整体尺寸
            int scaledWidth = (int)(drawWidth * scale);
            int scaledHeight = (int)(drawHeight * scale);
            // 计算缩放偏移量，以缩放中心为基准
            float offsetX = (scaledWidth - drawWidth) * scaleCenter.X;
            float offsetY = (scaledHeight - drawHeight) * scaleCenter.Y;
            // 调整后的绘制起始位置
            Vector2 adjustedPosition = new Vector2(drawPosition.X - offsetX, drawPosition.Y - offsetY);
            // 重新定义外部和内部的矩形区域
            Rectangle outerRect = new Rectangle((int)adjustedPosition.X, (int)adjustedPosition.Y, scaledWidth, scaledHeight);
            Rectangle innerRect = new Rectangle(
                outerRect.X + borderWidth,
                outerRect.Y + borderWidth,
                scaledWidth - 2 * borderWidth,
                scaledHeight - 2 * borderWidth
            );
            spriteBatch.Draw(borderTexture, new Rectangle(outerRect.X, outerRect.Y, scaledWidth, borderWidth), borderColor);
            spriteBatch.Draw(borderTexture, new Rectangle(outerRect.X, outerRect.Bottom - borderWidth, scaledWidth, borderWidth), borderColor);
            spriteBatch.Draw(borderTexture, new Rectangle(outerRect.X, outerRect.Y + borderWidth, borderWidth, scaledHeight - 2 * borderWidth), borderColor);
            spriteBatch.Draw(borderTexture, new Rectangle(outerRect.Right - borderWidth, outerRect.Y + borderWidth, borderWidth, scaledHeight - 2 * borderWidth), borderColor);
            spriteBatch.Draw(borderTexture, innerRect, borderCenterColor);
        }

        /// <summary>
        /// 判断给定的二维点是否在屏幕内（考虑一个小范围的边界扩展）
        /// </summary>
        /// <param name="pos">要判断的点的坐标</param>
        /// <returns>如果点在屏幕范围内（包括扩展边界），返回 true；否则返回 false</returns>
        public static bool IsPointOnScreen(Vector2 pos)
            => pos.X > -16 && pos.X < Main.screenWidth + 16 && pos.Y > -16 && pos.Y < Main.screenHeight + 16;

        /// <summary>
        /// 判断给定的矩形是否与屏幕范围有交集
        /// </summary>
        /// <param name="rect">要判断的矩形</param>
        /// <returns>如果矩形与屏幕范围有交集，返回 true；否则返回 false</returns>
        public static bool IsRectangleOnScreen(Rectangle rect)
            => rect.Intersects(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight));

        /// <summary>
        /// 判断以指定位置和尺寸定义的矩形是否在屏幕范围内（使用辅助方法实现矩形判断）
        /// </summary>
        /// <param name="pos">矩形的左上角位置</param>
        /// <param name="size">矩形的尺寸</param>
        /// <returns>如果矩形在屏幕范围内，返回 true；否则返回 false</returns>
        public static bool IsAreaOnScreen(Vector2 pos, Vector2 size)
            => IsRectangleOnScreen(new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y));

        /// <summary>
        /// 生成一个二维网格的坐标数组 用于按行列排列元素
        /// </summary>
        /// <param name="elementCount">元素总数 必须是正整数</param>
        /// <param name="withNum">每行元素数量 必须是正整数</param>
        /// <returns>
        /// 返回一个 <see cref="Point"/> 数组 每个点表示元素在网格中的坐标 (x, y)
        /// x 表示列索引 y 表示行索引
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
        public static Point[] GenerateGridCoordinates(int elementCount, int withNum) {
            if (elementCount <= 0 || withNum <= 0) {
                throw new ArgumentException("elementCount and withNum must be positive integers.");
            }
            Point[] result = new Point[elementCount];
            for (int i = 0; i < elementCount; i++) {
                result[i] = new Point(i % withNum, i / withNum);
            }

            return result;
        }

        /// <summary>
        /// 计算一个基于输入矩形的剪裁区域 用于确保绘制区域在当前屏幕视口内
        /// </summary>
        /// <param name="spriteBatch">当前的 <see cref="SpriteBatch"/> 实例 用于获取视口信息</param>
        /// <param name="r">输入的原始矩形区域</param>
        /// <returns>一个被剪裁后的矩形区域 保证其在视口内</returns>
        /// <remarks>
        /// 输入矩形首先通过 UIScaleMatrix 转换为屏幕坐标
        /// 然后对矩形区域的坐标及大小进行裁剪 使其不会超出屏幕的有效范围
        /// 适用于 UI 绘制时需要限制在屏幕显示区域内的场景
        /// </remarks>
        public static Rectangle GetClippingRectangle(SpriteBatch spriteBatch, Rectangle r) {
            // 转换矩形的左上角和右下角到屏幕坐标系
            Vector2 topLeft = Vector2.Transform(new Vector2(r.X, r.Y), Main.UIScaleMatrix);
            Vector2 bottomRight = Vector2.Transform(new Vector2(r.Right, r.Bottom), Main.UIScaleMatrix);

            // 计算转换后的矩形
            Rectangle result = new(
                x: (int)topLeft.X,
                y: (int)topLeft.Y,
                width: (int)(bottomRight.X - topLeft.X),
                height: (int)(bottomRight.Y - topLeft.Y)
            );

            // 获取当前屏幕视口尺寸
            int viewportWidth = spriteBatch.GraphicsDevice.Viewport.Width;
            int viewportHeight = spriteBatch.GraphicsDevice.Viewport.Height;

            // 裁剪矩形到屏幕范围内
            result.X = Utils.Clamp(result.X, 0, viewportWidth);
            result.Y = Utils.Clamp(result.Y, 0, viewportHeight);
            result.Width = Utils.Clamp(result.Width, 0, viewportWidth - result.X);
            result.Height = Utils.Clamp(result.Height, 0, viewportHeight - result.Y);

            return result;
        }

        /// <summary>
        /// 获取物品绘制的大小，确保根据纹理尺寸和给定宽度比例调整物品的大小
        /// </summary>
        /// <param name="item">物品实例</param>
        /// <param name="sizeW">宽度用于计算缩放比例，默认值为 32</param>
        /// <returns>返回缩放比例</returns>
        public static float GetDrawItemSize(this Item item, int sizeW = 32) {
            Rectangle rectangle = Main.itemAnimations[item.type] != null ?
                        Main.itemAnimations[item.type].GetFrame(TextureAssets.Item[item.type].Value)
                        : TextureAssets.Item[item.type].Value.Frame(1, 1, 0, 0);

            float size = 1f;
            if (rectangle.Width > sizeW) {
                size = sizeW / (float)rectangle.Width;
            }
            if (size * rectangle.Height > sizeW) {
                size = sizeW / (float)rectangle.Height;
            }
            return size;
        }

        /// <summary>
        /// 获取物品绘制的大小，确保根据纹理尺寸和给定宽度比例调整物品的大小
        /// </summary>
        /// <param name="type">物品类型 ID</param>
        /// <param name="sizeW">宽度用于计算缩放比例，默认值为 32</param>
        /// <returns>返回缩放比例</returns>
        public static float GetDrawItemSize(int type, int sizeW = 32) {
            Rectangle rectangle = Main.itemAnimations[type] != null ?
                        Main.itemAnimations[type].GetFrame(TextureAssets.Item[type].Value)
                        : TextureAssets.Item[type].Value.Frame(1, 1, 0, 0);

            float size = 1f;
            if (rectangle.Width > sizeW) {
                size = sizeW / (float)rectangle.Width;
            }
            if (size * rectangle.Height > sizeW) {
                size = sizeW / (float)rectangle.Height;
            }
            return size;
        }

        /// <summary>
        /// 绘制一个简单的物品图像
        /// </summary>
        /// <param name="spriteBatch">用于绘制的 <see cref="SpriteBatch"/> 实例</param>
        /// <param name="itemType">物品类型 ID</param>
        /// <param name="position">物品绘制的屏幕坐标</param>
        /// <param name="size">绘制缩放比例</param>
        /// <param name="rotation">物品旋转角度（弧度）</param>
        /// <param name="color">绘制颜色</param>
        /// <param name="orig">纹理原点（默认为纹理的中心点）</param>
        public static void SimpleDrawItem(SpriteBatch spriteBatch, int itemType, Vector2 position, float size, float rotation, Color color, Vector2 orig = default(Vector2)) {
            // 获取物品的纹理资源
            Texture2D texture = TextureAssets.Item[itemType].Value;

            // 获取物品的动画帧区域（如无动画则使用完整纹理）
            Rectangle? frame = Main.itemAnimations[itemType]?.GetFrame(texture) ?? texture.Frame(1, 1, 0, 0);

            // 如果未指定原点，则使用纹理帧的中心点作为默认原点
            if (orig == Vector2.Zero) orig = frame.HasValue ? frame.Value.Size() / 2 : texture.Size() / 2;

            // 绘制物品
            spriteBatch.Draw(texture, position, frame, color, rotation, orig, size, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 绘制一个简单的物品图像
        /// </summary>
        /// <param name="spriteBatch">用于绘制的 <see cref="SpriteBatch"/> 实例</param>
        /// <param name="itemType">物品类型 ID</param>
        /// <param name="position">物品绘制的屏幕坐标</param>
        /// <param name="size">绘制缩放比例，若为 0 则自动根据物品尺寸计算</param>
        /// <param name="rotation">物品旋转角度（弧度）</param>
        /// <param name="color">绘制颜色</param>
        /// <param name="orig">纹理原点（默认为纹理的中心点）</param>
        /// <param name="itemWidth">物品绘制框宽度，默认值为 32</param>
        public static void SimpleDrawItem(SpriteBatch spriteBatch, int itemType, Vector2 position, int itemWidth = 32, float size = 0, float rotation = 0, Color color = default, Vector2 orig = default) {
            Texture2D texture = TextureAssets.Item[itemType].Value;
            Rectangle? frame = Main.itemAnimations[itemType]?.GetFrame(texture) ?? texture.Frame(1, 1, 0, 0);
            if (orig == Vector2.Zero) {
                orig = frame.HasValue ? frame.Value.Size() / 2 : texture.Size() / 2;
            }
            // 如果未指定大小，则根据物品尺寸和提供的宽度计算缩放比例
            if (size <= 0) {
                size = GetDrawItemSize(itemType, itemWidth);
            }
            else {
                size = GetDrawItemSize(itemType, itemWidth) * size;
            }
            // 设置颜色
            if (color == default) {
                color = Color.White;
            }
            // 绘制物品
            spriteBatch.Draw(texture, position, frame, color, rotation, orig, size, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 绘制事件进度条
        /// </summary>
        /// <param name="spriteBatch">绘制使用的 <see cref="SpriteBatch"/> 实例</param>
        /// <param name="pixel">进度条材质</param>
        /// <param name="drawPos">进度条中心位置</param>
        /// <param name="iconAsset">事件图标的纹理资源</param>
        /// <param name="eventKillRatio">事件完成度（0~1）</param>
        /// <param name="size">整体缩放比例</param>
        /// <param name="barWidth">进度条宽度</param>
        /// <param name="barHeight">进度条高度</param>
        /// <param name="eventMainName">事件名称</param>
        /// <param name="eventMainColor">事件背景颜色</param>
        public static void DrawEventProgressBar(SpriteBatch spriteBatch, Texture2D pixel, Vector2 drawPos, Asset<Texture2D> iconAsset
            , float eventKillRatio, float size, int barWidth, int barHeight, string eventMainName, Color eventMainColor) {
            // 参数校验
            if (size < 0.1f || eventKillRatio < 0 || eventKillRatio > 1) {
                return;
            }

            // 事件标题绘制
            Vector2 eventNameSize = FontAssets.MouseText.Value.MeasureString(eventMainName);
            float titleBackgroundWidth = 120f + Math.Max(0, eventNameSize.X - 200f);
            Vector2 titlePosition = new(Main.screenWidth - titleBackgroundWidth, Main.screenHeight - 80);

            Rectangle titleBackgroundRect = Utils.CenteredRectangle(titlePosition, eventNameSize + new Vector2(iconAsset.Value.Width + 12, 6f));
            Utils.DrawInvBG(spriteBatch, titleBackgroundRect, eventMainColor * 0.5f * size);

            // 绘制事件图标
            spriteBatch.Draw(iconAsset.Value, titleBackgroundRect.Left() + Vector2.UnitX * 8f, null, Color.White * size
                , 0f, Vector2.UnitY * iconAsset.Value.Height / 2, 0.8f * size, SpriteEffects.None, 0f);

            // 绘制事件名称
            Utils.DrawBorderString(spriteBatch, eventMainName, titleBackgroundRect.Right() - Vector2.UnitX * 16f, Color.White * size, 0.9f * size, 1f, 0.4f, -1);

            // 绘制进度条背景
            drawPos += new Vector2(-100, 20);
            Rectangle progressBarRect = new((int)drawPos.X - barWidth / 2, (int)drawPos.Y - barHeight / 2, barWidth, barHeight);
            Utils.DrawInvBG(spriteBatch, progressBarRect, new Color(6, 80, 84, 255) * 0.785f * size);

            // 绘制进度条主体
            DrawProgressBar(spriteBatch, pixel, drawPos, eventKillRatio, size, barWidth);

            // 绘制完成百分比文本
            string progressText = Language.GetTextValue("Game.WaveCleared", $"{eventKillRatio * 100:N1}%");
            Vector2 progressTextSize = FontAssets.MouseText.Value.MeasureString(progressText);
            float textScale = progressTextSize.Y > 22f ? 22f / progressTextSize.Y : 1f;
            Utils.DrawBorderString(spriteBatch, progressText, drawPos + Vector2.UnitY * 6f, Color.White * size, textScale, 0.5f, 1f, -1);
        }

        /// <summary>
        /// 绘制进度条主体部分
        /// </summary>
        /// <param name="spriteBatch">绘制使用的 <see cref="SpriteBatch"/> 实例</param>
        /// <param name="pixel">进度条材质</param>
        /// <param name="drawPos">进度条中心位置</param>
        /// <param name="progress">完成度（0~1）</param>
        /// <param name="size">缩放比例</param>
        /// <param name="barWidth">进度条宽度</param>
        private static void DrawProgressBar(SpriteBatch spriteBatch, Texture2D pixel, Vector2 drawPos, float progress, float size, float barWidth) {
            // 已完成部分
            Vector2 completedBarPos = drawPos + Vector2.UnitX * (progress - 0.5f) * barWidth;
            spriteBatch.Draw(pixel, completedBarPos, new Rectangle(0, 0, 1, 1), new Color(255, 241, 51) * size
                , 0f, new Vector2(1f, 0.5f), new Vector2(barWidth * progress, 8) * size, SpriteEffects.None, 0f);
            // 边缘光效
            spriteBatch.Draw(pixel, completedBarPos, new Rectangle(0, 0, 1, 1), new Color(255, 165, 0, 127) * size
                , 0f, new Vector2(1f, 0.5f), new Vector2(2f, 8) * size, SpriteEffects.None, 0f);
            // 未完成部分
            Vector2 remainingBarPos = drawPos + Vector2.UnitX * (progress - 0.5f) * barWidth;
            spriteBatch.Draw(pixel, remainingBarPos, new Rectangle(0, 0, 1, 1), Color.Black * size
                , 0f, Vector2.UnitY * 0.5f, new Vector2(barWidth * (1f - progress), 8) * size, SpriteEffects.None, 0f);
        }

        #endregion
    }
}
