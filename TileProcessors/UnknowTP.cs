using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
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
        public string UnModName { get; set; } = "unknown";
        /// <summary>
        /// 暂存的实体名
        /// </summary>
        public string UnTypeName { get; set; } = "unknown";
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
        /// 在加载世界时调用，用于创建一个新的未知TP实体
        /// </summary>
        public static TileProcessor Place(Point16 point, TagCompound data, string mod, string name) {
            //尝试在世界中创建一个新的UnknowTP实例
            TileProcessor newTP = NewTPInWorld(GetModuleID<UnknowTP>(), point, null);

            //如果创建成功，则初始化其数据
            if (newTP is UnknowTP unknowTP) {
                unknowTP.UnModName = mod;
                unknowTP.UnTypeName = name;
                unknowTP.Data = data;
            }
            return newTP;
        }

        /// <summary>
        /// 获取这个占位符缓存的具体数据，主要用于世界存档的保存
        /// </summary>
        public TagCompound GetData() {
            return new TagCompound {
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
            //绘制问号贴图，表示这是一个未知的TP实体
            spriteBatch.Draw(VaultAsset.Unknow.Value, PosInWorld - Main.screenPosition, null
                , Lighting.GetColor(Position.ToPoint()), 0, Vector2.Zero, 1, SpriteEffects.None, 0);
        }

        /// <inheritdoc/>
        public override void FrontDraw(SpriteBatch spriteBatch) {
            if (!HoverTP) {
                return;
            }
            //当鼠标悬停时，显示原始的模组名和实体名
            Vector2 drawPos = PosInWorld - Main.screenPosition;
            Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, HoverString,
            drawPos.X, drawPos.Y + 60, Color.AliceBlue, Color.Black, Vector2.Zero, 1f);
        }

        /// <inheritdoc/>
        public override void SendData(ModPacket data) {
            //发送卸载的模组名和实体名，以便客户端显示
            data.Write(UnModName);
            data.Write(UnTypeName);
        }

        /// <inheritdoc/>
        public override void ReceiveData(BinaryReader reader, int whoAmI) {
            //接收卸载的模组名和实体名
            UnModName = reader.ReadString();
            UnTypeName = reader.ReadString();
        }

        /// <inheritdoc/>
        public override void SaveData(TagCompound tag) {
            //虽然世界存档使用GetData手动保存，但为了兼容其他可能的序列化需求（如复制粘贴），这里也实现标准的保存逻辑
            tag["unMod"] = UnModName;
            tag["unType"] = UnTypeName;
            tag["data"] = Data;
        }

        /// <inheritdoc/>
        public override void LoadData(TagCompound tag) {
            //加载标准的保存数据
            if (tag.TryGet("unMod", out string unModName)) {
                UnModName = unModName;
            }
            if (tag.TryGet("unType", out string unTypeName)) {
                UnTypeName = unTypeName;
            }
            if (tag.TryGet("data", out TagCompound data)) {
                Data = data;
            }
        }

        /// <summary>
        /// 检查并归档已卸载模组的TP数据，将其转换为占位符格式
        /// <br>建议在加载世界数据时调用此方法，以确保所有失效的TP数据都能被正确转换为占位符</br>
        /// </summary>
        public static void CheckAndArchive(IList<TagCompound> dataList) {
            for (int i = 0; i < dataList.Count; i++) {
                TagCompound tag = dataList[i];
                if (!tag.TryGet("mod", out string modName) || !tag.TryGet("name", out string typeName)) {
                    continue;
                }

                //如果已经是 UnknowTP，则跳过
                if (modName == "InnoVault" && typeName == "UnknowTP") {
                    continue;
                }

                //构建全名
                string fullName = GetFullName(modName, typeName);

                //如果该 TP 类型未被加载（模组被卸载或类被删除）
                if (!TP_FullName_To_ID.ContainsKey(fullName)) {
                    //创建新的占位符数据
                    TagCompound newTag = new() {
                        ["mod"] = "InnoVault",
                        ["name"] = "UnknowTP",
                        ["unMod"] = modName,
                        ["unType"] = typeName,
                        ["X"] = tag.GetShort("X"),
                        ["Y"] = tag.GetShort("Y"),
                        ["data"] = tag.GetCompound("data")
                    };
                    //替换原有数据
                    dataList[i] = newTag;
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString() => HoverString + $"\nWhoAmI:{WhoAmI}";
    }
}
