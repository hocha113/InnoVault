using System.Collections.Generic;
using System.Text;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 单条 3D 模型导入诊断信息
    /// <br/>用于记录导入器遇到的非致命警告或错误
    /// </summary>
    public readonly struct Model3DDiagnosticEntry
    {
        /// <summary>
        /// 诊断级别
        /// <br/>决定最终日志输出通道和模型可用性判断
        /// </summary>
        public Model3DDiagnosticSeverity Severity { get; }
        /// <summary>
        /// 来源文件
        /// <br/>通常为模组内相对路径
        /// </summary>
        public string Source { get; }
        /// <summary>
        /// 来源行号
        /// <br/>二进制资源或未知位置使用 0
        /// </summary>
        public int Line { get; }
        /// <summary>
        /// 诊断消息
        /// <br/>面向开发者的导入问题描述
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 构造一条诊断信息
        /// <br/>null 来源和消息会被规范为空字符串
        /// </summary>
        /// <param name="severity">诊断级别</param>
        /// <param name="source">来源文件</param>
        /// <param name="line">来源行号</param>
        /// <param name="message">诊断消息</param>
        public Model3DDiagnosticEntry(Model3DDiagnosticSeverity severity, string source, int line, string message) {
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
    /// 3D 模型导入诊断级别
    /// <br/>错误级别通常表示导入结果不可完整信任
    /// </summary>
    public enum Model3DDiagnosticSeverity
    {
        /// <summary>
        /// 信息
        /// <br/>不影响模型使用
        /// </summary>
        Info,
        /// <summary>
        /// 警告
        /// <br/>部分内容被跳过或回退
        /// </summary>
        Warning,
        /// <summary>
        /// 错误
        /// <br/>模型可能无法正常使用
        /// </summary>
        Error,
    }

    /// <summary>
    /// 3D 模型导入诊断集合
    /// <br/>由导入器在加载期间持续写入，最终附着在模型资源上
    /// </summary>
    public class Model3DDiagnostic
    {
        private readonly List<Model3DDiagnosticEntry> _entries = new();

        /// <summary>
        /// 诊断条目
        /// <br/>按记录顺序保存
        /// </summary>
        public IReadOnlyList<Model3DDiagnosticEntry> Entries => _entries;
        /// <summary>
        /// 警告数量
        /// <br/>便于日志摘要和调试面板显示
        /// </summary>
        public int WarningCount { get; private set; }
        /// <summary>
        /// 错误数量
        /// <br/>便于日志摘要和失败判定
        /// </summary>
        public int ErrorCount { get; private set; }
        /// <summary>
        /// 是否存在错误
        /// <br/>加载器可据此决定是否回退到空模型
        /// </summary>
        public bool HasErrors => ErrorCount > 0;

        /// <summary>
        /// 添加信息
        /// <br/>用于记录不影响结果的导入细节
        /// </summary>
        /// <param name="source">来源文件</param>
        /// <param name="line">来源行号</param>
        /// <param name="message">诊断消息</param>
        public void Info(string source, int line, string message) => Add(Model3DDiagnosticSeverity.Info, source, line, message);
        /// <summary>
        /// 添加警告
        /// <br/>用于记录可回退或可跳过的问题
        /// </summary>
        /// <param name="source">来源文件</param>
        /// <param name="line">来源行号</param>
        /// <param name="message">诊断消息</param>
        public void Warn(string source, int line, string message) => Add(Model3DDiagnosticSeverity.Warning, source, line, message);
        /// <summary>
        /// 添加错误
        /// <br/>用于记录会破坏模型完整性的问题
        /// </summary>
        /// <param name="source">来源文件</param>
        /// <param name="line">来源行号</param>
        /// <param name="message">诊断消息</param>
        public void Error(string source, int line, string message) => Add(Model3DDiagnosticSeverity.Error, source, line, message);

        /// <summary>
        /// 添加诊断条目
        /// <br/>会同步更新警告和错误计数
        /// </summary>
        /// <param name="severity">诊断级别</param>
        /// <param name="source">来源文件</param>
        /// <param name="line">来源行号</param>
        /// <param name="message">诊断消息</param>
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

        /// <summary>
        /// 格式化诊断内容
        /// <br/>主要用于一次性写入日志
        /// </summary>
        /// <returns>多行诊断文本</returns>
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
