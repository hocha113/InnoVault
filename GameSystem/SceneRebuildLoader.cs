using System.Collections.Generic;
using System.Reflection;
using Terraria;
using static InnoVault.GameSystem.SceneOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于场景重制节点的钩子均挂载于此处
    /// </summary>
    public class SceneRebuildLoader : IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_UpdateAudio_Dlelgate(Main main);
        public static List<IUpdateAudio> UpdateAudios { get; internal set; } = [];
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
        void IVaultLoader.LoadData() {
            On_Main.UpdateAudio_DecideOnTOWMusic += DecideOnTOWMusicEvent;
            On_Main.UpdateAudio_DecideOnNewMusic += DecideOnNewMusicEvent;
            UpdateAudios = VaultUtils.GetSubclassInstances<IUpdateAudio>();
            VaultHook.Add(typeof(Main).GetMethod("UpdateAudio", BindingFlags.Instance | BindingFlags.NonPublic), OnUpdateAudioHook);
        }

        void IVaultLoader.UnLoadData() {
            On_Main.UpdateAudio_DecideOnTOWMusic -= DecideOnTOWMusicEvent;
            On_Main.UpdateAudio_DecideOnNewMusic -= DecideOnNewMusicEvent;
            UpdateAudios.Clear();
        }

        private void DecideOnTOWMusicEvent(On_Main.orig_UpdateAudio_DecideOnTOWMusic orig, Main self) {
            orig.Invoke(self);
            ToMusicFunc();
        }

        private void DecideOnNewMusicEvent(On_Main.orig_UpdateAudio_DecideOnNewMusic orig, Main self) {
            orig.Invoke(self);
            ToMusicFunc();
        }

        private static void ToMusicFunc() {
            foreach (var audio in UpdateAudios) {
                audio.DecideMusic();
            }
            foreach (var inds in Instances) {
                if (!inds.CanOverride()) {
                    continue;
                }
                inds.DecideMusic();
            }
        }

        private static void OnUpdateAudioHook(On_UpdateAudio_Dlelgate orig, Main main) {
            orig.Invoke(main);
            foreach (var audio in UpdateAudios) {
                audio.PostUpdateAudio();
            }
            foreach (var inds in Instances) {
                if (!inds.CanOverride()) {
                    continue;
                }
                inds.PostUpdateAudio();
            }
        }
    }
}
