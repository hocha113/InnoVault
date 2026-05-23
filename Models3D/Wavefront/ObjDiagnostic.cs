using System.Collections.Generic;
using System.Text;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 单条 OBJ/MTL 加载诊断信息
    /// </summary>
    public readonly struct ObjDiagnosticEntry
    {
        /// <summary>
        /// 诊断级别
        /// </summary>
        public ObjDiagnosticSeverity Severity { get; }
        /// <summary>
        /// 来源文件相对路径
        /// </summary>
        public string Source { get; }
        /// <summary>
        /// 来源文件中的行号，未知时为 0
        /// </summary>
        public int Line { get; }
        /// <summary>
        /// 描述消息
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 构造一条诊断条目
        /// </summary>
        public ObjDiagnosticEntry(ObjDiagnosticSeverity severity, string source, int line, string message) {
            Severity = severity;
            Source = source ?? string.Empty;
            Line = line;
            Message = message ?? string.Empty;
        }

        /// <inheritdoc/>
        public override string ToString() {
            if (Line > 0) {
                return $"[{Severity}] {Source}:{Line} {Message}";
            }
            if (!string.IsNullOrEmpty(Source)) {
                return $"[{Severity}] {Source} {Message}";
            }
            return $"[{Severity}] {Message}";
        }
    }

    /// <summary>
    /// 诊断信息严重程度
    /// </summary>
    public enum ObjDiagnosticSeverity
    {
        /// <summary>
        /// 一般信息，不代表错误
        /// </summary>
        Info,
        /// <summary>
        /// 警告，部分内容被忽略但模型仍可使用
        /// </summary>
        Warning,
        /// <summary>
        /// 错误，模型可能不完整或加载失败
        /// </summary>
        Error,
    }

    /// <summary>
    /// OBJ/MTL 解析期间收集的诊断信息集合
    /// </summary>
    public sealed class ObjDiagnostic
    {
        private readonly List<ObjDiagnosticEntry> _entries = new();

        /// <summary>
        /// 全部已记录的诊断条目（只读）
        /// </summary>
        public IReadOnlyList<ObjDiagnosticEntry> Entries => _entries;

        /// <summary>
        /// 警告条目数量
        /// </summary>
        public int WarningCount { get; private set; }
        /// <summary>
        /// 错误条目数量
        /// </summary>
        public int ErrorCount { get; private set; }

        /// <summary>
        /// 是否包含至少一条错误
        /// </summary>
        public bool HasErrors => ErrorCount > 0;

        /// <summary>
        /// 添加一条信息级日志
        /// </summary>
        public void Info(string source, int line, string message) => Add(ObjDiagnosticSeverity.Info, source, line, message);
        /// <summary>
        /// 添加一条警告日志
        /// </summary>
        public void Warn(string source, int line, string message) => Add(ObjDiagnosticSeverity.Warning, source, line, message);
        /// <summary>
        /// 添加一条错误日志
        /// </summary>
        public void Error(string source, int line, string message) => Add(ObjDiagnosticSeverity.Error, source, line, message);

        /// <summary>
        /// 添加任意级别的诊断条目
        /// </summary>
        public void Add(ObjDiagnosticSeverity severity, string source, int line, string message) {
            _entries.Add(new ObjDiagnosticEntry(severity, source, line, message));
            switch (severity) {
                case ObjDiagnosticSeverity.Warning:
                    WarningCount++;
                    break;
                case ObjDiagnosticSeverity.Error:
                    ErrorCount++;
                    break;
            }
        }

        /// <summary>
        /// 将所有条目格式化为多行文本，便于一次性写入日志
        /// </summary>
        public string Format() {
            if (_entries.Count == 0) {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++) {
                sb.AppendLine(_entries[i].ToString());
            }
            return sb.ToString();
        }
    }
}
