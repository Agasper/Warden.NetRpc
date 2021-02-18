using System;
using System.Threading;
using Warden.Logging;

namespace Warden.Networking
{
    public static class ContextSynchronizationHelper
    {
        public static void SynchronizeSafe(SynchronizationContext context, ContextSynchronizationMode mode, Action callback, ILogger logger)
        {
            try
            {
                if (mode == ContextSynchronizationMode.Send)
                    context.Send((s) => callback(), null);
                else
                    context.Post((s) => callback(), null);
            }
            catch (Exception ex)
            {
                logger.Error($"Unhandled exception on context synchronization: {ex}");
            }
        }
    }
}
