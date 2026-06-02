using InnoVault.VaultNetWork;
using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameContent
{
    /// <summary>
    /// 继承自<see cref="ModPlayer"/>类，用于适配网络客户端输入
    /// </summary>
    [Obsolete("TetheredPlayer 是旧的玩家输入同步实现，请改用 InnoVault.VaultNetWork.PlayerNetwork")]
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
        public sealed override void OnEnterWorld() {
            if (Main.dedServ || Main.myPlayer != Owner.whoAmI) {
                return;
            }

            DownLeft = Main.mouseLeft;
            DownRight = Main.mouseRight;
            InMousePos = Main.MouseWorld;
            ToMouse = Owner.Center.To(InMousePos);
            UnitToMouseV = ToMouse.UnitVector();
            ToMouseA = ToMouse.ToRotation();
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
            if (Main.dedServ) {
                return;
            }

            if (Main.myPlayer == Owner.whoAmI) {
                DownLeft = Main.mouseLeft;
                DownRight = Main.mouseRight;
                InMousePos = Main.MouseWorld;
                return;
            }

            if (PlayerNetwork.TryGetSnapshot(Owner, out PlayerNetworkSnapshot snapshot, maxAgeTicks: -1)) {
                if (snapshot.Has(PlayerNetworkDataFlags.MouseButtons)) {
                    DownLeft = snapshot.MouseLeft;
                    DownRight = snapshot.MouseRight;
                }

                if (snapshot.Has(PlayerNetworkDataFlags.MouseWorld)) {
                    InMousePos = snapshot.MouseWorld;
                }
                else if (snapshot.Has(PlayerNetworkDataFlags.MouseDirection)) {
                    InMousePos = Owner.Center + snapshot.MouseDirection * 500f;
                }
            }
        }

        /// <summary>
        /// 更新玩家数据，运行在玩家更新的靠后时机
        /// </summary>
        public virtual void TetheredUpdate() {

        }
    }
}
