using System;
using Warden.Logging.Formatters;

namespace Warden.Logging.Handlers
{
    public class LoggingHandlerConsole : LoggingHandlerBase
    {
        public LoggingHandlerConsole()
        {
        }

        public LoggingHandlerConsole(ILoggingFormatter formatter) : base(formatter)
        {
        }

        protected override void Write(string payload)
        {
            Console.WriteLine(payload);
        }
    }
}
