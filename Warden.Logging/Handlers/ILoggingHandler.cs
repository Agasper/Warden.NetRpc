namespace Warden.Logging.Handlers
{
    public interface ILoggingHandler
    {
        void Write(LogSeverity severity, string payload, LoggingMeta meta, ILogger logger);
    }
}