namespace Warden.Logging.Formatters
{
    public interface ILoggingFormatter
    {
        string Format(LogSeverity severity, string payload, LoggingMeta meta, ILogger logger);
    }
}