using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Rigs2D
{
    //游戏宿主：把平台接缝接回 tModLoader / InnoVault 的原实现（与拆分前逐字同式）
    public static partial class Rig2DPlatform
    {
        static partial void InstallHost() {
            ErrorSink = VaultMod.LoggerError;
            InfoSink = message => VaultMod.Instance?.Logger.Info(message);
            TickSource = () => Main.GameUpdateCount;
            ServerCheck = () => Main.dedServ;
            MainThreadQueue = Main.QueueMainThreadAction;
            TileLight = world => Lighting.GetColor((int)(world.X / 16f), (int)(world.Y / 16f));
            TileCollide = (position, delta) => Collision.noSlopeCollision(position - new Vector2(3f), delta, 6, 6, true, true);
            GroundProbe = Rig2DGround.TileProbe;
            DebugPixel = () => VaultAsset.placeholder2?.Value;
            DebugText = (sb, text, pos, color, scale) => Utils.DrawBorderString(sb, text, pos, color, scale);
            Stepped = Rig2DSystem.NoteStepped;
        }
    }
}
