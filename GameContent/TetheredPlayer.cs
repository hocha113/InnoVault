using Microsoft.Xna.Framework;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameContent
{
    /// <summary>
    /// 继承自<see cref="ModPlayer"/>类，用于适配网络客户端输入
    /// </summary>
    public abstract class TetheredPlayer : ModPlayer, ITetheredPlayer
    {
        /// <inheritdoc/>
        public Player Owner => Player;
        /// <inheritdoc/>
        public Item Item => Player.GetItem();
        /// <inheritdoc/>
        public bool DownLeft { get; set; }
        /// <inheritdoc/>
        public bool DownRight { get; set; }
        /// <inheritdoc/>
        public Vector2 ToMouse { get; set; }
        /// <inheritdoc/>
        public Vector2 InMousePos { get; set; }
        /// <inheritdoc/>
        public Vector2 UnitToMouseV { get; set; }
        /// <inheritdoc/>
        public float ToMouseA { get; set; }

        /// <inheritdoc/>
        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            if (type == MessageType.TetheredPlayer) {
                bool left = reader.ReadBoolean();
                bool right = reader.ReadBoolean();
                Vector2 mousePos = reader.ReadVector2();
                if (VaultUtils.isClient) {
                    TetheredPlayer player = Main.player[whoAmI].GetModPlayer<TetheredPlayer>();
                    player.DownLeft = left;
                    player.DownRight = right;
                    player.InMousePos = mousePos;
                }
                else {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TetheredPlayer);
                    modPacket.Write(left);
                    modPacket.Write(right);
                    modPacket.WriteVector2(mousePos);
                    modPacket.Send(-1, whoAmI);
                }
            }
            else if (type == MessageType.TetheredPlayer_DownLeft) {
                bool left = reader.ReadBoolean();
                if (VaultUtils.isClient) {
                    Player player = Main.player[whoAmI];
                    player.GetModPlayer<TetheredPlayer>().DownLeft = left;
                }
                else {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TetheredPlayer_DownLeft);
                    modPacket.Write(left);
                    modPacket.Send(-1, whoAmI);
                }
            }
            else if (type == MessageType.TetheredPlayer_DownRight) {
                bool right = reader.ReadBoolean();
                if (VaultUtils.isClient) {
                    Player player = Main.player[whoAmI];
                    player.GetModPlayer<TetheredPlayer>().DownRight = right;
                }
                else {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TetheredPlayer_DownRight);
                    modPacket.Write(right);
                    modPacket.Send(-1, whoAmI);
                }
            }
            else if (type == MessageType.TetheredPlayer_InMousePos) {
                Vector2 mousePos = reader.ReadVector2();
                if (VaultUtils.isClient) {
                    Player player = Main.player[whoAmI];
                    player.GetModPlayer<TetheredPlayer>().InMousePos = mousePos;
                }
                else {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TetheredPlayer_InMousePos);
                    modPacket.WriteVector2(mousePos);
                    modPacket.Send(-1, whoAmI);
                }
            }
        }

        /// <inheritdoc/>
        public sealed override void OnEnterWorld() {
            if (VaultUtils.isSinglePlayer || Main.myPlayer != Owner.whoAmI) {
                return;
            }

            DownLeft = Main.mouseLeft;
            DownRight = Main.mouseRight;
            InMousePos = Main.MouseWorld;
            ToMouse = Owner.Center.To(InMousePos);
            UnitToMouseV = ToMouse.UnitVector();
            ToMouseA = ToMouse.ToRotation();
            ModPacket modPacket = Mod.GetPacket();
            modPacket.Write((byte)MessageType.TetheredPlayer);
            modPacket.Write(DownLeft);
            modPacket.Write(DownRight);
            modPacket.WriteVector2(InMousePos);
            modPacket.Send();
        }

        /// <inheritdoc/>
        public sealed override void PostUpdate() {
            UpdateNet();
            ToMouse = Owner.Center.To(InMousePos);
            UnitToMouseV = ToMouse.UnitVector();
            ToMouseA = ToMouse.ToRotation();
            TetheredUpdate();
        }

        private void UpdateNet() {
            if (VaultUtils.isSinglePlayer || Main.myPlayer != Owner.whoAmI) {
                return;
            }

            // 同步 DownLeft
            if (DownLeft != Main.mouseLeft) {
                DownLeft = Main.mouseLeft;
                ModPacket modPacket = Mod.GetPacket();
                modPacket.Write((byte)MessageType.TetheredPlayer_DownLeft);
                modPacket.Write(DownLeft);
                modPacket.Send();
            }

            // 同步 DownRight
            if (DownRight != Main.mouseRight) {
                DownRight = Main.mouseRight;
                ModPacket modPacket = Mod.GetPacket();
                modPacket.Write((byte)MessageType.TetheredPlayer_DownRight);
                modPacket.Write(DownRight);
                modPacket.Send();
            }

            // 同步 InMousePos
            if (InMousePos != Main.MouseWorld) {
                InMousePos = Main.MouseWorld;
                ModPacket modPacket = Mod.GetPacket();
                modPacket.Write((byte)MessageType.TetheredPlayer_InMousePos);
                modPacket.WriteVector2(InMousePos);
                modPacket.Send();
            }
        }

        /// <summary>
        /// 更新玩家数据，运行在玩家更新的靠后时机
        /// </summary>
        public virtual void TetheredUpdate() {

        }
    }
}
