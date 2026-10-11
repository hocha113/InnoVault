using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //镜头钳制：原版在所有模组的 ModifyScreenPosition 之后才钳制，模组钩子改不到最终结果，只能换掉这一个调用
    internal sealed class CameraClampTakeover : ScenarioTakeover
    {
        private static readonly MethodInfo _vanillaClamp = typeof(Main).GetMethod("ClampScreenPositionToWorld", BindingFlags.NonPublic | BindingFlags.Static);

        internal override string Name => "CameraClamp";

        internal override string Source => "Terraria/Main.cs DoDraw_UpdateCameraPosition: call ClampScreenPositionToWorld() (the only call in the method)";

        internal override bool ClientOnly => true;

        protected override void Install() {
            VaultCamera.BoundsOverride = ScenarioCameraPolicy.TryClamp;
            IL_Main.DoDraw_UpdateCameraPosition += Manipulate;
        }

        protected override void Uninstall() {
            IL_Main.DoDraw_UpdateCameraPosition -= Manipulate;
            VaultCamera.BoundsOverride = null;
        }

        private void Manipulate(ILContext il) {
            try {
                ILCursor cursor = new(il);
                if (_vanillaClamp == null || !cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(_vanillaClamp))) {
                    Fail("call to ClampScreenPositionToWorld not found", il);
                    return;
                }
                //栈：静态无参无返回值的调用换成同签名的入口，跳到原调用上的分支改落到新入口
                cursor.MoveAfterLabels();
                cursor.EmitDelegate(static () => VaultCamera.ClampScreen());
                cursor.Remove();
                Succeed();
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }

    //场景的镜头钳制策略，经 VaultCamera.BoundsOverride 接入。返回假表示按原版钳制。
    //钳制范围用一个矩形表示（可视范围必须留在里面的世界像素区域）；范围变化时在若干游戏帧内对矩形插值。
    //同一绘制帧里钳制会被调用两次（原版位置一次，镜头栈合成后一次），对矩形钳制是幂等的，不会叠加偏移
    internal static class ScenarioCameraPolicy
    {
        //与 Main.ClampScreenPositionToWorld 相同的边距：左上 656 px，右下 672 px
        private const float LeadMargin = 656f;
        private const float TrailMargin = 672f;
        //两次钳制之间玩家移动超过这么远（世界像素）视为传送，钳制范围直接切换，不做平移
        private const float TeleportDistance = 800f;

        private static VaultScenario _stage;
        private static bool _hasTarget;
        private static Vector4 _target;
        private static Vector4 _effective;
        private static Vector4 _blendFrom;
        private static int _blendFrame;
        private static int _blendLength;
        private static ScenarioCamera _lastLayer;
        private static Rectangle? _room;
        private static Vector2 _lastPlayerCenter;

        internal static bool TryClamp() {
            if (!ScenarioDirector.InSession || Main.gameMenu) {
                return false;
            }
            Player player = Main.LocalPlayer;
            ScenarioCamera layer = ScenarioRuntime.Camera.TopWhere(static c => c.Bounds.HasValue);
            ScenarioCameraBounds mode = layer?.Bounds ?? (ScenarioRuntime.InScenarioStage ? ScenarioCameraBounds.CenterWhenSmaller : ScenarioCameraBounds.Vanilla);
            Vector4 target = ResolveTarget(mode, layer, player);
            bool teleported = Vector2.DistanceSquared(player.Center, _lastPlayerCenter) > TeleportDistance * TeleportDistance;
            _lastPlayerCenter = player.Center;

            if (!ReferenceEquals(_stage, ScenarioRuntime.Stage)) {
                _stage = ScenarioRuntime.Stage;
                _hasTarget = false;
            }
            if (!_hasTarget) {
                _target = _effective = target;
                _hasTarget = true;
                _blendLength = 0;
            }
            else if (!Near(target, _target)) {
                int frames = (layer ?? _lastLayer)?.BoundsBlendFrames ?? ScenarioCamera.DefaultBoundsBlendFrames;
                if (frames > 0 && !teleported) {
                    _blendFrom = _effective;
                    _blendFrame = 0;
                    _blendLength = frames;
                }
                else {
                    _blendLength = 0;
                }
                _target = target;
            }
            _lastLayer = layer ?? _lastLayer;

            bool blending = _blendLength > 0 && _blendFrame < _blendLength;
            _effective = blending ? Vector4.Lerp(_blendFrom, _target, Ease(_blendFrame / (float)_blendLength)) : _target;
            if (!blending && mode == ScenarioCameraBounds.Vanilla) {
                return false;
            }
            if (!blending && mode == ScenarioCameraBounds.Unbounded) {
                return true;
            }
            ClampTo(_effective);
            return true;
        }

        //每个游戏帧推进过渡
        internal static void Tick() {
            if (_blendLength > 0 && _blendFrame < _blendLength) {
                _blendFrame++;
            }
        }

        internal static void Reset() {
            _stage = null;
            _hasTarget = false;
            _blendLength = 0;
            _blendFrame = 0;
            _lastLayer = null;
            _room = null;
        }

        private static Vector4 ResolveTarget(ScenarioCameraBounds mode, ScenarioCamera layer, Player player) {
            switch (mode) {
                case ScenarioCameraBounds.Area:
                    return layer?.ResolveArea() is Rectangle area ? TileRect(area) : WorldRect();
                case ScenarioCameraBounds.Rooms:
                    return ResolveRoom(layer, player) is Rectangle room ? TileRect(room) : WorldRect();
                case ScenarioCameraBounds.Unbounded:
                    Vector2 view = ViewSize();
                    return new Vector4(Main.leftWorld - view.X, Main.topWorld - view.Y, Main.rightWorld + view.X, Main.bottomWorld + view.Y);
                default:
                    return WorldRect();
            }
        }

        //玩家所在的房间；不在任何房间里时沿用上一间
        private static Rectangle? ResolveRoom(ScenarioCamera layer, Player player) {
            Point tile = player.Center.ToTileCoordinates();
            if (layer.Rooms is IReadOnlyList<Rectangle> { Count: > 0 } rooms) {
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Contains(tile)) {
                        _room = rooms[i];
                        return _room;
                    }
                }
                return _room ?? rooms[0];
            }
            Rectangle bounds = layer.ResolveArea() ?? new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY);
            if (layer.RoomSize is not Point size) {
                return bounds;
            }
            int column = (int)MathF.Floor((tile.X - bounds.X) / (float)size.X);
            int row = (int)MathF.Floor((tile.Y - bounds.Y) / (float)size.Y);
            Rectangle cell = new(bounds.X + column * size.X, bounds.Y + row * size.Y, size.X, size.Y);
            Rectangle clipped = Rectangle.Intersect(cell, bounds);
            if (clipped.Width > 0 && clipped.Height > 0) {
                _room = clipped;
            }
            return _room ?? bounds;
        }

        private static Vector4 WorldRect()
            => new(Main.leftWorld + LeadMargin, Main.topWorld + LeadMargin, Main.rightWorld - TrailMargin, Main.bottomWorld - TrailMargin);

        private static Vector4 TileRect(Rectangle tiles) => new(tiles.Left * 16f, tiles.Top * 16f, tiles.Right * 16f, tiles.Bottom * 16f);

        private static Vector2 ViewSize() => new(Main.screenWidth / Main.GameViewMatrix.Zoom.X, Main.screenHeight / Main.GameViewMatrix.Zoom.Y);

        private static bool Near(Vector4 a, Vector4 b)
            => MathF.Abs(a.X - b.X) < 0.5f && MathF.Abs(a.Y - b.Y) < 0.5f && MathF.Abs(a.Z - b.Z) < 0.5f && MathF.Abs(a.W - b.W) < 0.5f;

        private static float Ease(float t) {
            t = MathHelper.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        //与原版同式：可视范围按视图缩放折算并扣掉视图平移；某个方向上可视范围大于区域时居中
        private static void ClampTo(Vector4 rect) {
            Vector2 translation = Main.GameViewMatrix.Translation;
            Vector2 view = ViewSize();
            Vector2 min = new Vector2(rect.X, rect.Y) - translation;
            Vector2 max = new Vector2(rect.Z, rect.W) - view - translation;
            Vector2 position = Main.screenPosition;
            position.X = min.X > max.X ? (min.X + max.X) * 0.5f : MathHelper.Clamp(position.X, min.X, max.X);
            position.Y = min.Y > max.Y ? (min.Y + max.Y) * 0.5f : MathHelper.Clamp(position.Y, min.Y, max.Y);
            Main.screenPosition = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        }
    }

    //缩放范围与强制缩放：镜头栈里一层低优先级的来源，只改缩放，不改玩家设置
    internal sealed class ScenarioZoomSource : CameraSource
    {
        internal static ScenarioZoomSource Instance { get; private set; }

        public override int Priority => 100;

        public override bool Active => ScenarioDirector.InSession && ScenarioRuntime.Camera.TopWhere(static c => c.SetsZoom) != null;

        public override bool Evaluate(in CameraState below, out CameraState shot) {
            ScenarioLayers<ScenarioCamera> layers = ScenarioRuntime.Camera;
            float zoom = below.Zoom;
            if (layers.Pick(static c => c.Zoom) is float forced) {
                zoom = forced;
            }
            else {
                if (layers.Pick(static c => c.MinZoom) is float min) {
                    zoom = MathF.Max(zoom, MathF.Max(min, 1f));
                }
                if (layers.Pick(static c => c.MaxZoom) is float max) {
                    zoom = MathF.Min(zoom, max);
                }
            }
            shot = new CameraState(below.Center, zoom);
            return true;
        }

        internal static void Install() {
            if (Main.dedServ || Instance != null) {
                return;
            }
            Instance = new ScenarioZoomSource();
            VaultCamera.Register(Instance);
        }

        internal static void Uninstall() {
            if (Instance != null) {
                VaultCamera.Unregister(Instance);
            }
            Instance = null;
        }
    }

    internal sealed class ScenarioZoomSourceSystem : ModSystem
    {
        public override void PostSetupContent() => ScenarioZoomSource.Install();

        public override void Unload() => ScenarioZoomSource.Uninstall();
    }
}
