using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度工具类,提供维度相关的实用方法
    /// </summary>
    public static class DimensionUtils
    {
        #region 维度查询

        /// <summary>
        /// 通过完整名称查找维度
        /// </summary>
        public static Dimension FindDimension(string fullName) {
            DimensionSystem.dimensionsByFullName.TryGetValue(fullName, out Dimension dimension);
            return dimension;
        }

        /// <summary>
        /// 通过类型查找维度
        /// </summary>
        public static T FindDimension<T>() where T : Dimension {
            if (DimensionSystem.dimensionsByType.TryGetValue(typeof(T), out Dimension dimension)) {
                return dimension as T;
            }
            return null;
        }

        /// <summary>
        /// 获取所有指定模组的维度
        /// </summary>
        public static List<Dimension> GetDimensionsFromMod(Mod mod) {
            if (DimensionSystem.dimensionsByMod.TryGetValue(mod, out List<Dimension> dimensions)) {
                return new List<Dimension>(dimensions); // 返回副本以防止外部修改
            }
            return new List<Dimension>();
        }

        /// <summary>
        /// 获取所有指定层级的维度
        /// </summary>
        public static List<Dimension> GetDimensionsByLayer(DimensionLayer layer) {
            if (DimensionSystem.dimensionsByLayer.TryGetValue(layer, out List<Dimension> dimensions)) {
                return new List<Dimension>(dimensions); // 返回副本以防止外部修改
            }
            return new List<Dimension>();
        }

        #endregion

        #region 坐标转换

        /// <summary>
        /// 将主世界坐标转换为维度坐标
        /// </summary>
        public static Point MainWorldToDimension(Point mainWorldPos, Dimension dimension) {
            if (dimension == null)
                return mainWorldPos;

            float scaleX = (float)dimension.Width / Main.maxTilesX;
            float scaleY = (float)dimension.Height / Main.maxTilesY;

            return new Point(
                (int)(mainWorldPos.X * scaleX),
                (int)(mainWorldPos.Y * scaleY)
            );
        }

        /// <summary>
        /// 将维度坐标转换为主世界坐标
        /// </summary>
        public static Point DimensionToMainWorld(Point dimensionPos, Dimension dimension) {
            if (dimension == null)
                return dimensionPos;

            float scaleX = (float)Main.maxTilesX / dimension.Width;
            float scaleY = (float)Main.maxTilesY / dimension.Height;

            return new Point(
                (int)(dimensionPos.X * scaleX),
                (int)(dimensionPos.Y * scaleY)
            );
        }

        /// <summary>
        /// 在两个维度之间转换坐标
        /// </summary>
        public static Point ConvertBetweenDimensions(Point pos, Dimension from, Dimension to) {
            if (from == null || to == null)
                return pos;

            float scaleX = (float)to.Width / from.Width;
            float scaleY = (float)to.Height / from.Height;

            return new Point(
                (int)(pos.X * scaleX),
                (int)(pos.Y * scaleY)
            );
        }

        #endregion

        #region 玩家操作

        /// <summary>
        /// 安全地传送玩家到维度
        /// </summary>
        public static bool TeleportPlayerToDimension(Player player, string dimensionFullName, Vector2? spawnPosition = null) {
            if (player == null || VaultUtils.isServer)
                return false;

            Dimension dimension = FindDimension(dimensionFullName);
            if (dimension == null || !dimension.CanEnter(player))
                return false;

            bool success = DimensionSystem.Enter(dimensionFullName);

            if (success && spawnPosition.HasValue) {
                //设置玩家生成位置
                Main.spawnTileX = (int)spawnPosition.Value.X / 16;
                Main.spawnTileY = (int)spawnPosition.Value.Y / 16;
            }

            return success;
        }

        /// <summary>
        /// 将玩家踢回主世界
        /// </summary>
        public static void KickToMainWorld(Player player) {
            if (player == null || !DimensionSystem.AnyActive())
                return;

            DimensionSystem.Exit();
        }

        #endregion

        #region 环境效果

        /// <summary>
        /// 应用维度环境色调
        /// </summary>
        public static Color ApplyDimensionTint(Color original, Dimension dimension) {
            if (dimension?.Environment == null)
                return original;

            Color tint = dimension.Environment.ColorTint;
            return new Color(
                (byte)((original.R * tint.R) / 255),
                (byte)((original.G * tint.G) / 255),
                (byte)((original.B * tint.B) / 255),
                original.A
            );
        }

        /// <summary>
        /// 计算维度雾效
        /// </summary>
        public static Color CalculateFog(Color baseColor, float distance, Dimension dimension) {
            if (dimension?.Environment == null || dimension.Environment.FogDensity <= 0)
                return baseColor;

            float fogAmount = 1f - (float)Math.Exp(-distance * dimension.Environment.FogDensity);
            fogAmount = Math.Clamp(fogAmount, 0f, 1f);

            return Color.Lerp(baseColor, dimension.Environment.FogColor, fogAmount);
        }

        /// <summary>
        /// 生成环境粒子
        /// </summary>
        public static void SpawnAmbientParticles(Dimension dimension) {
            if (dimension?.Environment == null || dimension.Environment.AmbientParticles.Count == 0)
                return;

            if (Main.rand.NextFloat() > dimension.Environment.ParticleSpawnRate)
                return;

            int particleType = Main.rand.Next(dimension.Environment.AmbientParticles);
            Vector2 position = Main.LocalPlayer.Center + new Vector2(
                Main.rand.Next(-Main.screenWidth / 2, Main.screenWidth / 2),
                Main.rand.Next(-Main.screenHeight / 2, Main.screenHeight / 2)
            );

            Dust.NewDust(position, 0, 0, particleType);
        }

        #endregion

        #region 时间操作

        /// <summary>
        /// 设置维度时间
        /// </summary>
        public static void SetDimensionTime(double time, bool isDayTime) {
            if (!DimensionSystem.AnyActive())
                return;

            if (DimensionSystem.Current.EnableTimeOfDay) {
                Main.time = time;
                Main.dayTime = isDayTime;
            }
        }

        /// <summary>
        /// 加速或减慢维度时间
        /// </summary>
        public static void ModifyTimeScale(float newScale) {
            if (!DimensionSystem.AnyActive())
                return;

            //通过反射或其他方式修改时间流速
            //注意: 这需要维度类支持动态修改TimeScale
        }

        /// <summary>
        /// 冻结维度时间
        /// </summary>
        public static void FreezeTime() {
            ModifyTimeScale(0f);
        }

        /// <summary>
        /// 时间倒流
        /// </summary>
        public static void ReverseTime() {
            ModifyTimeScale(-1f);
        }

        #endregion

        #region 维度验证

        /// <summary>
        /// 检查维度是否有效
        /// </summary>
        public static bool IsValidDimension(Dimension dimension) {
            if (dimension == null)
                return false;

            return dimension.Width > 0 &&
                   dimension.Height > 0 &&
                   dimension.GenerationTasks != null &&
                   dimension.GenerationTasks.Count > 0;
        }

        /// <summary>
        /// 检查玩家是否可以进入维度
        /// </summary>
        public static bool CanPlayerEnterDimension(Player player, Dimension dimension) {
            if (player == null || dimension == null)
                return false;

            //检查最大玩家数限制
            if (dimension.MaxPlayers > 0) {
                int currentPlayers = GetPlayerCountInDimension(dimension);
                if (currentPlayers >= dimension.MaxPlayers)
                    return false;
            }

            return dimension.CanEnter(player);
        }

        /// <summary>
        /// 获取维度中的玩家数量
        /// </summary>
        public static int GetPlayerCountInDimension(Dimension dimension) {
            if (dimension != DimensionSystem.Current)
                return 0;

            int count = 0;
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (Main.player[i].active)
                    count++;
            }
            return count;
        }

        #endregion

        #region 数据持久化

        /// <summary>
        /// 保存维度自定义数据
        /// </summary>
        public static void SaveCustomData(string key, object value) {
            DimensionSystem.CopyData(key, value);
        }

        /// <summary>
        /// 读取维度自定义数据
        /// </summary>
        public static T LoadCustomData<T>(string key, T defaultValue = default) {
            try {
                return DimensionSystem.ReadData<T>(key);
            } catch {
                return defaultValue;
            }
        }

        #endregion

        #region 维度层级工具

        /// <summary>
        /// 获取维度的父维度
        /// </summary>
        public static Dimension GetParentDimension(Dimension dimension) {
            if (dimension == null || dimension.Layer != DimensionLayer.Sub)
                return null;

            int parentIndex = dimension.ParentDimensionIndex;
            if (DimensionSystem.dimensionsByIndex.TryGetValue(parentIndex, out Dimension parent)) {
                return parent;
            }
            return null;
        }

        /// <summary>
        /// 获取维度的所有子维度
        /// </summary>
        public static List<Dimension> GetChildDimensions(Dimension dimension) {
            if (dimension == null)
                return new List<Dimension>();

            int parentIndex = DimensionSystem.GetIndex(dimension.FullName);
            
            List<Dimension> children = new List<Dimension>();
            if (DimensionSystem.dimensionsByLayer.TryGetValue(DimensionLayer.Sub, out List<Dimension> subDimensions)) {
                foreach (Dimension subDim in subDimensions) {
                    if (subDim.ParentDimensionIndex == parentIndex) {
                        children.Add(subDim);
                    }
                }
            }
            return children;
        }

        /// <summary>
        /// 检查两个维度是否在同一层级链中
        /// </summary>
        public static bool InSameHierarchy(Dimension dim1, Dimension dim2) {
            if (dim1 == null || dim2 == null)
                return false;

            //向上追溯到根维度
            Dimension root1 = GetRootDimension(dim1);
            Dimension root2 = GetRootDimension(dim2);

            return root1 == root2;
        }

        /// <summary>
        /// 获取维度层级的根维度
        /// </summary>
        public static Dimension GetRootDimension(Dimension dimension) {
            if (dimension == null)
                return null;

            Dimension current = dimension;
            while (current.Layer == DimensionLayer.Sub) {
                Dimension parent = GetParentDimension(current);
                if (parent == null)
                    break;
                current = parent;
            }

            return current;
        }

        #endregion

        #region 调试工具

        /// <summary>
        /// 打印维度信息
        /// </summary>
        public static void PrintDimensionInfo(Dimension dimension) {
            if (dimension == null) {
                Main.NewText("维度为空", Color.Red);
                return;
            }

            Main.NewText($"=== 维度信息 ===", Color.Gold);
            Main.NewText($"名称: {dimension.DisplayName.Value}");
            Main.NewText($"大小: {dimension.Width} x {dimension.Height}");
            Main.NewText($"层级: {dimension.Layer}");
            Main.NewText($"时间流速: {dimension.TimeScale}x");
            Main.NewText($"重力倍率: {dimension.GetGravityMultiplier(Main.LocalPlayer)}x");
            Main.NewText($"是否保存: {(dimension.ShouldSave ? "是" : "否")}");
            Main.NewText($"是否临时: {(dimension.IsTemporary ? "是" : "否")}");
        }

        /// <summary>
        /// 列出所有已注册的维度
        /// </summary>
        public static void ListAllDimensions() {
            Main.NewText("=== 已注册维度列表 ===", Color.Gold);

            if (DimensionSystem.registeredDimensions == null || DimensionSystem.registeredDimensions.Count == 0) {
                Main.NewText("没有已注册的维度", Color.Gray);
                return;
            }

            for (int i = 0; i < DimensionSystem.registeredDimensions.Count; i++) {
                Dimension dim = DimensionSystem.registeredDimensions[i];
                string current = dim == DimensionSystem.Current ? " [当前]" : "";
                Main.NewText($"{i}. {dim.DisplayName.Value} ({dim.Layer}){current}", Color.White);
            }
        }

        #endregion
    }
}
