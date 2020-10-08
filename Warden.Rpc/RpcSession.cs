using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.IO;
using Warden.Rpc.Payload;
using Warden.Util;

namespace Warden.Rpc
{
    public abstract class RpcSession : IRpcSession
    {
        public object Tag { get; set; }

        readonly ConcurrentDictionary<uint, RemotingRequest> requests;
        readonly IRpcConnection connection;
        readonly RpcSerializer rpcSerializer;
        readonly bool orderedExecution;
        readonly int defaultExecutionTimeout;
        readonly int orderedExecutionMaxQueue;
        readonly object orderedExecutionTaskMutex = new object();
        readonly object lastRequestIdMutex = new object();
        readonly ILogger logger;
        readonly CancellationTokenSource cancellationTokenSource;
        readonly TaskScheduler taskScheduler;

        Task orderedExecutionTask;
        uint lastRequestId;
        volatile int executionQueueSize;
        volatile bool closed;
        object requestsMutex = new object();


        public RpcSession(RpcConfiguration configuration)
        {
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            if (configuration.Connection == null)
                throw new ArgumentNullException(nameof(configuration.Connection));
            if (configuration.TaskScheduler == null)
                throw new ArgumentNullException(nameof(configuration.TaskScheduler));
            this.cancellationTokenSource = new CancellationTokenSource();
            this.requests = new ConcurrentDictionary<uint, RemotingRequest>();
            this.rpcSerializer = configuration.Serializer;
            this.orderedExecution = configuration.OrderedExecution;
            this.orderedExecutionTask = Task.CompletedTask;
            this.taskScheduler = configuration.TaskScheduler;
            this.orderedExecutionMaxQueue = configuration.OrderedExecutionMaxQueue;
            this.connection = configuration.Connection;
            this.defaultExecutionTimeout = configuration.DefaultExecutionTimeout;
            this.logger = configuration.LogManager.GetLogger(nameof(RpcSession));
            this.logger.Meta["kind"] = this.GetType().Name;
        }

        public virtual bool Close(Exception exception = null)
        {
            if (closed)
                return false;
            closed = true;

            this.cancellationTokenSource.Dispose();

            lock (requestsMutex)
            {
                foreach (var pair in requests)
                {
                    pair.Value.SetError(exception ?? new RemotingException("Connection was closed"));
                }
                requests.Clear();
            }

            if (exception == null)
                this.logger.Debug($"{this} closed!");
            else
                this.logger.Debug($"{this} closed with exception: {exception}");

            return true;
        }

        CancellationToken GetCancellationToken()
        {
            var cts = cancellationTokenSource;
            return cts == null ? default : cts.Token;
        }

        protected void CheckClosed()
        {
            if (closed)
                throw new ObjectDisposedException(nameof(RpcSession));
        }

        public void OnMessage(IReader reader)
        {
            CheckClosed();
            MessageType messageType = (MessageType)reader.ReadByte();
            ReadFormatterInfo readFormatterInfo = new ReadFormatterInfo(reader, rpcSerializer);
            switch (messageType)
            {
                case MessageType.RpcRequest:
                    RemotingRequest remotingRequest = new RemotingRequest();
                    remotingRequest.MergeFrom(readFormatterInfo);
                    LogMessageReceived(remotingRequest);
                    ExecuteRequest(remotingRequest);
                    break;
                case MessageType.RpcResponse:
                    RemotingResponse remotingResponse = new RemotingResponse();
                    remotingResponse.MergeFrom(readFormatterInfo);
                    LogMessageReceived(remotingResponse);
                    ExecuteResponse(remotingResponse);
                    break;
                case MessageType.RpcResponseError:
                    RemotingResponseError remotingResponseError = new RemotingResponseError();
                    remotingResponseError.MergeFrom(readFormatterInfo);
                    LogMessageReceived(remotingResponseError);
                    ExecuteResponseError(remotingResponseError);
                    break;
                default:
                    throw new ArgumentException($"Wrong message type: {messageType}");
            }
        }

        void LogMessageReceived(ICustomMessage message)
        {
            this.logger.Trace($"{this} received {message}");
        }

        void SendMessage(ICustomMessage message)
        {
            this.logger.Trace($"{this} sending {message}");
            this.connection.SendMessage(message);
        }

        void SendMessage(ICustomMessage message, SendingOptions sendingOptions)
        {
            this.logger.Trace($"{this} sending {message} with options {sendingOptions}");
            this.connection.SendMessage(message, sendingOptions);
        }

        public override string ToString()
        {
            return $"{nameof(RpcSession)}[connection={connection},tag={Tag}]";
        }

        void ExecuteResponseError(RemotingResponseError remotingResponseError)
        {
            bool exists = requests.TryRemove(remotingResponseError.RequestId, out RemotingRequest remotingRequest);
            if (exists)
                remotingRequest.SetError(remotingResponseError.Exception);

            logger.Error($"Remote execution exception on method {remotingResponseError.MethodKey}: {remotingResponseError.Exception}");

            ProcessRemoteExecutionException(remotingResponseError.MethodKey, remotingResponseError.Exception);
        }

        void ExecuteResponse(RemotingResponse remotingResponse)
        {
            bool exists = requests.TryRemove(remotingResponse.RequestId, out RemotingRequest remotingRequest);
            if (!exists)
            {
                logger.Warn($"{this} got response for unknown request id {remotingResponse.RequestId}");
                return;
            }

            remotingRequest.SetResult(remotingResponse);
        }

        void ExecuteRequest(RemotingRequest request)
        {
            if (orderedExecution && executionQueueSize >= orderedExecutionMaxQueue)
            {
                _ = ProcessLocalExecutionExceptionInternal(request, new RemotingException("Execution queue exceed it's limits"));
                return;
            }

            if (!orderedExecution)
            {
                Interlocked.Increment(ref executionQueueSize);
                _ = ExecuteRequestOuterAsync(request);
            }
            else
            {
                lock (orderedExecutionTaskMutex)
                {
                    Interlocked.Increment(ref executionQueueSize);
                    orderedExecutionTask = orderedExecutionTask.ContinueWith((t, o)
                        => ExecuteRequestOuterAsync(o as RemotingRequest), request,
                            GetCancellationToken(),
                            TaskContinuationOptions.None,
                            taskScheduler).Unwrap();
                }
            }
        }

        async Task ExecuteRequestOuterAsync(RemotingRequest request)
        {
            ExecutionRequest executionRequest = ExecutionRequest.FromRemotingMessage(request);
            try
            {
                logger.Debug($"Executing {request} locally");

                Stopwatch sw = new Stopwatch();
                sw.Start();
                var result = await ExecuteRequestAsync(executionRequest).ConfigureAwait(false);
                sw.Stop();



                double timeSeconds = sw.ElapsedTicks / (double)Stopwatch.Frequency;
                ulong timeSpanTicks = (ulong)(timeSeconds * TimeSpan.TicksPerSecond);
                float ms = timeSpanTicks / (float)TimeSpan.TicksPerMillisecond;
                logger.Debug($"Executed {request} locally in {ms.ToString("0.00")}ms");

                RemotingResponse response = new RemotingResponse();
                if (request.ExpectResponse)
                {
                    response.HasArgument = result.HasResult;
                    response.Argument = result.Result;
                    response.RequestId = request.RequestId;
                    response.ExecutionTime = timeSpanTicks;

                    SendMessage(response);
                }
            }
            catch (Exception ex)
            {
                Exception innermost = ex.GetInnermostException();
                RemotingException remotingException = new RemotingException(innermost);
                await ProcessLocalExecutionExceptionInternal(request, remotingException).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref executionQueueSize);
            }
        }

        void ProcessRemoteExecutionException(object methodIdentity, RemotingException exception)
        {
            try
            {
                RemoteExecutionExceptionArgs args = new RemoteExecutionExceptionArgs(exception,
                    methodIdentity);
                OnRemoteExecutionException(args);

                if (args.CloseConnection)
                    connection.Close();
            }
            catch (Exception ex)
            {
                logger.Error($"{this} got an unhandled exception in OnRemoteExecutionException(): {ex}");
            }
        }

        async Task ProcessLocalExecutionExceptionInternal(RemotingRequest remotingRequest, RemotingException exception)
        {
            logger.Error($"{this} got an unhandled exception on method execution {remotingRequest.MethodKey}: {exception}");

            RemotingResponseError remotingResponseError = new RemotingResponseError(
                    remotingRequest.RequestId,
                    remotingRequest.MethodKey,
                    exception);

            SendMessage(remotingResponseError);

            try
            {
                LocalExecutionExceptionArgs args = new LocalExecutionExceptionArgs(exception,
                    ExecutionRequest.FromRemotingMessage(remotingRequest));
                OnLocalExecutionException(args);

                if (args.CloseConnection)
                {
                    await connection.FlushSendQueueAndCloseAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"{this} got an unhandled exception in OnLocalExecutionException(): {ex}");
            }
        }

        protected virtual void OnLocalExecutionException(LocalExecutionExceptionArgs args)
        {

        }

        protected virtual void OnRemoteExecutionException(RemoteExecutionExceptionArgs args)
        {

        }

        protected abstract Task<ExecutionResult> ExecuteRequestAsync(ExecutionRequest request);

        RemotingRequest GetRequest(object methodIdentity, bool expectResponse)
        {
            if (!(methodIdentity is int || methodIdentity is string))
                throw new ArgumentException($"{nameof(methodIdentity)} should be int or string");

            lock (requestsMutex)
            {
                CheckClosed();
                RemotingRequest request = new RemotingRequest();
                request.MethodKey = methodIdentity;
                request.HasArgument = false;
                request.Argument = null;
                request.ExpectResponse = expectResponse;

                uint newId = 0;
                lock (lastRequestIdMutex)
                    newId = lastRequestId++;
                request.RequestId = newId;

                if (expectResponse)
                {
                    if (!requests.TryAdd(request.RequestId, request))
                        throw new InvalidOperationException("Could not create a new request");
                    request.CreateAwaiter();
                }
                return request;
            }
        }

        protected virtual Task ExecutionWrapper(Task executionTask)
        {
            return executionTask;
        }

        async Task SendAndWait(RemotingRequest request, ExecutionOptions options)
        {
            try
            {
                this.logger.Debug($"Executing {request} remotely with {options}");
                OnRemoteExecutionStarted(request, options);
                SendMessage(request);
                int timeout = defaultExecutionTimeout;
                if (options.Timeout > Timeout.Infinite)
                    timeout = options.Timeout;
                await ExecutionWrapper(request.WaitAsync(timeout)).ConfigureAwait(false);
                float ms = request.Response.ExecutionTime / (float)TimeSpan.TicksPerMillisecond;
                OnRemoteExecutionCompleted(request, request.Response, options, ms);
                this.logger.Debug($"Executed {request} remotely in {ms.ToString("0.00")}ms");
            }
            catch (Exception ex)
            {
                Exception inner = ex.GetInnermostException();
                RemotingException remotingException = inner as RemotingException;
                if (remotingException == null)
                    remotingException = new RemotingException(inner);
                this.logger.Error($"Executed {request} remotely with exception ({inner.GetType().Name}).");
                ProcessRemoteExecutionException(request.MethodKey, remotingException);
                throw remotingException;
            }
        }

        protected virtual void OnRemoteExecutionStarted(RemotingRequest remotingRequest, ExecutionOptions options)
        {

        }
        protected virtual void OnRemoteExecutionCompleted(RemotingRequest remotingRequest, RemotingResponse response, ExecutionOptions options, float elapsedMilliseconds)
        {

        }
        protected virtual void OnSendingCompleted(RemotingRequest remotingRequest, SendingOptions options)
        {

        }

        public virtual Task ExecuteAsync(int methodIdentity) => ExecuteAsync_(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(string methodIdentity) => ExecuteAsync_(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(int methodIdentity, ExecutionOptions options) => ExecuteAsync_(methodIdentity, options);
        public virtual Task ExecuteAsync(string methodIdentity, ExecutionOptions options) => ExecuteAsync_(methodIdentity, options);

        Task ExecuteAsync_(object methodIdentity, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true);
            request.HasArgument = false;
            return SendAndWait(request, options);
        }

        public virtual Task ExecuteAsync<A>(int methodIdentity, A arg) => ExecuteAsync_(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task ExecuteAsync<A>(string methodIdentity, A arg) => ExecuteAsync_(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task ExecuteAsync<A>(int methodIdentity, A arg, ExecutionOptions options) => ExecuteAsync_(methodIdentity, arg, options);
        public virtual Task ExecuteAsync<A>(string methodIdentity, A arg, ExecutionOptions options) => ExecuteAsync_(methodIdentity, arg, options);

        Task ExecuteAsync_<A>(object methodIdentity, A arg, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true);
            request.HasArgument = true;
            request.Argument = arg;
            return SendAndWait(request, options);
        }

        public virtual Task<R> ExecuteAsync<R>(int methodIdentity) => ExecuteAsync_<R>(methodIdentity, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R>(string methodIdentity) => ExecuteAsync_<R>(methodIdentity, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R>(int methodIdentity, ExecutionOptions options) => ExecuteAsync_<R>(methodIdentity, options);
        public virtual Task<R> ExecuteAsync<R>(string methodIdentity, ExecutionOptions options) => ExecuteAsync_<R>(methodIdentity, options);

        async Task<R> ExecuteAsync_<R>(object methodIdentity, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true);
            request.HasArgument = false;
            await SendAndWait(request, options).ConfigureAwait(false);
            return (R)request.Result;
        }

        public virtual Task<R> ExecuteAsync<R, A>(int methodIdentity, A arg) => ExecuteAsync_<R, A>(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R, A>(string methodIdentity, A arg) => ExecuteAsync_<R, A>(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R, A>(int methodIdentity, A arg, ExecutionOptions options) => ExecuteAsync_<R, A>(methodIdentity, arg, options);
        public virtual Task<R> ExecuteAsync<R, A>(string methodIdentity, A arg, ExecutionOptions options) => ExecuteAsync_<R, A>(methodIdentity, arg, options);

        async Task<R> ExecuteAsync_<R, A>(object methodIdentity, A arg, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true);
            request.HasArgument = true;
            request.Argument = arg;
            await SendAndWait(request, options).ConfigureAwait(false);
            return (R)request.Result;
        }

        public virtual void Send(int methodIdentity) => Send_(methodIdentity, SendingOptions.Default);
        public virtual void Send(string methodIdentity) => Send_(methodIdentity, SendingOptions.Default);
        public virtual void Send(int methodIdentity, SendingOptions sendingOptions) => Send_(methodIdentity, sendingOptions);
        public virtual void Send(string methodIdentity, SendingOptions sendingOptions) => Send_(methodIdentity, sendingOptions);

        void Send_(object methodIdentity, SendingOptions sendingOptions)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, false);
            request.HasArgument = false;
            this.logger.Debug($"Sending {request}");
            SendMessage(request, sendingOptions);
            OnSendingCompleted(request, sendingOptions);
        }

        public virtual void Send<T>(int methodIdentity, T arg) => Send_(methodIdentity, arg, SendingOptions.Default);
        public virtual void Send<T>(string methodIdentity, T arg) => Send_(methodIdentity, arg, SendingOptions.Default);
        public virtual void Send<T>(int methodIdentity, T arg, SendingOptions sendingOptions) => Send_(methodIdentity, arg, sendingOptions);
        public virtual void Send<T>(string methodIdentity, T arg, SendingOptions sendingOptions) => Send_(methodIdentity, arg, sendingOptions);

        void Send_<T>(object methodIdentity, T arg, SendingOptions sendingOptions)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, false);
            request.HasArgument = true;
            request.Argument = arg;
            this.logger.Debug($"Sending {request}");
            SendMessage(request, sendingOptions);
            OnSendingCompleted(request, sendingOptions);
        }
    }
}
