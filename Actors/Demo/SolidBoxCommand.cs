using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// 调试指令：在鼠标处生成一个 <see cref="DemoSolidPlatform"/> 演示移动平台
    /// <para>用法：聊天框输入 <c>/solidbox</c>。仅作演示之用，可在正式使用中删除本文件</para>
    /// </summary>
    public sealed class SolidBoxCommand : ModCommand
    {
        /// <inheritdoc/>
        public override CommandType Type => CommandType.Chat;
        /// <inheritdoc/>
        public override string Command => "solidbox";
        /// <inheritdoc/>
        public override string Description => "在鼠标处生成一个可移动固体平台 (InnoVault SolidActor 演示)";

        /// <inheritdoc/>
        public override void Action(CommandCaller caller, string input, string[] args) {
            ActorLoader.NewActor<DemoSolidPlatform>(Main.MouseWorld);
        }
    }
}
