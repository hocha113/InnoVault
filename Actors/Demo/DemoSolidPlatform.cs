using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;
using Terraria;

namespace InnoVault.Actors
{
    /// <summary>
    /// 演示用的可移动固体平台：水平来回巡逻，可被站立、四面阻挡、被钩爪勾住
    /// <para>生成方式：<c>ActorLoader.NewActor&lt;DemoSolidPlatform&gt;(worldPos)</c>，或在聊天框输入调试指令 <c>/solidbox</c></para>
    /// <para>仅作演示与手感验证之用，可在正式使用中删除本文件</para>
    /// </summary>
    public sealed class DemoSolidPlatform : SolidActor
    {
        private float originX;
        private int dir = 1;
        private const float PatrolRange = 160f;
        private const float MoveSpeed = 1.5f;

        /// <inheritdoc/>
        public override void OnSpawn(params object[] args) {
            Width = 96;
            Height = 24;
            originX = Position.X;
            OneWay = false;
            DrawLayer = ActorDrawLayer.AfterTiles;
        }

        /// <inheritdoc/>
        public override void SolidAI() {
            Velocity.X = dir * MoveSpeed;
            Velocity.Y = 0f;

            if (Position.X > originX + PatrolRange) {
                dir = -1;
            }
            else if (Position.X < originX - PatrolRange) {
                dir = 1;
            }
        }

        /// <inheritdoc/>
        public override void SendExtraData(BinaryWriter writer) {
            //把不属于 [SyncVar] 的内部巡逻状态纳入生成 / 晚加入快照，避免晚加入者用当前位置反推出错误的巡逻原点
            writer.Write(originX);
            writer.Write(dir);
        }

        /// <inheritdoc/>
        public override void ReceiveExtraData(BinaryReader reader) {
            originX = reader.ReadSingle();
            dir = reader.ReadInt32();
        }

        /// <inheritdoc/>
        public override bool PreDraw(SpriteBatch spriteBatch, ref Color drawColor) {
            Rectangle dest = new(
                (int)(Position.X - Main.screenPosition.X),
                (int)(Position.Y - Main.screenPosition.Y),
                Width, Height);
            spriteBatch.Draw(VaultAsset.placeholder2.Value, dest, Color.SandyBrown);
            return false;
        }
    }
}
