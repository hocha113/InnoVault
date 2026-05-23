using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.Models3D.Examples
{
#if DEBUG
    /// <summary>
    /// 仅 DEBUG 构建启用的 3D 渲染验证物品
    /// <br/>左键持有时在玩家附近实时绘制一个旋转的立方体，用于验证 OBJ/MTL/贴图加载与渲染管线
    /// <br/>同时演示了三类高级 API：<see cref="Model3DInstance.PreDrawGroup"/> 委托钩子、
    /// <see cref="Model3DInstance.RenderStateOverride"/> 状态覆盖、<see cref="Model3DRenderer.OnLayerRendered"/> RT 截获
    /// </summary>
    internal sealed class Model3DTestItem : ModItem
    {
        /// <summary>
        /// 通过 <see cref="VaultLoadenAttribute"/> 自动加载示例 cube 模型
        /// </summary>
        [VaultLoaden("Assets/Models3D/cube")]
        public static Vault3DModel CubeModel { get; set; }

        /// <summary>
        /// 通过统一 3D 模型加载器加载 glTF 示例模型
        /// </summary>
        [VaultLoaden("Assets/Models3D/SunFace/scene")]
        public static Vault3DModel SunFaceModel { get; set; }

        public override string Texture => "InnoVault/icon";

        //========== 示例 2：OnLayerRendered 后处理订阅（一次性挂上） ==========
        //OnLayerRendered 是全局静态事件，在每层 3D 模型画完后、合成回屏之前触发
        //这里订阅 AfterPlayers 层，把 RT 以 Additive blend 叠回一遍，制造一圈外发光halo
        //订阅会在 Model3DSystem.UnLoadData 时被自动清理（ClearAllSubscriptions）
        private static bool _glowHookSubscribed = false;
        private static void EnsureGlowHookSubscribed() {
            if (_glowHookSubscribed) {
                return;
            }
            _glowHookSubscribed = true;
            Model3DRenderer.OnLayerRendered += OnAfterPlayersRendered;
        }

        private static void OnAfterPlayersRendered(Model3DLayer layer, RenderTarget2D rt) {
            //只在 AfterPlayers 层生效；其它层保持默认合成
            if (layer != Model3DLayer.AfterPlayers || rt == null || rt.IsDisposed) {
                return;
            }
            //订阅触发时 GraphicsDevice 已切回屏幕 RT、SpriteBatch 非 Active
            //在默认合成之前 additive 一遍即可形成外发光，默认合成随后会画上"正常的 cube"
            SpriteBatch sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp
                , DepthStencilState.None, RasterizerState.CullCounterClockwise);
            sb.Draw(rt, Vector2.Zero, Color.White * 0.35f);
            sb.End();
        }

        //========== 示例 1：PreDrawGroup 委托动态修改 BasicEffect 的 emissive ==========
        //不需要写 .fx 文件就能玩转默认 BasicEffect：在 PreDrawGroup 中把 emissive 改成时间相关的颜色
        //这样既可以快速验证 callback 链路，也是绝大多数"轻量特效"的常见做法
        private static void PulseEmissive(in Model3DDrawContext ctx, BasicEffect basic) {
            if (basic == null) {
                return;
            }
            float t = ctx.Time * 0.05f;
            //三相位 sin 形成一个不停循环的彩虹emissive
            Vector3 color = new Vector3(
                0.5f + 0.5f * (float)System.Math.Sin(t),
                0.5f + 0.5f * (float)System.Math.Sin(t + MathHelper.TwoPi / 3f),
                0.5f + 0.5f * (float)System.Math.Sin(t + 2f * MathHelper.TwoPi / 3f));
            //EmissiveColor 不参与光照，无论 LightingEnabled 是否打开都能看到
            basic.EmissiveColor = color;
        }

        public override void SetDefaults() {
            Item.width = 32;
            Item.height = 32;
            Item.useAnimation = Item.useTime = 20;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.useTurn = false;
            Item.value = 1;
            Item.rare = ItemRarityID.Cyan;
            Item.UseSound = SoundID.Item1;
        }

        public override void HoldItem(Player player) {
            if (Main.dedServ || CubeModel == null || !CubeModel.IsValid) {
                return;
            }
            if (player.whoAmI != Main.myPlayer) {
                return;
            }

            float t = (float)Main.timeForVisualEffects * 0.02f;

            if (SunFaceModel != null && SunFaceModel.IsValid) {
                Model3DRenderer.Submit(new Model3DInstance(SunFaceModel) {
                    Position = player.Center + new Vector2(-80f, -220f),
                    Rotation = new Vector3(0f, t * 0.35f, 0f),
                    Scale = new Vector3(6f),
                    Tint = Color.White,
                    LightingEnabled = true,
                    Layer = Model3DLayer.AfterPlayers,
                    DepthEnabled = true,
                    CullBackface = false,
                });
            }
        }

        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor
            , ref float rotation, ref float scale, int whoAmI) {
            return true;
        }
    }

    /// <summary>
    /// 原子 API 使用示例：演示开发者如何完全绕开 Submit / 桶分类，
    /// 在自己的 <see cref="RenderHandles.RenderHandle"/> / 自定义钩子中手写一遍 3D 绘制
    /// <br/>用法示例（伪代码）：
    /// <code>
    /// // 在某个 RenderHandle 的 DrawAfterPlayers 钩子中，SpriteBatch 已非 Active
    /// public override void DrawAfterPlayers(SpriteBatch sb, GraphicsDevice gd, RenderTarget2D screenSwap) {
    ///     // 1) 把模型完整画一遍，使用解析后的 effect/状态链路
    ///     var instance = new Model3DInstance(myModel) { Position = ..., Rotation = ... };
    ///     Model3DRenderer.DrawInstance(gd, instance
    ///         , Model3DRenderer.BuildScreenViewMatrix()
    ///         , Model3DRenderer.BuildScreenProjection()
    ///         , Model3DLayer.AfterPlayers, isTransparent: false);
    ///
    ///     // 2) 或者完全自己拼，用任意 Effect 画任意一个 group
    ///     Matrix world = Model3DRenderer.BuildWorldMatrix(instance);
    ///     myShader.Parameters["u_t"].SetValue((float)Main.timeForVisualEffects);
    ///     foreach (var group in myModel.Groups) {
    ///         Model3DRenderer.DrawMeshGroup(gd, group, myShader, world
    ///             , Model3DRenderer.BuildScreenViewMatrix()
    ///             , Model3DRenderer.BuildScreenProjection());
    ///     }
    /// }
    /// </code>
    /// </summary>
    internal static class Model3DAtomicExample
    {
        //此类纯文档示意，方法体保留为可调用的最小实现，避免被裁掉
        internal static void DrawWithCustomShader(GraphicsDevice gd, Model3DInstance instance, Effect customEffect) {
            if (gd == null || instance == null || instance.Model == null || customEffect == null) {
                return;
            }
            Matrix world = Model3DRenderer.BuildWorldMatrix(instance);
            Matrix view = Model3DRenderer.BuildScreenViewMatrix();
            Matrix projection = Model3DRenderer.BuildScreenProjection();

            for (int g = 0; g < instance.Model.Groups.Count; g++) {
                Model3DMeshGroup group = instance.Model.Groups[g];
                Model3DRenderer.DrawMeshGroup(gd, group, customEffect, world, view, projection);
            }
        }
    }
#endif
}
