using InnoVault.Actors;
using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault
{
#if DEBUG
    internal class TestItem : ModItem
    {
        public override string Texture => "InnoVault/icon";

        [VaultLoaden("Assets/Models3D/cube")]
        public static Vault3DModel CubeModel { get; set; }

        [VaultLoaden("Assets/Models3D/Sun")]
        public static Vault3DModel SunModel { get; set; }

        /// <summary>
        /// 通过统一 3D 模型加载器加载 glTF 示例模型
        /// </summary>
        [VaultLoaden("Assets/Models3D/SunFace/scene")]
        public static Vault3DModel SunFaceModel { get; set; }

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

        public override bool IsLoadingEnabled(Mod mod) {
            return true;
        }

        public override void SetDefaults() {
            Item.width = 80;
            Item.height = 80;
            Item.damage = 9999;
            Item.DamageType = DamageClass.Default;
            Item.useAnimation = Item.useTime = 13;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 2.25f;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.shootSpeed = 8f;
            Item.shoot = ProjectileID.PurificationPowder;
            Item.value = 7;
            Item.rare = ItemRarityID.Yellow;
        }

        public override void UpdateInventory(Player player) {

        }

        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI) {
            return false;
        }

        public override bool AltFunctionUse(Player player) {
            return true;
        }

        public override void HoldItem(Player player) {
            if (Main.dedServ || SunModel == null || !SunModel.IsValid) {
                return;
            }
            if (player.whoAmI != Main.myPlayer) {
                return;
            }

            //float t = (float)Main.timeForVisualEffects * 0.02f;

            ////主太阳：光标作为主光源
            //Vector2 sunPos1 = player.Center + new Vector2(0f, -220f);
            //Model3DRenderer.Submit(new Model3DInstance(SunModel) {
            //    Position = sunPos1,
            //    Rotation = new Vector3(t * 0.7f, t, t * 0.3f),
            //    Scale = Vector3.One * 60,
            //    Tint = Color.White,
            //    Layer = Model3DLayer.AfterPlayers,
            //    DepthEnabled = true,
            //    CullBackface = false,
            //    LightingEnabled = true,
            //    LightingOverride = BuildCursorLighting(sunPos1, Main.MouseWorld),
            //});

            ////第二个小球放在玩家右侧，验证多实例下各自独立计算光向
            //Vector2 sunPos2 = player.Center + new Vector2(220f, 0f);
            //Model3DRenderer.Submit(new Model3DInstance(SunModel) {
            //    Position = sunPos2,
            //    Rotation = new Vector3(0f, t * 1.3f, 0f),
            //    Scale = new Vector3(0.5f) * 60,
            //    Tint = Color.LightSkyBlue,
            //    Layer = Model3DLayer.BeforePlayers,
            //    DepthEnabled = true,
            //    CullBackface = false,
            //    LightingEnabled = true,
            //    LightingOverride = BuildCursorLighting(sunPos2, Main.MouseWorld),
            //});

            //另一种写法：订阅 Model3DRenderer.ResolveLighting，对所有实例统一施加规则
            //例如：Model3DRenderer.ResolveLighting += (inst, cfg) => { cfg.Light0.Direction = ...; };

            if (Main.dedServ || CubeModel == null || !CubeModel.IsValid) {
                return;
            }
            if (player.whoAmI != Main.myPlayer) {
                return;
            }

            //首次持物时一次性挂上 OnLayerRendered，演示后处理钩子
            EnsureGlowHookSubscribed();

            float t = (float)Main.timeForVisualEffects * 0.02f;

            //基本 cube：使用默认 BasicEffect 路径
            Model3DRenderer.Submit(new Model3DInstance(CubeModel) {
                Position = player.Center + new Vector2(0f, -120f),
                Rotation = new Vector3(t * 0.7f, t, t * 0.3f),
                Scale = Vector3.One,
                Tint = Color.White,
                Layer = Model3DLayer.AfterPlayers,
                DepthEnabled = true,
                CullBackface = false,
            });

            //第二个 cube：层级靠后，用于验证遮挡
            Model3DRenderer.Submit(new Model3DInstance(CubeModel) {
                Position = player.Center + new Vector2(120f, 0f),
                Rotation = new Vector3(0f, t * 1.3f, 0f),
                Scale = new Vector3(0.5f),
                Tint = Color.LightSkyBlue,
                Layer = Model3DLayer.BeforePlayers,
                DepthEnabled = true,
                CullBackface = false,
            });

            //示例 1：通过 PreDrawGroup 委托动态修改 BasicEffect 的 emissive 颜色
            //展示"零 .fx 文件"也能做的最轻量特效注入手法
            Model3DRenderer.Submit(new Model3DInstance(CubeModel) {
                Position = player.Center + new Vector2(-120f, 0f),
                Rotation = new Vector3(t * 0.4f, t * 0.6f, 0f),
                Scale = new Vector3(0.6f),
                Tint = Color.White,
                LightingEnabled = true,
                Layer = Model3DLayer.AfterPlayers,
                DepthEnabled = true,
                CullBackface = false,
                PreDrawGroup = static (in Model3DDrawContext ctx) => {
                    //通过全局静态 DefaultEffect 拿到渲染器持有的 BasicEffect，并改写 emissive
                    //ConfigureBasicEffectFor 在本 group 绘制时刚刚写完默认 emissive，本回调紧随其后覆盖
                    if (Model3DRenderer.DefaultEffect != null) {
                        PulseEmissive(in ctx, Model3DRenderer.DefaultEffect);
                    }
                },
            });

            //示例：使用 RenderStateOverride 强制 Additive blend
            //该实例会被自动归入透明桶（IsInstanceTransparent 检测到非 Opaque 的 Blend 覆盖）
            Model3DInstance additiveCube = new Model3DInstance(CubeModel) {
                Position = player.Center + new Vector2(0f, 60f),
                Rotation = new Vector3(t * 0.8f, t * 0.5f, t * 0.2f),
                Scale = new Vector3(0.4f),
                Tint = new Color(255, 180, 80, 200),
                Layer = Model3DLayer.AfterPlayers,
                DepthEnabled = true,
                CullBackface = false,
                RenderStateOverride = new Model3DRenderState {
                    Blend = BlendState.Additive,
                },
            };
            Model3DRenderer.Submit(additiveCube);

            Vector2 sunFacePos = player.Center + new Vector2(0f, -420f);
            if (SunFaceModel != null && SunFaceModel.IsValid) {
                Model3DRenderer.Submit(new Model3DInstance(SunFaceModel) {
                    Position = sunFacePos,
                    Rotation = new Vector3(0f, t * 0.35f, 0f),
                    Scale = new Vector3(10f),
                    Tint = Color.White,
                    LightingEnabled = true,
                    LightingOverride = BuildCursorLighting(sunFacePos, Main.MouseWorld),
                    Layer = Model3DLayer.AfterPlayers,
                    DepthEnabled = true,
                    CullBackface = false,
                });
            }
        }

        //把光标当作"灯泡"，让主光从光标方向射向模型中心
        //zBias 给一个负值让光略偏向相机方向，避免完全侧光导致正面太暗
        private static Model3DLightingConfig BuildCursorLighting(Vector2 modelWorldPos, Vector2 cursorWorldPos) {
            const float zBias = -100f;
            Vector2 toModel = modelWorldPos - cursorWorldPos;
            Vector3 raw = new Vector3(toModel.X, toModel.Y, zBias);
            Vector3 dir = raw.LengthSquared() < 1e-4f ? -Vector3.UnitZ : Vector3.Normalize(raw);

            Model3DLightingConfig cfg = Model3DLightingConfig.CreateDefault();
            cfg.Light0.Enabled = true;
            cfg.Light0.Direction = dir;
            cfg.Light0.DiffuseColor = new Vector3(1.4f, 1.2f, 0.95f);
            cfg.Light0.SpecularColor = new Vector3(1.0f, 0.9f, 0.7f);
            //关闭补光和背光，强调"光标即唯一光源"
            cfg.Light1.Enabled = false;
            cfg.Light2.Enabled = false;
            //保留少量环境光，避免暗面纯黑
            cfg.AmbientColor = new Vector3(0.12f);
            cfg.SpecularPower = 24f;
            return cfg;
        }

        public override bool? UseItem(Player player) {
            if (player.whoAmI != Main.myPlayer) {
                return true;
            }

            ////左键：进入石头维度
            //if (player.altFunctionUse != 2) {
            //  if (!DimensionLoader.AnyActive()) {
            //      //当前在主世界，进入石头维度
            //      if (DimensionLoader.Enter<StoneDimension>()) {
            //          VaultUtils.Text("正在进入石头维度...", Color.Cyan);
            //      }
            //      else {
            //          VaultUtils.Text("无法进入石头维度！", Color.Red);
            //      }
            //  }
            //  else {
            //      VaultUtils.Text("你已经在维度中了！右键退出", Color.Yellow);
            //  }
            //}
            ////右键：退出维度
            //else {
            //  if (DimensionLoader.AnyActive()) {
            //      DimensionLoader.Exit();
            //      VaultUtils.Text("正在退出维度...", Color.Cyan);
            //  }
            //  else {
            //      VaultUtils.Text("你已经在主世界了！", Color.Yellow);
            //  }
            //}

            return true;
        }
    }

    internal class TestProj : ModProjectile
    {
        public override string Texture => "InnoVault/icon";
        public override void SetDefaults() {
            Projectile.width = Projectile.height = 32;
            Projectile.friendly = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 60;
        }

        public override void AI() {
            if (Projectile.ai[0] == 0 && !VaultUtils.isClient) {
                ActorLoader.NewActor<TestActor>(Projectile.Center);
            }
            Projectile.ai[0]++;
        }
    }

    internal class TestActor : Actor
    {
        [SyncVar]
        public int NumValue;
        public override void OnSpawn(params object[] args) {
            Width = 100;
            Height = 100;
            NetUpdate = true;
        }
        public override void AI() {
            if (HitBox.Intersects(Main.MouseWorld.GetRectangle(1))
                && Main.mouseLeft) {
                NumValue++;
                NetUpdate = true;
            }
        }
        public override bool PreDraw(SpriteBatch spriteBatch, ref Color drawColor) {
            Main.spriteBatch.Draw(TextureAssets.Projectile[ModContent.ProjectileType<TestProj>()].Value
                , Center - Main.screenPosition, null, Color.Red, 0, Velocity, 1f, SpriteEffects.None, 0);
            Utils.DrawBorderString(spriteBatch, $"NumValue: {NumValue}", Center - Main.screenPosition + new Vector2(0, 40), Color.White);
            return false;
        }
    }
#endif
}

