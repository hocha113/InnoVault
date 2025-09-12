using Terraria.DataStructures;
using static InnoVault.TileProcessors.TileProcessorLoader;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 工具类，提供了对 <see cref="TileProcessorLoader"/> 中常用静态函数的快捷访问方式
    /// </summary>
    public static class TPUtils
    {
        #region ID 获取 (Get ID)

        /// <summary>
        /// 根据TP的类型获取其注册的ID
        /// </summary>
        /// <typeparam name="T">继承自 <see cref="TileProcessor"/> 的类型</typeparam>
        /// <returns>TP的ID</returns>
        /// <seealso cref="GetModuleID{T}()"/>
        public static int GetID<T>() where T : TileProcessor => GetModuleID<T>();

        /// <summary>
        /// 根据TP的完整内部名 (ModName/ClassName) 获取其注册的ID
        /// </summary>
        /// <param name="fullName">TP的完整内部名</param>
        /// <returns>TP的ID</returns>
        /// <seealso cref="GetModuleID(string)"/>
        public static int GetID(string fullName) => GetModuleID(fullName);

        /// <summary>
        /// 尝试根据TP的完整内部名 (ModName/ClassName) 获取其注册的ID
        /// </summary>
        /// <param name="fullName">TP的完整内部名</param>
        /// <param name="id">如果成功，则输出TP的ID</param>
        /// <returns>如果找到了对应的ID，则返回 <see langword="true"/>；否则返回 <see langword="false"/></returns>
        /// <seealso cref="TryGetTpID(string, out int)"/>
        public static bool TryGetID(string fullName, out int id) => TryGetTpID(fullName, out id);

        #endregion

        #region 位置查询 (Position Queries)

        /// <summary>
        /// 判断给定的物块坐标是否是一个多格结构物块的左上角
        /// </summary>
        /// <param name="i">物块的X坐标</param>
        /// <param name="j">物块的Y坐标</param>
        /// <param name="topLeft">如果为左上角，则输出左上角的坐标</param>
        /// <returns>如果该点是左上角，则返回 <see langword="true"/></returns>
        /// <seealso cref="TileProcessorIsTopLeft(int, int, out Point16)"/>
        public static bool IsTopLeft(int i, int j, out Point16 topLeft) => TileProcessorIsTopLeft(i, j, out topLeft);

        /// <summary>
        /// 安全地获取指定坐标所在的多格结构物块的左上角坐标
        /// </summary>
        /// <param name="i">物块的X坐标</param>
        /// <param name="j">物块的Y坐标</param>
        /// <param name="topLeft">如果找到，则输出左上角的坐标</param>
        /// <returns>如果成功找到左上角，则返回 <see langword="true"/></returns>
        /// <seealso cref="TileProcessorSafeGetTopLeft(int, int, out Point16)"/>
        public static bool TryGetTopLeft(int i, int j, out Point16 topLeft) => TileProcessorSafeGetTopLeft(i, j, out topLeft);

        #endregion

        #region TP 实例获取 (Get TP Instance)

        /// <summary>
        /// 尝试获取指定坐标（必须是左上角）的TP实体
        /// </summary>
        /// <param name="position">物块的左上角坐标</param>
        /// <param name="tp">如果找到，则输出TP实例</param>
        /// <returns>如果该坐标存在TP实例，则返回 <see langword="true"/></returns>
        /// <seealso cref="ByPositionGetTP(Point16, out TileProcessor)"/>
        public static bool TryGetTP(Point16 position, out TileProcessor tp) => ByPositionGetTP(position, out tp);

        /// <summary>
        /// 尝试获取指定坐标（必须是左上角）的特定类型的TP实体
        /// </summary>
        /// <typeparam name="T">要获取的TP类型</typeparam>
        /// <param name="position">物块的左上角坐标</param>
        /// <param name="tp">如果找到且类型匹配，则输出TP实例</param>
        /// <returns>如果该坐标存在TP实例且类型匹配，则返回 <see langword="true"/></returns>
        /// <seealso cref="ByPositionGetTP{T}(Point16, out T)"/>
        public static bool TryGetTP<T>(Point16 position, out T tp) where T : TileProcessor => ByPositionGetTP(position, out tp);

        /// <summary>
        /// 自动识别并获取指定坐标（可以是多格结构的任意部分）上的TP实体
        /// <br/>这个函数会自动寻找物块的左上角
        /// </summary>
        /// <param name="i">物块的X坐标</param>
        /// <param name="j">物块的Y坐标</param>
        /// <param name="tp">如果找到，则输出TP实例</param>
        /// <returns>如果成功找到TP实例，则返回 <see langword="true"/></returns>
        /// <seealso cref="AutoPositionGetTP{T}(int, int, out T)"/>
        public static bool TryGetTPAt<T>(int i, int j, out T tp) where T : TileProcessor => AutoPositionGetTP(i, j, out tp);

        /// <summary>
        /// 自动识别并获取指定坐标（可以是多格结构的任意部分）上的TP实体
        /// <br/>这个函数会自动寻找物块的左上角
        /// </summary>
        /// <param name="position">物块的坐标</param>
        /// <param name="tp">如果找到，则输出TP实例</param>
        /// <returns>如果成功找到TP实例，则返回 <see langword="true"/></returns>
        /// <seealso cref="AutoPositionGetTP{T}(Point16, out T)"/>
        public static bool TryGetTPAt<T>(Point16 position, out T tp) where T : TileProcessor => AutoPositionGetTP(position, out tp);

        #endregion

        #region TP 范围搜索 (Range Search)

        /// <summary>
        /// 在指定坐标和半径范围内，查找最近的特定类型的TP实体
        /// </summary>
        /// <typeparam name="T">要查找的TP类型</typeparam>
        /// <param name="center">搜索中心的物块坐标</param>
        /// <param name="maxRange">最大搜索范围（以物块为单位）</param>
        /// <returns>如果找到，则返回最近的TP实例；否则返回 <see langword="null"/></returns>
        /// <seealso cref="FindModuleRangeSearch{T}(Point16, int)"/>
        public static T FindNearestTP<T>(Point16 center, int maxRange) where T : TileProcessor => FindModuleRangeSearch<T>(center, maxRange);

        /// <summary>
        /// 在指定坐标和半径范围内，查找最近的特定ID的TP实体
        /// </summary>
        /// <param name="tpID">要查找的TP的ID</param>
        /// <param name="centerX">搜索中心的X坐标</param>
        /// <param name="centerY">搜索中心的Y坐标</param>
        /// <param name="maxRange">最大搜索范围（以物块为单位）</param>
        /// <returns>如果找到，则返回最近的TP实例；否则返回 <see langword="null"/></returns>
        /// <seealso cref="FindModuleRangeSearch(int, int, int, int)"/>
        public static TileProcessor FindNearestTP(int tpID, int centerX, int centerY, int maxRange) => FindModuleRangeSearch(tpID, centerX, centerY, maxRange);

        #endregion
    }
}
