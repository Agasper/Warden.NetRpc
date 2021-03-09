using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Warden.Util
{
    public static class TaskExtensions
    {
        public static async Task TimeoutAfter(this Task task, int millisecondsTimeout)
        {
            if (task.IsCompleted || (millisecondsTimeout == Timeout.Infinite))
            {
                await task.ConfigureAwait(false);
                return;
            }

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.CancelAfter(millisecondsTimeout);

                TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                _ = task.ContinueWith((t, state) =>
                {
                    MarshalTaskResults(t, state as TaskCompletionSource<object>);
                }, tcs, TaskContinuationOptions.ExecuteSynchronously);

                using (cts.Token.Register(() =>
                {
                    tcs.SetException(new TimeoutException("Operation timed out"));
                }))
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
        }

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout)
        {
            if (task.IsCompleted || (millisecondsTimeout == Timeout.Infinite))
                return await task.ConfigureAwait(false);

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.CancelAfter(millisecondsTimeout);

                TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
                
                _ = task.ContinueWith((t, state) =>
                {
                    MarshalTaskResults(t, state as TaskCompletionSource<TResult>);
                }, tcs, TaskContinuationOptions.ExecuteSynchronously);

                using (cts.Token.Register(() =>
                {
                    tcs.TrySetException(new TimeoutException("Operation timed out"));
                }))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
        }

        internal static void MarshalTaskResults<TResult>(
            Task source, TaskCompletionSource<TResult> proxy)
        {
            switch (source.Status)
            {
                case TaskStatus.Faulted:
                    if (source.Exception.InnerExceptions.Count == 1)
                        proxy.TrySetException(source.Exception.InnerExceptions.First());
                    else
                        proxy.TrySetException(source.Exception);
                    break;
                case TaskStatus.Canceled:
                    proxy.TrySetCanceled();
                    break;
                case TaskStatus.RanToCompletion:
                    Task<TResult> castedSource = source as Task<TResult>;
                    proxy.TrySetResult(
                        castedSource == null ? default(TResult) : // source is a Task
                            castedSource.Result); // source is a Task<TResult>
                    break;
                default:
                    throw new InvalidOperationException("Task has invalid status for marshaling: " + source.Status.ToString());
            }
        }
    }
}
