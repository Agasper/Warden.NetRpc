using System.Collections.Generic;
using Warden.Logging.Handlers;

namespace Warden.Logging
{
    public class Logger : ILogger
    {
        public LogSeverity Severity { get; set; }
        public string Name { get; private set; }
        public LoggingMeta Meta { get; private set; }
        public IList<ILoggingHandler> Handlers => handlers;

        readonly LogManager parent;
        readonly List<ILoggingHandler> handlers;

        LoggingMeta mergedMeta;
        int? mergedMetaHash;

        internal Logger(string name, LogManager parent)
        {
            this.Name = name;
            this.parent = parent;
            this.Severity = LogSeverity.TRACE;
            this.Meta = new LoggingMeta();
            this.handlers = new List<ILoggingHandler>();
        }

        public void Trace(string payload)
        {
            Write(LogSeverity.TRACE, payload);
        }
        public void Trace(string payload, params object[] format)
        {
            Write(LogSeverity.TRACE, payload, format);
        }
        public void Trace(string payload, LoggingMeta meta, params object[] format)
        {
            Write(LogSeverity.TRACE, payload, meta, format);
        }

        public void Debug(string payload)
        {
            Write(LogSeverity.DEBUG, payload);
        }
        public void Debug(string payload, params object[] format)
        {
            Write(LogSeverity.DEBUG, payload, format);
        }
        public void Debug(string payload, LoggingMeta meta, params object[] format)
        {
            Write(LogSeverity.DEBUG, payload, meta, format);
        }

        public void Info(string payload)
        {
            Write(LogSeverity.INFO, payload);
        }
        public void Info(string payload, params object[] format)
        {
            Write(LogSeverity.INFO, payload, format);
        }
        public void Info(string payload, LoggingMeta meta, params object[] format)
        {
            Write(LogSeverity.INFO, payload, meta, format);
        }

        public void Warn(string payload)
        {
            Write(LogSeverity.WARNING, payload);
        }
        public void Warn(string payload, params object[] format)
        {
            Write(LogSeverity.WARNING, payload, format);
        }
        public void Warn(string payload, LoggingMeta meta, params object[] format)
        {
            Write(LogSeverity.WARNING, payload, meta, format);
        }

        public void Error(string payload)
        {
            Write(LogSeverity.ERROR, payload);
        }
        public void Error(string payload, params object[] format)
        {
            Write(LogSeverity.ERROR, payload, format);
        }
        public void Error(string payload, LoggingMeta meta, params object[] format)
        {
            Write(LogSeverity.ERROR, payload, meta, format);
        }

        public void Critical(string payload)
        {
            Write(LogSeverity.CRITICAL, payload);
        }
        public void Critical(string payload, params object[] format)
        {
            Write(LogSeverity.CRITICAL, payload, format);
        }
        public void Critical(string payload, LoggingMeta meta, params object[] format)
        {
            Write(LogSeverity.CRITICAL, payload, meta, format);
        }

        public virtual bool IsLevelEnabled(LogSeverity level)
        {
            if (level < Severity)
                return false;
            if (level < parent.Severity)
                return false;
            return true;
        }

        public void Write(LogSeverity logSeverity, string payload, params object[] format)
        {
            Write(logSeverity, payload, LoggingMeta.Empty, format);
        }

        public void Write(LogSeverity level, string payload)
        {
            Write(level, payload, LoggingMeta.Empty);
        }

        public void Write(LogSeverity level, string payload, LoggingMeta meta, params object[] format)
        {
            if (format != null && format.Length > 0)
                Write(level, string.Format(payload, format), meta);
            else
                Write(level, payload, meta);
        }

        public virtual void Write(LogSeverity level, string payload, LoggingMeta meta)
        {
            if (!IsLevelEnabled(level))
                return;

            int currentMetaHash = parent.Meta.MetaHash ^ this.Meta.MetaHash;

            if (!mergedMetaHash.HasValue || currentMetaHash != mergedMetaHash.Value)
            {
                mergedMeta = parent.Meta.Merge(this.Meta);
                mergedMetaHash = currentMetaHash;
            }

            LoggingMeta localMergedMeta = mergedMeta;
            if (meta.Count > 0)
            {
                localMergedMeta = localMergedMeta.Merge(meta);
            }

            for(int i = 0; i < parent.Handlers.Count; i++)
                parent.Handlers[i].Write(level, payload, localMergedMeta, this);

            for(int i = 0; i < handlers.Count; i++)
                handlers[i].Write(level, payload, localMergedMeta, this);
        }
    }
}
