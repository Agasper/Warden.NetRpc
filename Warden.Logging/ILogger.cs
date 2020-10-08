using System.Collections.Generic;
using Warden.Logging.Handlers;

namespace Warden.Logging
{
    public interface ILogger
    {
        LogSeverity Severity { get; set; }
        string Name { get; }
        LoggingMeta Meta { get; }
        IList<ILoggingHandler> Handlers { get; }

        void Trace(string message);
        void Trace(string message, params object[] format);
        void Trace(string message, LoggingMeta meta, params object[] format);
        void Debug(string message);
        void Debug(string message, params object[] format);
        void Debug(string message, LoggingMeta meta, params object[] format);
        void Info(string message);
        void Info(string message, params object[] format);
        void Info(string message, LoggingMeta meta, params object[] format);
        void Warn(string message);
        void Warn(string message, params object[] format);
        void Warn(string message, LoggingMeta meta, params object[] format);
        void Error(string message);
        void Error(string message, params object[] format);
        void Error(string message, LoggingMeta meta, params object[] format);
        void Critical(string message);
        void Critical(string message, params object[] format);
        void Critical(string message, LoggingMeta meta, params object[] format);

        void Write(LogSeverity severity, string message);
        void Write(LogSeverity level, string payload, LoggingMeta meta, params object[] format);

        bool IsLevelEnabled(LogSeverity level);
    }
}
