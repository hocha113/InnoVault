using Microsoft.Xna.Framework;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.VaultNetworks
{
    internal static class PlayerNetworkPacketIO
    {
        internal static PlayerNetworkDataFlags NormalizeFlags(PlayerNetworkDataFlags flags) {
            flags &= PlayerNetworkDataFlags.All;
            return flags == PlayerNetworkDataFlags.None ? PlayerNetworkDataFlags.BasicAim : flags;
        }

        internal static void WriteFlags(BinaryWriter writer, PlayerNetworkDataFlags flags)
            => writer.Write((byte)(flags & PlayerNetworkDataFlags.All));

        internal static PlayerNetworkDataFlags ReadFlags(BinaryReader reader)
            => (PlayerNetworkDataFlags)reader.ReadByte() & PlayerNetworkDataFlags.All;

        internal static void WriteSnapshot(BinaryWriter writer, PlayerNetworkSnapshot snapshot, bool writePlayerIndex) {
            if (writePlayerIndex) {
                writer.Write((byte)snapshot.PlayerIndex);
            }

            PlayerNetworkDataFlags flags = snapshot.Flags & PlayerNetworkDataFlags.All;
            WriteFlags(writer, flags);

            if ((flags & PlayerNetworkDataFlags.MouseDirection) != 0) {
                writer.WriteVector2(snapshot.MouseDirection);
            }

            if ((flags & PlayerNetworkDataFlags.MouseWorld) != 0) {
                writer.WriteVector2(snapshot.MouseWorld);
            }

            if ((flags & PlayerNetworkDataFlags.MouseButtons) != 0) {
                BitsByte buttons = new BitsByte();
                buttons[0] = snapshot.MouseLeft;
                buttons[1] = snapshot.MouseRight;
                writer.Write((byte)buttons);
            }
        }

        internal static PlayerNetworkSnapshot ReadSnapshot(BinaryReader reader, int playerIndex, long updateTick) {
            PlayerNetworkDataFlags flags = ReadFlags(reader);
            Vector2 mouseDirection = Vector2.Zero;
            Vector2 mouseWorld = Vector2.Zero;
            bool mouseLeft = false;
            bool mouseRight = false;

            if ((flags & PlayerNetworkDataFlags.MouseDirection) != 0) {
                mouseDirection = reader.ReadVector2();
                float lengthSq = mouseDirection.LengthSquared();
                if (!IsFinite(mouseDirection) || lengthSq < 0.9f || lengthSq > 1.1f) {
                    flags &= ~PlayerNetworkDataFlags.MouseDirection;
                    mouseDirection = Vector2.Zero;
                }
            }

            if ((flags & PlayerNetworkDataFlags.MouseWorld) != 0) {
                mouseWorld = reader.ReadVector2();
                if (!IsFinite(mouseWorld)) {
                    flags &= ~PlayerNetworkDataFlags.MouseWorld;
                    mouseWorld = Vector2.Zero;
                }
            }

            if ((flags & PlayerNetworkDataFlags.MouseButtons) != 0) {
                BitsByte buttons = reader.ReadByte();
                mouseLeft = buttons[0];
                mouseRight = buttons[1];
            }

            return new PlayerNetworkSnapshot(playerIndex, flags, mouseWorld, mouseDirection
                , mouseLeft, mouseRight, updateTick, false);
        }

        private static bool IsFinite(Vector2 value)
            => IsFinite(value.X) && IsFinite(value.Y);

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
