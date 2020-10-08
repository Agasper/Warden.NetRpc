using System;
using System.Text;
using System.Threading;

namespace Warden.Logging.Formatters
{
    public class LoggingFormatterDefault : ILoggingFormatter, IDisposable
    {
        public bool IncludeLoggerNameInMessage { get; set; } = true;
        public bool IncludeTimestampInMessage { get; set; } = true;
        public bool IncludeSeverityInMessage { get; set; } = true;

        ThreadLocal<StringBuilder> stringBuilders;

        public LoggingFormatterDefault()
        {
            stringBuilders = new ThreadLocal<StringBuilder>(() => new StringBuilder());
        }

        public void Dispose()
        {
            stringBuilders.Dispose();
        }

        protected StringBuilder GetStringBuilder()
        {
            return stringBuilders.Value;
        }

        public virtual string Format(LogSeverity severity, string payload, LoggingMeta meta, ILogger logger)
        {
            var stringBuilder = GetStringBuilder();
            stringBuilder.Clear();
            if (IncludeTimestampInMessage)
                stringBuilder.AppendFormat("[{0}] ", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            if (IncludeLoggerNameInMessage)
                stringBuilder.AppendFormat("[{0}] ", logger.Name);
            if (IncludeSeverityInMessage)
                stringBuilder.Append($"[{severity}] ");
            stringBuilder.Append(payload);
            return stringBuilder.ToString();

        }
    }
}
