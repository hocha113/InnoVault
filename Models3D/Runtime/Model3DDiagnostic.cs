using System.Collections.Generic;
using System.Text;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 单条 3D 模型导入诊断信息
    /// </summary>
    public readonly struct Model3DDiagnosticEntry
    {
        public Model3DDiagnosticSeverity Severity { get; }
        public string Source { get; }
        public int Line { get; }
        public string Message { get; }

        public Model3DDiagnosticEntry(Model3DDiagnosticSeverity severity, string source, int line, string message) {
            Severity = severity;
            Source = source ?? string.Empty;
            Line = line;
            Message = message ?? string.Empty;
        }

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
    /// 3D 模型导入诊断级别
    /// </summary>
    public enum Model3DDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// 3D 模型导入诊断集合
    /// </summary>
    public class Model3DDiagnostic
    {
        private readonly List<Model3DDiagnosticEntry> _entries = new();

        public IReadOnlyList<Model3DDiagnosticEntry> Entries => _entries;
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }
        public bool HasErrors => ErrorCount > 0;

        public void Info(string source, int line, string message) => Add(Model3DDiagnosticSeverity.Info, source, line, message);
        public void Warn(string source, int line, string message) => Add(Model3DDiagnosticSeverity.Warning, source, line, message);
        public void Error(string source, int line, string message) => Add(Model3DDiagnosticSeverity.Error, source, line, message);

        public void Add(Model3DDiagnosticSeverity severity, string source, int line, string message) {
            _entries.Add(new Model3DDiagnosticEntry(severity, source, line, message));
            switch (severity) {
                case Model3DDiagnosticSeverity.Warning:
                    WarningCount++;
                    break;
                case Model3DDiagnosticSeverity.Error:
                    ErrorCount++;
                    break;
            }
        }

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
#pragma warning restore CS1591
