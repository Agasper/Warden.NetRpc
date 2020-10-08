using System;
using Warden.Logging.Formatters;

namespace Warden.Logging.Handlers
{
    public abstract class LoggingHandlerBase : ILoggingHandler
    {
        public ILoggingFormatter Formatter { get; set; } = new LoggingFormatterDefault();

        public LoggingHandlerBase()
        {
        }

        public LoggingHandlerBase(ILoggingFormatter formatter)
        {
            this.Formatter = formatter;
        }

        protected abstract void Write(string payload);

        public virtual void Write(LogSeverity severity, string payload, LoggingMeta meta, ILogger logger)
        {
            if (Formatter == null)
                throw new NullReferenceException($"Handler {this.GetType().Name} has no formatter");
            this.Write(Formatter.Format(severity, payload, meta, logger));
        }
    }
}
