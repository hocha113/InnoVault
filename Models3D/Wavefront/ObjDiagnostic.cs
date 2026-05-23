using InnoVault.Models3D.Runtime;

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
    public sealed class ObjDiagnostic : Model3DDiagnostic
    {
    }
}
