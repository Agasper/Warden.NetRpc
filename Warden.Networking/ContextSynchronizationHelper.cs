using System;
using System.Threading;
using Warden.Logging;

namespace Warden.Networking
{
    public static class ContextSynchronizationHelper
    {
        public static void SynchronizeSafe(SynchronizationContext context, Action callback, ILogger logger)
        {
            try
            {
                context.Send((s) => callback(), null);
            }
            catch (Exception ex)
            {
                logger.Error($"Unhandled exception on context synchronization: {ex}");
            }
        }
    }
}
