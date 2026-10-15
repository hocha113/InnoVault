using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.UI;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于修改游戏菜单的基类
    /// <br>该API的使用介绍:<see href="https://innovault.wiki/cn/overrides/menu/"/></br>
    /// </summary>
    public abstract class MenuOverride : VaultType<MenuOverride>
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public new static List<MenuOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public new static Dictionary<Type, MenuOverride> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() {
            Instances.Add(this);
            TypeToInstance[GetType()] = this;
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }
        /// <summary>
        /// 绘制菜单主页，在这个函数中需要注意线程安全
        /// </summary>
        /// <param name="gameTime"></param>
        /// <returns></returns>
        public virtual bool? DrawMenu(GameTime gameTime) {
            return null;
        }
        /// <summary>
        /// 绘制菜单主页，在这个函数中需要注意线程安全<br/>
        /// 调用在<see cref="DrawMenu"/>与<see cref="Main.DrawMenu(GameTime)"/>之后
        /// </summary>
        /// <param name="gameTime"></param>
        /// <returns></returns>
        public virtual void PostDrawMenu(GameTime gameTime) {

        }
        /// <summary>
        /// 绘制模组菜单主页，在这个函数中需要注意线程安全<br/>
        /// 调用在 <see cref="DrawMenu"/> 之后，在 <see cref="PostDrawMenu"/> 之前<br/>
        /// 此钩子的位置是 MenuLoader.UpdateAndDrawModMenu 的开始，所以可以阻止模组菜单的绘制更新
        /// </summary>
        /// <param name="gameTime"></param>
        /// <param name="color"></param>
        /// <param name="logoRotation"></param>
        /// <param name="logoScale"></param>
        /// <returns></returns>
        public virtual bool? DrawModMenu(GameTime gameTime, Color color, float logoRotation, float logoScale) {
            return null;
        }
        /// <summary>
        /// 绘制模组菜单主页，在这个函数中需要注意线程安全<br/>
        /// 调用在 <see cref="DrawMenu"/> 之后，在 <see cref="PostDrawMenu"/> 之前<br/>
        /// 此钩子的位置是 MenuLoader.UpdateAndDrawModMenu 的末尾，可以用于在模组菜单绘制之后添加内容
        /// </summary>
        /// <param name="gameTime"></param>
        /// <param name="color"></param>
        /// <param name="logoRotation"></param>
        /// <param name="logoScale"></param>
        public virtual void PostDrawModMenu(GameTime gameTime, Color color, float logoRotation, float logoScale) {

        }
        /// <summary>
        /// 主菜单逻辑更新，以固定60tick频率调用，与帧率无关<br/>
        /// 由于主菜单没有独立的逻辑线程，此方法通过时间累积器在绘制线程中驱动，请注意线程安全<br/>
        /// 仅在 <see cref="Main.gameMenu"/> 为 <see langword="true"/> 时才会被调用
        /// </summary>
        public virtual void MenuLogicUpdate() {

        }
        /// <summary>
        /// 添加菜单按钮
        /// </summary>
        /// <param name="main"></param>
        /// <param name="selectedMenu"></param>
        /// <param name="buttonNames"></param>
        /// <param name="buttonScales"></param>
        /// <param name="offY"></param>
        /// <param name="spacing"></param>
        /// <param name="buttonIndex"></param>
        /// <param name="numButtons"></param>
        public virtual void AddMenuButtons(Main main, int selectedMenu, string[] buttonNames, float[] buttonScales, ref int offY, ref int spacing, ref int buttonIndex, ref int numButtons) {

        }
        /// <summary>
        /// 菜单界面 <see cref="Main.MenuUI"/> 切换到一个 UIState 之后调用，此时它已 Activate 并完成一次排版，
        /// 原版的世界创建、角色创建、世界选择等页面都会经过这里，按 <c>state is UIWorldCreation</c> 这类判断分支<br/>
        /// 同一个实例会被多次激活（例如从虚拟键盘返回世界创建页），<paramref name="firstActivation"/> 只在该实例第一次激活时为真，
        /// 往页面里追加元素只应在首次激活时做。所有覆盖调用完毕后框架会再排版一次
        /// </summary>
        /// <param name="state">刚激活的界面</param>
        /// <param name="firstActivation">是否为该实例的首次激活</param>
        public virtual void OnMenuStateActivated(UIState state, bool firstActivation) {

        }
        /// <summary>
        /// 菜单界面每次逻辑更新之后调用，只在 <see cref="Main.MenuUI"/> 持有界面时触发
        /// </summary>
        /// <param name="state">当前界面</param>
        public virtual void PostUpdateMenuState(UIState state) {

        }
        /// <summary>
        /// 菜单界面绘制之后调用，批次仍处于菜单 UI 的状态（UI 缩放矩阵），可以在页面之上叠画，
        /// 也可以在原版每帧重建手柄导航之后修补导航点
        /// </summary>
        /// <param name="state">当前界面</param>
        /// <param name="spriteBatch">菜单 UI 的批次</param>
        public virtual void PostDrawMenuState(UIState state, SpriteBatch spriteBatch) {

        }
    }
}
