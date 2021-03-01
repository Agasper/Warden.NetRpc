using System.Collections.Generic;
using Warden.Logging.Handlers;

namespace Warden.Logging
{
    public interface ILogManager
    {
        LogSeverity Severity { get; set; }
        LoggingMeta Meta { get; set; }
        IList<ILoggingHandler> Handlers { get; }

        bool IsLevelEnabled(LogSeverity level);
        ILogger GetLogger(string name);
    }
}
