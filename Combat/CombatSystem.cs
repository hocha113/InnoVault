using Terraria.ModLoader;

namespace InnoVault.Combat
{
    //战斗契约的生命周期：换世界清空攻击体积与受击盒，卸载时连监听一起清
    internal sealed class CombatSystem : ModSystem
    {
        public override void OnWorldUnload() {
            AttackVolumes.Clear();
            NPCHurtbox.ClearAll();
        }

        public override void ClearWorld() {
            AttackVolumes.Clear();
            NPCHurtbox.ClearAll();
        }

        public override void Unload() {
            AttackVolumes.Clear();
            HitEvents.Clear();
        }
    }
}
