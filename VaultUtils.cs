using InnoVault.GameSystem;
using InnoVault.TileProcessors;
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
        /// 弧度分母
        /// </summary>
        public const float atoR = MathHelper.Pi / 180;

        /// <summary>
        /// 表示一个完整的圆周角度（2π），约为 6.2832 弧度
        /// </summary>
        public const float TwoPi = MathF.PI * 2;

        /// <summary>
        /// 表示两个完整的圆周角度（4π），约为 12.5664 弧度
        /// </summary>
        public const float FourPi = MathF.PI * 4;

        /// <summary>
        /// 表示一个半圆周角度（3π），约为 9.4248 弧度
        /// </summary>
        public const float ThreePi = MathF.PI * 3;

        /// <summary>
        /// 表示三分之一圆周角度（π/3），约为 1.0472 弧度（60度）
        /// </summary>
        public const float PiOver3 = MathF.PI / 3f;

        /// <summary>
        /// 表示五分之一圆周角度（π/5），约为 0.6283 弧度（36度）
        /// </summary>
        public const float PiOver5 = MathF.PI / 5f;

        /// <summary>
        /// 表示六分之一圆周角度（π/6），约为 0.5236 弧度（30度）
        /// </summary>
        public const float PiOver6 = MathF.PI / 6f;

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
        /// 将制定角度转化为指定模长的向量
        /// </summary>
        /// <param name="radius"></param>
        /// <param name="theta"></param>
        /// <returns></returns>
        public static Vector2 RodingToVer(float radius, float theta) => theta.ToRotationVector2() * radius;

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
        /// <summary>
        /// 创建一个矩形
        /// </summary>
        public static Rectangle GetRectangle(this Vector2 topLeft, Point size) => new Rectangle((int)topLeft.X, (int)topLeft.Y, size.X, size.Y);

        /// <summary>
        /// 计算一个渐进速度值
        /// </summary>
        /// <param name="thisCenter">本体位置</param>
        /// <param name="targetCenter">目标位置</param>
        /// <param name="speed">速度</param>
        /// <param name="shutdownDistance">停摆范围</param>
        /// <returns></returns>
        public static float AsymptoticVelocity(this Vector2 thisCenter, Vector2 targetCenter, float speed, float shutdownDistance) {
            Vector2 toMou = targetCenter - thisCenter;
            float thisSpeed = toMou.LengthSquared() > shutdownDistance * shutdownDistance ? speed : MathHelper.Min(speed, toMou.Length());
            return thisSpeed;
        }

        /// <summary>
        /// 平滑地将当前角度旋转到目标角度，限制每次更新的最大变化量
        /// 该方法确保采用最短旋转路径，并正确处理角度环绕
        /// </summary>
        /// <param name="currentAngle">当前角度（弧度）</param>
        /// <param name="targetAngle">目标角度（弧度）</param>
        /// <param name="maxChange">每次更新允许的最大角度变化量（弧度），必须为非负数</param>
        /// <returns>新的角度（弧度），向目标角度靠近但不超过最大变化量</returns>
        /// <remarks>
        /// 此方法适用于游戏或动画中的平滑旋转，例如角色或摄像机的转向
        /// 角度会被归一化到 [-π, π] 范围，以避免大角度值引发的问题
        /// 方法会自动选择最短旋转路径，即使跨越 -π/π 边界
        /// </remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">如果 maxChange 小于 0，则抛出异常</exception>
        public static float RotTowards(this float currentAngle, float targetAngle, float maxChange) {
            if (maxChange < 0f) {
                maxChange *= -1;
            }

            // 归一化角度到 [-π, π]
            currentAngle = MathHelper.WrapAngle(currentAngle);
            targetAngle = MathHelper.WrapAngle(targetAngle);

            // 计算最短路径的角度差
            float delta = MathHelper.WrapAngle(targetAngle - currentAngle);

            // 限制变化量
            delta = MathHelper.Clamp(delta, -maxChange, maxChange);

            // 返回新的角度并再次归一化
            return MathHelper.WrapAngle(currentAngle + delta);
        }

        #endregion

        #region System
        /// <summary>
        /// 发送游戏文本并记录日志内容
        /// </summary>
        /// <param name="obj">需要输入的内容</param>
        /// <param name="mod">打印的模组对象，如果为默认值<see langword="null"/>，则不会在日志中输出记录</param>
        public static void LoggerDomp(this object obj, Mod mod = null) {
            string text;
            if (obj == null) {
                text = "ERROR is Null";
            }
            else {
                text = obj.ToString();
            }

            Text(text, Color.Red);

            mod?.Logger.Info(text);
        }

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
                if (!VaultMod.ModTypeSetCache.TryGetValue(mod.Code, out var typeSet)) {
                    typeSet = [.. AssemblyManager.GetLoadableTypes(mod.Code)];
                    VaultMod.ModTypeSetCache[mod.Code] = typeSet;
                }

                if (typeSet.Contains(type)) {
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
            if (VaultMod.AnyModCodeType == null) {
                // 创建一个存储所有类型的列表
                List<Type> types = [];
                Mod[] mods = ModLoader.Mods;
                // 使用 LINQ 将每个Mod的代码程序集中的所有可加载类型平铺到一个集合中
                types.AddRange(mods.SelectMany(mod => AssemblyManager.GetLoadableTypes(mod.Code)));
                VaultMod.AnyModCodeType = [.. types];
            }

            return VaultMod.AnyModCodeType;
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

        #region AI

        /// <summary>
        /// 让弹幕进行爆炸效果的操作
        /// </summary>
        /// <param name="projectile">要爆炸的投射物</param>
        /// <param name="blastRadius">爆炸效果的半径（默认为 120 单位）</param>
        /// <param name="explosionSound">爆炸声音的样式（默认为默认的爆炸声音）</param>
        /// <param name="spanSound">是否自行播放声音，默认为<see langword="true"/></param>
        public static void Explode(this Projectile projectile, int blastRadius = 120, SoundStyle explosionSound = default, bool spanSound = true) {
            Vector2 originalPosition = projectile.position;
            int originalWidth = projectile.width;
            int originalHeight = projectile.height;

            if (spanSound) {
                _ = SoundEngine.PlaySound(explosionSound == default ? SoundID.Item14 : explosionSound, projectile.Center);
            }

            projectile.position = projectile.Center;
            projectile.width = projectile.height = blastRadius * 2;
            projectile.position.X -= projectile.width / 2;
            projectile.position.Y -= projectile.height / 2;

            projectile.maxPenetrate = -1;
            projectile.penetrate = -1;
            projectile.usesLocalNPCImmunity = true;
            projectile.localNPCHitCooldown = -1;

            projectile.Damage();

            projectile.position = originalPosition;
            projectile.width = originalWidth;
            projectile.height = originalHeight;
        }

        /// <summary>
        /// 普通的追逐行为
        /// </summary>
        /// <param name="entity">需要操纵的实体</param>
        /// <param name="TargetCenter">目标地点</param>
        /// <param name="Speed">速度</param>
        /// <param name="ShutdownDistance">停摆距离</param>
        /// <returns></returns>
        public static Vector2 ChasingBehavior(this Entity entity, Vector2 TargetCenter, float Speed, float ShutdownDistance = 16) {
            if (entity == null) {
                return Vector2.Zero;
            }

            Vector2 ToTarget = TargetCenter - entity.Center;
            Vector2 ToTargetNormalize = ToTarget.SafeNormalize(Vector2.Zero);
            Vector2 speed = ToTargetNormalize * entity.Center.AsymptoticVelocity(TargetCenter, Speed, ShutdownDistance);
            entity.velocity = speed;
            return speed;
        }

        /// <summary>
        /// 更加缓和的追逐行为
        /// </summary>
        /// <param name="entity">需要操纵的实体</param>
        /// <param name="TargetCenter">目标地点</param>
        /// <param name="SpeedUpdates">速度的更新系数</param>
        /// <param name="HomingStrenght">追击力度</param>
        /// <returns></returns>
        public static Vector2 SmoothHomingBehavior(this Entity entity, Vector2 TargetCenter, float SpeedUpdates = 1, float HomingStrenght = 0.1f) {
            float targetAngle = entity.AngleTo(TargetCenter);
            float f = entity.velocity.ToRotation().RotTowards(targetAngle, HomingStrenght);
            Vector2 speed = f.ToRotationVector2() * entity.velocity.Length() * SpeedUpdates;
            entity.velocity = speed;
            return speed;
        }

        /// <summary>
        /// 寻找必要的玩家目标
        /// </summary>
        /// <param name="npc"></param>
        /// <returns></returns>
        public static Player FindPlayer(this NPC npc) {
            if (npc.target < 0 || npc.target >= 255 || !Main.player[npc.target].Alives()) {
                npc.FindClosestPlayer();
            }
            if (npc.target < 0 || npc.target >= 255 || !Main.player[npc.target].Alives()) {
                npc.target = 0;
            }
            return Main.player[npc.target];
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
        /// 尝试寻找距离指定位置最近的NPC
        /// </summary>
        /// <param name="origin">开始搜索的位置</param>
        /// <param name="npc">返回找到的NPC</param>
        /// <param name="maxDistanceToCheck">搜索NPC的最大距离</param>
        /// <param name="ignoreTiles">在检查障碍物时是否忽略瓦片</param>
        /// <param name="bossPriority">是否优先选择Boss</param>
        /// <param name="onHitNPCs">排除的NPC列表</param>
        /// <param name="chasedByNPC">额外的条件过滤，用于判断NPC是否应被考虑，如果该委托不为<see langword="null"/> 那么它的返回值将覆盖其他的筛选结果</param>
        /// <returns>是否成功找到NPC</returns>
        public static bool TryFindClosestNPC(this Vector2 origin, out NPC npc, float maxDistanceToCheck, bool ignoreTiles = true,
            bool bossPriority = false, IEnumerable<NPC> onHitNPCs = null, Func<NPC, bool> chasedByNPC = null) {
            npc = origin.FindClosestNPC(maxDistanceToCheck, ignoreTiles, bossPriority, onHitNPCs, chasedByNPC);
            return npc != null;
        }

        /// <summary>
        /// 寻找距离指定位置最近的玩家
        /// </summary>
        /// <param name="position">搜索起点</param>
        /// <param name="maxRange">最大搜索距离；如果为-1，则忽略范围限制</param>
        /// <param name="ignoreTiles">是否忽略瓦片遮挡</param>
        /// <param name="playerFilter">额外的玩家过滤器；若不为 <see langword="null"/>，将根据此委托结果决定玩家是否被考虑</param>
        /// <returns>距离最近的玩家；若无匹配玩家，返回 <see langword="null"/></returns>
        public static Player FindClosestPlayer(this Vector2 position, float maxRange = 3000f, bool ignoreTiles = true, Func<Player, bool> playerFilter = null) {
            Player closestPlayer = null;
            float minDistance = maxRange == -1f ? float.MaxValue : maxRange;

            foreach (Player player in Main.player) {
                if (!player.Alives()) {
                    continue;
                }

                if (playerFilter != null && !playerFilter(player)) {
                    continue;
                }

                float distance = Vector2.Distance(position, player.Center);

                if (distance <= minDistance) {
                    bool canHit = ignoreTiles || Collision.CanHit(position, 1, 1, player.Center, 1, 1);

                    if (canHit) {
                        minDistance = distance;
                        closestPlayer = player;
                    }
                }
            }

            return closestPlayer;
        }

        /// <summary>
        /// 尝试寻找距离指定位置最近的玩家
        /// </summary>
        /// <param name="position">搜索起点</param>
        /// <param name="player">返回找到的玩家</param>
        /// <param name="maxRange">最大搜索距离；如果为-1，则忽略范围限制</param>
        /// <param name="ignoreTiles">是否忽略瓦片遮挡</param>
        /// <param name="playerFilter">玩家过滤条件</param>
        /// <returns>是否成功找到玩家</returns>
        public static bool TryFindClosestPlayer(this Vector2 position, out Player player, float maxRange = 3000f, bool ignoreTiles = true, Func<Player, bool> playerFilter = null) {
            player = position.FindClosestPlayer(maxRange, ignoreTiles, playerFilter);
            return player != null;
        }

        /// <summary>
        /// 寻找距离指定位置最近的箱子
        /// </summary>
        /// <param name="point">搜索起点，单位为物块</param>
        /// <param name="maxRange">最大搜索距离；如果为-1，则忽略范围限制，单位为像素</param>
        /// <param name="ignoreTiles">是否忽略瓦片遮挡</param>
        /// <param name="chestFilter">可选的箱子过滤器；若为 <see langword="null"/> 则不过滤</param>
        /// <returns>距离最近且符合条件的 <see cref="Chest"/>，若无匹配项则为  <see langword="null"/></returns>
        public static Chest FindClosestChest(this Point16 point, float maxRange = 3000f, bool ignoreTiles = true, Func<Chest, bool> chestFilter = null)
            => FindClosestChest(point.ToWorldCoordinates(), maxRange, ignoreTiles, chestFilter);

        /// <summary>
        /// 寻找距离指定位置最近的箱子
        /// </summary>
        /// <param name="point">搜索起点，单位为物块</param>
        /// <param name="chest">返回的箱子实例</param>
        /// <param name="maxRange">最大搜索距离；如果为-1，则忽略范围限制，单位为像素</param>
        /// <param name="ignoreTiles">是否忽略瓦片遮挡</param>
        /// <param name="chestFilter">可选的箱子过滤器；若为 <see langword="null"/> 则不过滤</param>
        /// <returns>距离最近且符合条件的 <see cref="Chest"/>，若无匹配项则为  <see langword="null"/></returns>
        public static bool TryFindClosestChest(this Point16 point, out Chest chest, float maxRange = 3000f, bool ignoreTiles = true, Func<Chest, bool> chestFilter = null) {
            chest = FindClosestChest(point, maxRange, ignoreTiles, chestFilter);
            return chest != null;
        }

        /// <summary>
        /// 寻找距离指定位置最近的箱子
        /// </summary>
        /// <param name="position">搜索起点，单位为像素</param>
        /// <param name="maxRange">最大搜索距离；如果为-1，则忽略范围限制，单位为像素</param>
        /// <param name="ignoreTiles">是否忽略瓦片遮挡</param>
        /// <param name="chestFilter">可选的箱子过滤器；若为 <see langword="null"/> 则不过滤</param>
        /// <returns>距离最近且符合条件的 <see cref="Chest"/>，若无匹配项则为  <see langword="null"/></returns>
        public static Chest FindClosestChest(this Vector2 position, float maxRange = 3000f, bool ignoreTiles = true, Func<Chest, bool> chestFilter = null) {
            Chest closestChest = null;
            float minDistance = maxRange == -1f ? float.MaxValue : maxRange;

            foreach (Chest chest in Main.chest) {
                if (chest == null) {
                    continue;
                }

                if (chestFilter != null && !chestFilter(chest)) {
                    continue;
                }

                //箱子位置为左上角 Tile，需转换为世界坐标
                Vector2 chestWorldPos = new(chest.x * 16 + 8, chest.y * 16 + 8); //中心点

                float distance = Vector2.Distance(position, chestWorldPos);
                if (distance <= minDistance) {
                    bool canReach = ignoreTiles || Collision.CanHitLine(position, 0, 0, chestWorldPos, 0, 0);

                    if (canReach) {
                        minDistance = distance;
                        closestChest = chest;
                    }
                }
            }

            return closestChest;
        }

        /// <summary>
        /// 寻找距离指定位置最近的箱子
        /// </summary>
        /// <param name="position">搜索起点，单位为像素</param>
        /// <param name="chest">返回的箱子实例</param>
        /// <param name="maxRange">最大搜索距离；如果为-1，则忽略范围限制，单位为像素</param>
        /// <param name="ignoreTiles">是否忽略瓦片遮挡</param>
        /// <param name="chestFilter">可选的箱子过滤器；若为 <see langword="null"/> 则不过滤</param>
        /// <returns>距离最近且符合条件的 <see cref="Chest"/>，若无匹配项则为  <see langword="null"/></returns>
        public static bool TryFindClosestChest(Vector2 position, out Chest chest, float maxRange = 3000f, bool ignoreTiles = true, Func<Chest, bool> chestFilter = null) {
            chest = FindClosestChest(position, maxRange, ignoreTiles, chestFilter);
            return chest != null;
        }

        /// <summary>
        /// 检测玩家是否有效且正常存活
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示活跃，返回 <see langword="false"/> 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this Player player) => player != null && player.active && !player.dead;

        /// <summary>
        /// 检测弹幕是否有效且正常存活
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示活跃，返回 <see langword="false"/> 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this Projectile projectile) => projectile != null && projectile.active && projectile.timeLeft > 0;

        /// <summary>
        /// 检测NPC是否有效且正常存活
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示活跃，返回 <see langword="false"/> 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this NPC npc) => npc != null && npc.active && npc.timeLeft > 0;

        /// <summary>
        /// 检测物品是否有效且正常存活
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示活跃，返回 <see langword="false"/> 表示为空或者已经死亡的非活跃状态</returns>
        public static bool Alives(this Item item) => item != null && item.type > ItemID.None && item.stack > 0;

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
        /// 是否处于愚人节
        /// </summary>
        public static bool IsAprilFoolsDay => DateTime.Now.Month == 4 && DateTime.Now.Day == 1;

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
            if (obj is TileProcessor tp) {
                return new EntitySource_WorldEvent($"{tp.Position.X}:{tp.Position.Y}");
            }
            if (obj is Chest chest) {
                return new EntitySource_WorldEvent($"{chest.x}:{chest.y}");
            }
            if (obj is Point16 point) {
                return new EntitySource_WorldEvent($"{point.X}:{point.Y}");
            }
            return new EntitySource_Parent(Main.LocalPlayer, "NullSource");
        }

        public static bool TryGetOverride(this Item item, out Dictionary<Type, ItemOverride> value) {
            value = null;
            if (Main.gameMenu) {
                return false;
            }
            if (ItemOverride.ByID.TryGetValue(item.type, out value)) {
                return true;
            }
            return value != null;
        }

        public static bool TryGetOverride<T>(this Item item, out T value) where T : NPCOverride {
            value = null;
            if (Main.gameMenu) {
                return false;
            }
            if (ItemOverride.ByID.TryGetValue(item.type, out var values)) {
                value = values[typeof(T)] as T;
                return true;
            }
            return value != null;
        }

        /// <summary>
        /// 获取指定类型 <typeparamref name="T"/> 的 <see cref="NPCOverride"/> 实例
        /// 通常用于直接访问某个特定的重制逻辑节点，无需判断是否存在
        /// 若未在 <see cref="NPCRebuildLoader"/> 中注册对应类型，会抛出异常
        /// </summary>
        /// <typeparam name="T">要获取的重制节点类型</typeparam>
        /// <param name="npc">目标 NPC 实例</param>
        /// <returns><typeparamref name="T"/> 类型对应的 <see cref="NPCOverride"/> 实例</returns>
        /// <exception cref="KeyNotFoundException">如果未注册指定类型的节点</exception>
        public static T GetOverride<T>(this NPC npc) where T : NPCOverride
            => npc.GetGlobalNPC<NPCRebuildLoader>().NPCOverrides[typeof(T)] as T;

        /// <summary>
        /// 尝试获取目标 <paramref name="npc"/> 的全部 <see cref="NPCOverride"/> 节点集合
        /// 该方法通常用于遍历或批量处理所有已注册的重制逻辑节点
        /// </summary>
        /// <param name="npc">目标 NPC 实例</param>
        /// <param name="value">返回对应的重制节点字典，键为类型，值为 <see cref="NPCOverride"/> 实例</param>
        /// <returns>
        /// 如果获取成功并非处于主菜单状态，则返回 <see langword="true"/>；
        /// 否则返回 <see langword="false"/>，此时 <paramref name="value"/> 为 <see langword="null"/>
        /// </returns>
        public static bool TryGetOverride(this NPC npc, out Dictionary<Type, NPCOverride> value) {
            value = null;
            if (Main.gameMenu) {
                return false;
            }
            if (npc.TryGetGlobalNPC(out NPCRebuildLoader globalInstance)) {
                value = globalInstance.NPCOverrides;
            }
            return value != null;
        }

        /// <summary>
        /// 尝试获取目标 <paramref name="npc"/> 的特定类型 <typeparamref name="T"/> 的 <see cref="NPCOverride"/> 实例
        /// </summary>
        /// <typeparam name="T">要尝试获取的重制节点类型</typeparam>
        /// <param name="npc">目标 NPC 实例</param>
        /// <param name="value">返回的 <see cref="NPCOverride"/> 实例，如果未找到则为 <see langword="null"/></param>
        /// <returns>
        /// 如果找到指定类型的重制节点并成功获取，则返回 <see langword="true"/>；
        /// 否则返回 <see langword="false"/>
        /// </returns>
        public static bool TryGetOverride<T>(this NPC npc, out T value) where T : NPCOverride {
            value = null;
            if (Main.gameMenu) {
                return false;
            }
            if (!npc.TryGetGlobalNPC(out NPCRebuildLoader globalInstance)) {
                return false;
            }
            if (!globalInstance.NPCOverrides.TryGetValue(typeof(T), out var value2)) {
                value = value2 as T;
                return false;
            }
            return value != null;
        }

        /// <summary>
        /// 获取指定物品的本地化名字
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static LocalizedText GetLocalizedItemName<T>() where T : ModItem => GetLocalizedItemName(ModContent.ItemType<T>());

        /// <summary>
        /// 获取指定物品的本地化名字
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static LocalizedText GetLocalizedItemName(int id) {
            if (id <= ItemID.None) {
                return null;
            }

            if (id < ItemID.Count) {
                return Language.GetText("ItemName." + ItemID.Search.GetName(id));
            }
            else {
                return ItemLoader.GetItem(id).GetLocalization("DisplayName");
            }
        }

        /// <summary>
        /// 获取指定物品的本地化描述
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static LocalizedText GetLocalizedItemTooltip<T>() where T : ModItem => GetLocalizedItemTooltip(ModContent.ItemType<T>());

        /// <summary>
        /// 获取指定物品的本地化描述
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static LocalizedText GetLocalizedItemTooltip(int id) {
            if (id <= ItemID.None) {
                return null;
            }

            if (id < ItemID.Count) {
                return Language.GetText("ItemTooltip." + ItemID.Search.GetName(id));
            }
            else {
                return ItemLoader.GetItem(id).GetLocalization("Tooltip");
            }
        }

        /// <summary>
        /// 解析字符串键并获取对应的物品类型
        /// </summary>
        /// <param name="fullName">用于解析的字符串键，可以是整数类型或模组/物品名称的组合</param>
        /// <param name="loadVanillaItem">是否自动加载一次原版物品</param>
        /// <returns>解析后得到的物品类型</returns>
        public static int GetItemTypeFromFullName(string fullName, bool loadVanillaItem = false) {
            if (fullName == "Null/Null") {
                return ItemID.None;
            }

            if (int.TryParse(fullName, out int intValue)) {
                if (loadVanillaItem && !isServer) {
                    Main.instance.LoadItem(intValue);
                }
                return intValue;
            }
            else {
                string[] fruits = fullName.Split('/');
                return ModLoader.GetMod(fruits[0]).Find<ModItem>(fruits[1]).Type;
            }
        }

        /// <summary>
        /// 安全加载物品资源
        /// </summary>
        /// <param name="id"></param>
        public static void SafeLoadItem(int id) {
            if (!Main.dedServ && id > 0 && id < TextureAssets.Item.Length && Main.Assets != null && TextureAssets.Item[id] != null) {
                Main.instance.LoadItem(id);
            }
        }
        /// <summary>
        /// 安全加载物品资源
        /// </summary>
        /// <param name="item"></param>
        public static void SafeLoadItem(this Item item) => SafeLoadItem(item.type);

        /// <summary>
        /// 安全加载弹幕资源
        /// </summary>
        /// <param name="id"></param>
        public static void SafeLoadProj(int id) {
            if (!Main.dedServ && id > 0 && id < TextureAssets.Projectile.Length && Main.Assets != null && TextureAssets.Projectile[id] != null) {
                Main.instance.LoadProjectile(id);
            }
        }
        /// <summary>
        /// 安全加载弹幕资源
        /// </summary>
        /// <param name="proj"></param>
        public static void SafeLoadProj(this Projectile proj) => SafeLoadProj(proj.type);

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
        /// 设置玩家鼠标指向物块的信息状态
        /// </summary>
        /// <param name="player"></param>
        /// <param name="itemID"></param>
        public static void SetMouseOverByTile(this Player player, int itemID) {
            player.noThrow = 2;
            player.mouseInterface = true;
            player.cursorItemIconEnabled = true;
            if (itemID > 0) {
                player.cursorItemIconID = itemID;//当玩家鼠标悬停在物块之上时，显示该物品的材质
            }
        }

        /// <summary>
        /// 设置玩家鼠标指向物块的信息状态
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="player"></param>
        public static void SetMouseOverByTile<T>(this Player player) where T : ModItem
            => SetMouseOverByTile(player, ModContent.ItemType<T>());

        /// <summary>
        /// 利用<see cref="Projectile.identity"/>搜索对应的弹幕实例
        /// </summary>
        /// <param name="projectiles"></param>
        /// <param name="identity"></param>
        /// <returns></returns>
        public static Projectile FindByIdentity(this Projectile[] projectiles, int identity) => projectiles.FirstOrDefault(x => x.identity == identity);

        /// <summary>
        /// 实时计算当前所有激活弹幕中，指定ID的弹幕数量
        /// </summary>
        /// <param name="projID">要统计的弹幕类型<see cref="Projectile.type"/></param>
        /// <param name="owner">玩家索引 <see cref="Projectile.owner"/> , 默认为-1，即不启用玩家主人排查</param>
        /// <returns>符合条件的弹幕数量</returns>
        public static int CountProjectilesOfID(int projID, int owner = -1) {
            int num = 0;
            foreach (var proj in Main.ActiveProjectiles) {
                if (owner >= 0 && owner != proj.owner) {
                    continue;
                }
                if (proj.type == projID) {
                    num++;
                }
            }
            return num;
        }
        /// <summary>
        /// 实时计算当前所有激活弹幕中，指定ID的弹幕数量
        /// </summary>
        /// <typeparam name="T">弹幕类型</typeparam>
        /// <param name="owner">玩家索引 <see cref="Projectile.owner"/> , 默认为-1，即不启用玩家主人排查</param>
        /// <returns></returns>
        public static int CountProjectilesOfID<T>(int owner = -1) where T : ModProjectile
            => CountProjectilesOfID(ModContent.ProjectileType<T>(), owner);
        /// <summary>
        /// 实时计算当前所有激活弹幕中，指定ID的弹幕数量
        /// </summary>
        /// <param name="player">玩家实例</param>
        /// <param name="projID">要统计的弹幕类型<see cref="Projectile.type"/></param>
        /// <returns></returns>
        public static int CountProjectilesOfID(this Player player, int projID) => CountProjectilesOfID(projID, player.whoAmI);
        /// <summary>
        /// 实时计算当前所有激活弹幕中，指定ID的弹幕数量
        /// </summary>
        /// <typeparam name="T">弹幕类型</typeparam>
        /// <param name="player">玩家索引 <see cref="Projectile.owner"/> , 默认为-1，即不启用玩家主人排查</param>
        /// <returns></returns>
        public static int CountProjectilesOfID<T>(this Player player) where T : ModProjectile
            => CountProjectilesOfID(ModContent.ProjectileType<T>(), player.whoAmI);

        /// <summary>
        /// 一个模拟原版机制的消耗判定，考虑了大多数情况下的弹药消耗概率因素
        /// </summary>
        /// <param name="player"></param>
        /// <param name="ammo"></param>
        /// <returns>如果返回<see langword="true"/>则代表不消耗</returns>
        public static bool IsRangedAmmoFreeThisShot(this Player player, Item ammo) {
            bool flag2 = false;
            if (player.magicQuiver && ammo.ammo == AmmoID.Arrow && Main.rand.NextBool(5)) {
                flag2 = true;
            }

            if (player.ammoBox && Main.rand.NextBool(5)) {
                flag2 = true;
            }

            if (player.ammoPotion && Main.rand.NextBool(5)) {
                flag2 = true;
            }

            if (player.huntressAmmoCost90 && Main.rand.NextBool(10)) {
                flag2 = true;
            }

            if (player.chloroAmmoCost80 && Main.rand.NextBool(5)) {
                flag2 = true;
            }

            if (player.ammoCost80 && Main.rand.NextBool(5)) {
                flag2 = true;
            }

            if (player.ammoCost75 && Main.rand.NextBool(4)) {
                flag2 = true;
            }

            return flag2;
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
        /// 向指定的 <see cref="Chest"/> 中添加一个物品，支持自动堆叠与空格插入
        /// </summary>
        /// <param name="chest">目标箱子实例</param>
        /// <param name="item">要添加的物品对象</param>
        /// <param name="throwingExcessItems">
        /// 是否在物品无法全部放入箱子时，将剩余部分掉落在箱子实体位置处，默认为<see langword="false"/>
        /// </param>
        public static void AddItem(this Chest chest, Item item, bool throwingExcessItems = false) {
            if (item == null || item.IsAir || item.stack <= 0) {
                return;
            }

            Item toAdd = item.Clone();

            //1.先尝试堆叠到已有相同类型的物品
            for (int i = 0; i < chest.item.Length && toAdd.stack > 0; i++) {
                Item slot = chest.item[i];
                if (slot == null || slot.IsAir) {
                    continue;
                }

                if (slot.type == toAdd.type && slot.stack < slot.maxStack) {
                    int transferable = Math.Min(toAdd.stack, slot.maxStack - slot.stack);
                    slot.stack += transferable;
                    toAdd.stack -= transferable;
                }
            }

            //2.然后尝试放到空格子中
            for (int i = 0; i < chest.item.Length && toAdd.stack > 0; i++) {
                Item slot = chest.item[i];
                if (slot == null || slot.IsAir || slot.type == ItemID.None) {
                    chest.item[i] = toAdd.Clone();
                    chest.item[i].stack = Math.Min(toAdd.stack, chest.item[i].maxStack);
                    toAdd.stack -= chest.item[i].stack;
                }
            }

            if (toAdd == null || toAdd.IsAir || toAdd.stack <= 0) {
                return;
            }

            //3.如果还有剩余物品，考虑丢弃
            if (throwingExcessItems) {
                toAdd.SpwanItem(chest.FromObjectGetParent(), chest.GetPoint16().ToWorldCoordinates());
            }
        }

        /// <summary>
        /// 获取箱子的坐标
        /// </summary>
        /// <param name="chest"></param>
        /// <returns></returns>
        public static Point16 GetPoint16(this Chest chest) => new Point16(chest.x, chest.y);

        /// <summary>
        /// 判断一个物品是否可以完整放入指定的箱子中
        /// </summary>
        /// <param name="chest">目标箱子</param>
        /// <param name="item">要尝试放入的物品</param>
        /// <returns>如果该物品可以完全放入箱子，返回 <see langword="true"/>；否则返回 <see langword="false"/></returns>
        public static bool CanItemBeAddedToChest(this Chest chest, Item item) {
            if (item == null || item.IsAir || item.stack <= 0) {
                return false;
            }

            int remaining = item.stack;

            for (int i = 0; i < chest.item.Length && remaining > 0; i++) {
                Item slot = chest.item[i];

                //可堆叠
                if (slot != null && !slot.IsAir && slot.type == item.type && slot.stack < slot.maxStack) {
                    int space = slot.maxStack - slot.stack;
                    remaining -= Math.Min(remaining, space);
                }
                //空格子可用
                else if (slot == null || slot.IsAir || slot.type == ItemID.None) {
                    remaining -= Math.Min(remaining, item.maxStack);
                }
            }

            return remaining <= 0;
        }

        /// <summary>
        /// 判断一个物品是否可以完整放入指定的箱子中
        /// </summary>
        public static bool CanItemBeAddedToChest(this Chest chest) {
            for (int i = 0; i < chest.item.Length; i++) {
                Item item = chest.item[i];
                if (item == null || item.IsAir || item.stack < item.maxStack) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 在指定位置生成一个物品，生成区域为以 <paramref name="spwanPos"/> 为中心，尺寸为 <paramref name="randomSize"/> 的矩形区域
        /// </summary>
        /// <param name="source">物品生成源（通常为玩家、NPC、事件等）</param>
        /// <param name="spwanPos">中心生成位置</param>
        /// <param name="randomSize">生成区域尺寸，用于制造掉落的“偏移感”</param>
        /// <param name="spwanItem">要生成的物品实例</param>
        /// <param name="netUpdate">是否进行网络同步，默认为 true</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(IEntitySource source, Vector2 spwanPos, Vector2 randomSize, Item spwanItem, bool netUpdate = true) {
            int whoAmi = Item.NewItem(source, spwanPos.GetRectangle(randomSize), spwanItem.Clone());
            if (!isSinglePlayer && netUpdate) {
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, whoAmi, 0f, 0f, 0f, 0, 0, 0);
            }
            return whoAmi;
        }

        /// <summary>
        /// 在指定位置生成一个物品
        /// </summary>
        /// <param name="spwanItem">要生成的物品</param>
        /// <param name="source">物品生成源</param>
        /// <param name="spwanPos">中心生成位置</param>
        /// <param name="randomSize">生成偏移尺寸</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(this Item spwanItem, IEntitySource source, Vector2 spwanPos, Vector2 randomSize, bool netUpdate = true)
            => SpwanItem(source, spwanPos, randomSize, spwanItem, netUpdate);

        /// <summary>
        /// 在指定的矩形区域内生成一个物品
        /// </summary>
        /// <param name="source">物品生成源</param>
        /// <param name="spwanBox">生成区域的矩形框</param>
        /// <param name="spwanItem">要生成的物品实例</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(IEntitySource source, Rectangle spwanBox, Item spwanItem, bool netUpdate = true) {
            int whoAmi = Item.NewItem(source, spwanBox, spwanItem.Clone());
            if (!isSinglePlayer && netUpdate) {
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, whoAmi, 0f, 0f, 0f, 0, 0, 0);
            }
            return whoAmi;
        }

        /// <summary>
        /// 在指定矩形区域内生成一个物品
        /// </summary>
        /// <param name="spwanItem">要生成的物品</param>
        /// <param name="source">生成源</param>
        /// <param name="spwanBox">生成区域</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(this Item spwanItem, IEntitySource source, Rectangle spwanBox, bool netUpdate = true)
            => SpwanItem(source, spwanBox, spwanItem, netUpdate);

        /// <summary>
        /// 在指定中心位置生成一个物品，其生成区域大小为该物品尺寸的一半
        /// </summary>
        /// <param name="source">生成源</param>
        /// <param name="spwanPos">生成中心位置</param>
        /// <param name="spwanItem">要生成的物品实例</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(IEntitySource source, Vector2 spwanPos, Item spwanItem, bool netUpdate = true) {
            int whoAmi = Item.NewItem(source, spwanPos.GetRectangle(spwanItem.Size / 2), spwanItem.Clone());
            if (!isSinglePlayer && netUpdate) {
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, whoAmi, 0f, 0f, 0f, 0, 0, 0);
            }
            return whoAmi;
        }

        /// <summary>
        /// 在指定位置生成物品，区域大小为物品尺寸一半
        /// </summary>
        /// <param name="spwanItem">要生成的物品</param>
        /// <param name="source">生成源</param>
        /// <param name="spwanPos">中心生成位置</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(this Item spwanItem, IEntitySource source, Vector2 spwanPos, bool netUpdate = true)
            => SpwanItem(source, spwanPos, spwanItem, netUpdate);

        /// <summary>
        /// 在指定中心位置生成一个物品，其生成区域大小为该物品尺寸的一半
        /// </summary>
        /// <param name="source">生成源</param>
        /// <param name="spwanItem">要生成的物品实例</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(IEntitySource source, Item spwanItem, bool netUpdate = true) {
            int whoAmi = Item.NewItem(source, spwanItem.position.GetRectangle(spwanItem.Size / 2), spwanItem.Clone());
            if (!isSinglePlayer && netUpdate) {
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, whoAmi, 0f, 0f, 0f, 0, 0, 0);
            }
            return whoAmi;
        }

        /// <summary>
        /// 在指定位置生成物品，区域大小为物品尺寸一半
        /// </summary>
        /// <param name="spwanItem">要生成的物品</param>
        /// <param name="source">生成源</param>
        /// <param name="netUpdate">是否网络同步</param>
        /// <returns>生成物品的索引 ID</returns>
        public static int SpwanItem(this Item spwanItem, IEntitySource source, bool netUpdate = true)
            => SpwanItem(source, spwanItem, netUpdate);

        /// <summary>
        /// 在游戏中发送文本消息
        /// </summary>
        /// <param name="message">要发送的消息文本</param>
        /// <param name="colour">消息的颜色,默认为 <see langword="null"/></param>
        public static void Text(string message, Color? colour = null) {
            Color newColor = (Color)(colour == null ? Color.White : colour);
            if (isServer) {
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), (Color)(colour == null ? Color.White : colour));
                return;
            }
            Main.NewText(message, newColor);
        }

        /// <summary>
        /// 一个根据语言选项返回字符的方法
        /// </summary>
        public static string Translation(string Chinese = null, string English = null, string Spanish = null, string Russian = null) {
            English ??= "Invalid Character";
            string text = Language.ActiveCulture.LegacyId switch {
                (int)GameCulture.CultureName.Chinese => Chinese,
                (int)GameCulture.CultureName.Russian => Russian,
                (int)GameCulture.CultureName.Spanish => Spanish,
                (int)GameCulture.CultureName.English => English,
                _ => English,
            };
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
        /// 将文本拆分为多行，并为每行分别添加颜色代码
        /// </summary>
        /// <param name="textContent">输入的文本内容，支持换行符</param>
        /// <param name="color">颜色对象</param>
        /// <returns>格式化后的多行带颜色文本</returns>
        public static string FormatColorTextMultiLine(string textContent, Color color) {
            if (string.IsNullOrEmpty(textContent)) {
                return string.Empty;
            }

            // 将颜色转换为 16 进制字符串
            string hexColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";

            // 按换行符分割文本
            string[] lines = textContent.Split('\n');

            // 对每一行添加颜色代码
            for (int i = 0; i < lines.Length; i++) {
                lines[i] = $"[c/{hexColor}:{lines[i]}]";
            }

            // 使用换行符重新组合
            return string.Join("\n", lines);
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
                ammoState.MinAmountItem = itemInds[^1];  // 最小数量的物品
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
        /// 尝试在考虑网络状态的前提下生成一个 Boss NPC
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="player">触发生成的玩家实例</param>
        /// <param name="checkLocalPlayer">是否只允许本地玩家触发生成</param>
        /// <returns></returns>
        public static bool TrySpawnBossWithNet<T>(Player player, bool checkLocalPlayer = true) where T : ModNPC
            => TrySpawnBossWithNet(player, ModContent.NPCType<T>(), checkLocalPlayer);

        /// <summary>
        /// 尝试在考虑网络状态的前提下生成一个 Boss NPC
        /// </summary>
        /// <param name="player">触发生成的玩家实例</param>
        /// <param name="bossType">要生成的 Boss 的类型</param>
        /// <param name="checkLocalPlayer">是否只允许本地玩家触发生成</param>
        /// <returns>是否成功发起了 Boss 的生成请求</returns>
        public static bool TrySpawnBossWithNet(Player player, int bossType, bool checkLocalPlayer = true) {
            // 若启用了本地玩家检查，但当前玩家不是本地玩家，则中止
            if (checkLocalPlayer && player.whoAmI != Main.myPlayer) {
                return false;
            }

            SoundEngine.PlaySound(SoundID.Roar, player.position);

            if (isSinglePlayer || isServer) {
                // 本地或服务器：直接生成 Boss
                NPC.SpawnOnPlayer(player.whoAmI, bossType);
                return true;
            }
            else if (isClient) {
                // 多人客户端：发送生成请求给服务器
                NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: bossType);
                return true;
            }

            // 不在任何有效模式下
            return false;
        }


        /// <summary>
        /// 发送关于一个<see cref="Point16"/>结构的网络数据
        /// </summary>
        /// <param name="modPacket"></param>
        /// <param name="point16"></param>
        public static void WritePoint16(this ModPacket modPacket, Point16 point16) {
            modPacket.Write(point16.X);
            modPacket.Write(point16.Y);
        }

        /// <summary>
        /// 发送关于一个<see cref="Point16"/>结构的网络数据
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="point16"></param>
        public static void WritePoint16(this BinaryWriter writer, Point16 point16) {
            writer.Write(point16.X);
            writer.Write(point16.Y);
        }

        /// <summary>
        /// 接收关于一个<see cref="Point16"/>结构的网络数据
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

            //一个关于TP的G钩子，用于修改一些特殊物块对于TP的实体的判定
            if (TileProcessorLoader.TargetTileTypes.Contains(tile.TileType)) {
                Point16? gPoint = null;
                foreach (var gTP in TileProcessorLoader.TPGlobalHooks) {
                    gPoint = gTP.GetTopLeftOrNull(tile, i, j);
                }
                if (gPoint.HasValue) {
                    return gPoint.Value;
                }
            }

            // 如果没有物块，返回null
            if (!tile.HasTile) {
                return null;
            }

            // 获取物块的数据结构，如果为null则认为是单个物块
            TileObjectData data = TileObjectData.GetTileData(tile);

            // 如果是单个物块，直接返回当前坐标
            if (data == null) {
                return new Point16(i, j);
            }

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
        /// 获取给定坐标的物块左上角位置，并判断该位置是否为多结构物块的左上角
        /// </summary>
        /// <param name="point">物块的坐标</param>
        /// <returns>
        /// 如果物块存在并且位于一个多结构物块的左上角，返回其左上角坐标，否则返回null
        /// </returns>
        public static Point16? GetTopLeftOrNull(Point16 point) => GetTopLeftOrNull(point.X, point.Y);

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
        /// 判断给定坐标是否为多结构物块的左上角位置，并输出左上角的坐标
        /// </summary>
        /// <param name="origPoint">物块的坐标</param>
        /// <param name="point">输出的左上角坐标，如果不是左上角则为(0,0)</param>
        /// <returns>如果是左上角，返回true，否则返回false </returns>
        public static bool IsTopLeft(Point16 origPoint, out Point16 point) => IsTopLeft(origPoint.X, origPoint.Y, out point);

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
        /// 安全的获取多结构物块左上角的位置，给定一个物块坐标，自动寻找到该坐标对应的左上原点位置输出
        /// </summary>
        /// <param name="origPoint"></param>
        /// <param name="point"></param>
        /// <returns>如果没能找到，则输出(0,0)，并返回<see langword="false"/></returns>
        public static bool SafeGetTopLeft(Point16 origPoint, out Point16 point) => SafeGetTopLeft(origPoint.X, origPoint.Y, out point);

        /// <summary>
        /// 获取一个物块的掉落物ID
        /// </summary>
        /// <param name="tile"></param>
        /// <returns></returns>
        public static int GetTileDorp(this Tile tile) {
            int stye = TileObjectData.GetTileStyle(tile);
            if (stye == -1) {
                stye = 0;
            }

            return TileLoader.GetItemDropFromTypeAndStyle(tile.TileType, stye);
        }

        /// <summary>
        /// 检查给定的 <see cref="Vector2"/> 坐标点是否位于世界范围内
        /// </summary>
        /// <param name="vector">要检查的世界坐标，以像素为单位</param>
        /// <returns>如果坐标转换为 <see cref="Point16"/> 后仍然位于世界范围内，则返回<see langword="true"/>，否则返回<see langword="false"/></returns>
        /// <remarks>
        /// 由于世界的物块坐标是 16×16 像素的网格，因此本方法会将 <paramref name="vector"/> 的 X 和 Y 坐标
        /// 除以 16 并取整，以转换为 tile 坐标，然后调用 <see cref="InWorld(Point16)"/> 进行判断
        /// </remarks>
        public static bool InWorld(Vector2 vector) => InWorld(new Point16((int)(vector.X / 16), (int)(vector.Y / 16)));

        /// <summary>
        /// 检查给定的 <see cref="Point16"/> 坐标点是否位于世界范围内
        /// </summary>
        /// <param name="point">要检查的 tile 坐标，以 <see cref="Point16"/> 形式表示</param>
        /// <returns>如果点位于世界范围内，则返回<see langword="true"/>，否则返回<see langword="false"/></returns>
        /// <remarks>
        /// 本方法是对 <see cref="WorldGen.InWorld"/> 方法的封装，以支持 <see cref="Point16"/> 类型的参数
        /// 适用于 tile 级别的世界范围判断，而非像素级别的坐标检查
        /// </remarks>
        public static bool InWorld(Point16 point) => WorldGen.InWorld(point.X, point.Y);

        /// <summary>
        /// 检查多个 <see cref="Point16"/> 坐标点是否全部位于世界范围内
        /// </summary>
        /// <param name="points">要检查的多个 tile 坐标点</param>
        /// <returns>如果所有点都位于世界范围内，则返回<see langword="true"/>，否则返回<see langword="false"/></returns>
        public static bool InWorld(params Point16[] points) {
            foreach (var point in points) {
                if (!WorldGen.InWorld(point.X, point.Y)) {
                    return false; // 只要有一个点不在世界范围内，就返回 false
                }
            }
            return true; // 所有点都在世界范围内
        }

        #endregion

        #region Draw
        /// <summary>
        /// 计算并返回屏幕变换矩阵（World-View-Projection 矩阵）
        /// </summary>
        /// <param name="offset">
        /// 世界坐标的平移偏移量（默认值为 -Main.screenPosition）
        /// 这用于调整世界空间的原点，使其相对于屏幕中心进行渲染
        /// </param>
        /// <param name="matrix">
        /// 视图矩阵（View Matrix），默认为 Main.GameViewMatrix.TransformationMatrix
        /// 该矩阵用于从世界空间转换到摄像机视角，控制视角的缩放、旋转和偏移
        /// </param>
        /// <param name="screenWidth">
        /// 屏幕宽度（默认值为 Main.screenWidth）
        /// 该参数用于定义投影矩阵的宽度，通常与游戏窗口的宽度保持一致
        /// </param>
        /// <param name="screenHeight">
        /// 屏幕高度（默认值为 Main.screenHeight）
        /// 该参数用于定义投影矩阵的高度，通常与游戏窗口的高度保持一致
        /// </param>
        /// <returns>
        /// 返回一个组合后的变换矩阵（World * View * Projection）
        /// 该矩阵用于将世界空间坐标转换为屏幕坐标，适用于 2D 渲染（如 UI 元素、精灵绘制）
        /// </returns>
        public static Matrix GetTransfromMatrix(Vector3? offset = null, Matrix? matrix = null, int? screenWidth = null, int? screenHeight = null) {
            Matrix world = Matrix.CreateTranslation(offset ?? -Main.screenPosition.ToVector3());
            Matrix view = matrix ?? Main.GameViewMatrix.TransformationMatrix;
            Matrix projection = Matrix.CreateOrthographicOffCenter(0, screenWidth ?? Main.screenWidth, screenHeight ?? Main.screenHeight, 0, -1, 1);
            return world * view * projection;
        }
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

        /// <summary>
        /// 获取完整纹理的矩形区域
        /// </summary>
        /// <param name="value">纹理对象</param>
        /// <returns>完整纹理的 Rectangle 区域</returns>
        public static Rectangle GetRectangle(this Texture2D value)
            => new Rectangle(0, 0, value.Width, value.Height);

        /// <summary>
        /// 获取分帧后的某一帧对应的矩形区域
        /// </summary>
        /// <param name="value">纹理对象</param>
        /// <param name="frame">当前帧索引</param>
        /// <param name="maxFrame">总帧数</param>
        /// <returns>对应帧的 Rectangle 区域</returns>
        public static Rectangle GetRectangle(this Texture2D value, int frame, int maxFrame = 1) {
            int singleFrameY = value.Height / maxFrame;
            return new Rectangle(0, singleFrameY * frame, value.Width, singleFrameY);
        }

        /// <summary>
        /// 获取纹理的中心点作为绘制原点
        /// </summary>
        /// <param name="value">纹理对象</param>
        /// <returns>原点坐标</returns>
        public static Vector2 GetOrig(this Texture2D value) => value.Size() / 2;

        /// <summary>
        /// 获取分帧纹理中某一帧的中心点作为绘制原点
        /// </summary>
        /// <param name="value">纹理对象</param>
        /// <param name="maxFrame">帧总数</param>
        /// <returns>某帧的原点坐标</returns>
        public static Vector2 GetOrig(this Texture2D value, int maxFrame) =>
            new Vector2(value.Width / 2, value.Height / maxFrame / 2);

        /// <summary>
        /// 控制帧动画的播放，基于游戏更新频率定时播放
        /// </summary>
        /// <param name="frame">当前帧引用</param>
        /// <param name="intervalFrame">每几帧切换一次动画</param>
        /// <param name="maxFrame">帧上限（包含）</param>
        /// <param name="minFrame">帧起始（默认 0）</param>
        public static void ClockFrame(ref int frame, int intervalFrame, int maxFrame, int minFrame = 0) {
            if (frame < minFrame) {
                frame = minFrame;
            }

            if (Main.GameUpdateCount % intervalFrame == 0) {
                frame++;
            }

            if (frame > maxFrame) {
                frame = minFrame;
            }
        }

        /// <summary>
        /// 控制帧动画的播放，基于游戏更新频率定时播放
        /// </summary>
        /// <param name="frame">当前帧引用</param>
        /// <param name="intervalFrame">每几帧切换一次动画</param>
        /// <param name="maxFrame">帧上限</param>
        /// <param name="minFrame">帧起始，默认0</param>
        public static void ClockFrame(ref double frame, int intervalFrame, int maxFrame, int minFrame = 0) {
            if (frame < minFrame) {
                frame = minFrame;
            }

            if (Main.GameUpdateCount % intervalFrame == 0) {
                frame++;
            }

            if (frame > maxFrame) {
                frame = minFrame;
            }
        }

        /// <summary>
        /// 控制帧动画的播放，基于游戏更新频率定时播放
        /// </summary>
        /// <param name="frame">当前帧引用</param>
        /// <param name="intervalFrame">每几帧切换一次动画</param>
        /// <param name="maxFrame">帧上限</param>
        /// <param name="minFrame">帧起始，默认0</param>
        public static void ClockFrame(ref float frame, int intervalFrame, int maxFrame, int minFrame = 0) {
            if (frame < minFrame) {
                frame = minFrame;
            }

            if (Main.GameUpdateCount % intervalFrame == 0) {
                frame++;
            }

            if (frame > maxFrame) {
                frame = minFrame;
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
        /// 判断给定的二维点是否在屏幕内（考虑一个小范围的边界扩展）
        /// </summary>
        /// <param name="pos">要判断的点的坐标</param>
        /// <param name="extend">扩张范围</param>
        /// <returns>如果点在屏幕范围内（包括扩展边界），返回 true；否则返回 false</returns>
        public static bool IsPointOnScreen(Vector2 pos, int extend)
            => pos.X > -extend && pos.X < Main.screenWidth + extend && pos.Y > -extend && pos.Y < Main.screenHeight + extend;

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
