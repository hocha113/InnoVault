using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Services;
using System;
using System.Collections.Generic;

namespace InnoVault.Narrative.History
{
    /// <summary>
    /// 对话历史的统一门面，也是<b>通用唤起接口</b>。<br/>
    /// 读取走 <see cref="NarrativeServices.History"/>；唤起转发给当前注册的 <see cref="INarrativeBacklogView"/>；<br/>
    /// 持久化转发给框架内置的 <see cref="NarrativeHistorySave"/>（按角色、客户端本地）。<br/>
    /// 任何消费者都可以用键位 / HUD 图标 / 命令调用 <see cref="Toggle"/> 自定义唤起形式
    /// </summary>
    public static class NarrativeHistory
    {
        private static readonly IReadOnlyList<NarrativeLogEntry> _empty = Array.Empty<NarrativeLogEntry>();
        private static INarrativeBacklogView _view;

        /// <summary>
        /// 是否启用框架内置的 <see cref="NarrativeHistorySave"/> 持久化（按角色、客户端本地）。<br/>
        /// 自带持久化方案的消费者可设为 <see langword="false"/> 并替换 <see cref="NarrativeServices.History"/>
        /// </summary>
        public static bool UseBuiltinPersistence { get; set; } = true;

        /// <summary>按时间顺序读取全部历史记录（最旧在前）</summary>
        public static IReadOnlyList<NarrativeLogEntry> Entries => NarrativeServices.History?.GetEntries() ?? _empty;

        /// <summary>当前记录条数</summary>
        public static int Count => NarrativeServices.History?.Count ?? 0;

        /// <summary>当前注册的 backlog 视图是否处于打开状态（供对话视图在其打开时屏蔽点击推进）</summary>
        public static bool IsOpen => _view?.IsOpen ?? false;

        /// <summary>注册当前生效的 backlog 视图（默认视图与消费者自定义视图择一）</summary>
        public static void Register(INarrativeBacklogView view) {
            if (view != null) {
                _view = view;
            }
        }

        /// <summary>注销 backlog 视图（仅当传入的正是当前视图时生效）</summary>
        public static void Unregister(INarrativeBacklogView view) {
            if (ReferenceEquals(_view, view)) {
                _view = null;
            }
        }

        /// <summary>打开 backlog（无注册视图时为空操作）</summary>
        public static void Open() => _view?.Open();

        /// <summary>关闭 backlog</summary>
        public static void Close() => _view?.Close();

        /// <summary>切换 backlog 开 / 关</summary>
        public static void Toggle() => _view?.Toggle();

        /// <summary>把当前内存历史落盘到当前角色的存档文件</summary>
        public static void Save() {
            if (!UseBuiltinPersistence) {
                return;
            }
            try {
                NarrativeHistorySave.Persist();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative history save threw: {ex}");
            }
        }

        /// <summary>从当前角色的存档文件读回历史（会先清空当前内存内容）</summary>
        public static void Load() {
            if (!UseBuiltinPersistence) {
                return;
            }
            try {
                NarrativeServices.History?.Clear();
                NarrativeHistorySave.Restore();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative history load threw: {ex}");
            }
        }

        /// <summary>复位门面状态（卸载时调用）</summary>
        internal static void Reset() {
            _view = null;
            UseBuiltinPersistence = true;
        }
    }
}
