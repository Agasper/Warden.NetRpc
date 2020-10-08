using System;
using System.Collections.Generic;
using Warden.Logging.Handlers;

namespace Warden.Logging
{
    public class LogManager : ILogManager
    {
        public static LogManager Dummy => logManagerDummy.Value;
        public static LogManager Default => logManagerDefault;

        static Lazy<LogManager> logManagerDummy;
        static LogManager logManagerDefault;

        static LogManager()
        {
            logManagerDefault = new LogManager();
            logManagerDummy = new Lazy<LogManager>(() => {
                return new LogManager();
            }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public static void SetDefault(LogManager logManager)
        {
            if (logManager == null)
                throw new ArgumentNullException(nameof(logManager));
            logManagerDefault = logManager;
        }

        public LogSeverity Severity { get; set; }
        public LoggingMeta Meta { get; set; }
        public IList<ILoggingHandler> Handlers => handlers;

        List<ILoggingHandler> handlers;

        public LogManager(LogSeverity severity, LoggingMeta meta, params ILoggingHandler[] handlers) : this(severity, meta)
        {
            this.handlers.AddRange(handlers);
        }

        public LogManager(LogSeverity severity, LoggingMeta meta) : this(severity)
        {
            this.Meta = this.Meta.Merge(meta);
        }

        public LogManager(LogSeverity severity, params ILoggingHandler[] handlers) : this(severity)
        {
            this.handlers.AddRange(handlers);
        }

        public LogManager(LogSeverity severity) : this()
        {
            this.Severity = severity;
        }

        public LogManager()
        {
            this.Severity = LogSeverity.TRACE;
            this.Meta = new LoggingMeta();
            this.handlers = new List<ILoggingHandler>();
        }

        public virtual bool IsLevelEnabled(LogSeverity level)
        {
            if (level < this.Severity)
                return false;
            return true;
        }

        ILogger ILogManager.GetLogger(string name)
        {
            return GetLogger(name);
        }

        public virtual Logger GetLogger(string name)
        {
            var logger = new Logger(name, this);
            return logger;
        }
    }
}
