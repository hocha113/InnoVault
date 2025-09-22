using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using static InnoVault.TileProcessors.TileProcessorLoader;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 用于暂存被禁用的模组的TP实体的占位符实体
    /// </summary>
    public class UnknowTP : TileProcessor
    {
        /// <summary>
        /// 标识名，和使用 <see cref="UnknowTP"/> 的 <see cref="VaultType{T}.GetFullName(string, string)"/> 返回值相同
        /// </summary>
        public const string UnknowTag = "InnoVault/UnknowTP";
        /// <summary>
        /// 暂存的模组名
        /// </summary>
        public string UnModName { get; set; }
        /// <summary>
        /// 暂存的实体名
        /// </summary>
        public string UnTypeName { get; set; }
        /// <summary>
        /// 暂存的数据
        /// </summary>
        public TagCompound Data { get; set; } = [];
        /// <summary>
        /// 悬浮查看时应该显示的字符
        /// </summary>
        public string HoverString => GetFullName(UnModName, UnTypeName);
        /// <inheritdoc/>
        public override bool IsDaed() {
            //在多人游戏中，不允许客户端自行杀死Tp实体，这些要通过服务器的统一广播来管理
            if (VaultUtils.isClient) {
                return false;
            }

            if (Tile == default(Tile)) {
                return true;
            }

            if (!Tile.HasTile) {
                return true;
            }

            //删除关于目标物块ID的判定
            return false;
        }

        /// <summary>
        /// 在加载世界时调用
        /// </summary>
        public static TileProcessor Place(TagCompound data, string mod, string name, Point16 point) {
            TileProcessor newTP = NewTPInWorld(GetModuleID<UnknowTP>(), point, null);
            if (newTP is UnknowTP unknowTP) {
                unknowTP.UnModName = mod;
                unknowTP.UnTypeName = name;
                unknowTP.Data = data;
            }
            return newTP;
        }

        /// <summary>
        /// 获取这个占位符缓存的具体数据
        /// </summary>
        /// <returns></returns>
        public TagCompound GetData() {
            return new() {
                ["unMod"] = UnModName,
                ["unType"] = UnTypeName,
                ["mod"] = Mod.Name,
                ["name"] = Name,
                ["X"] = Position.X,
                ["Y"] = Position.Y,
                ["data"] = Data
            };
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            spriteBatch.Draw(VaultAsset.Unknow.Value, PosInWorld - Main.screenPosition, null
                , Lighting.GetColor(Position.ToPoint()), 0, Vector2.Zero, 1, SpriteEffects.None, 0);

        }

        /// <inheritdoc/>
        public override void FrontDraw(SpriteBatch spriteBatch) {
            if (!HoverTP) {
                return;
            }
            Vector2 drawPos = PosInWorld - Main.screenPosition;
            Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, HoverString,
            drawPos.X, drawPos.Y + 60, Color.AliceBlue, Color.Black, Vector2.Zero, 1f);
        }

        //发送一下卸载名，以避免客户端只能看到斜杠，至于Data就不用发送了，客户端不保存存档数据
        /// <inheritdoc/>
        public override void SendData(ModPacket data) {
            data.Write(UnModName);
            data.Write(UnTypeName);
        }
        /// <inheritdoc/>
        public override void ReceiveData(BinaryReader reader, int whoAmI) {
            UnModName = reader.ReadString();
            UnTypeName = reader.ReadString();
        }

        /// <inheritdoc/>
        public override void SaveData(TagCompound tag) { }
        /// <inheritdoc/>
        public override void LoadData(TagCompound tag) { }
        /// <inheritdoc/>
        public override string ToString() => HoverString + $"\nWhoAmI:{WhoAmI}";
    }
}
