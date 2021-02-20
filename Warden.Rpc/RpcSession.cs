using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.IO;
using Warden.Rpc.EventArgs;
using Warden.Rpc.Payload;
using Warden.Util;

namespace Warden.Rpc
{
    public abstract class RpcSession : IRpcSession
    {
        public object Tag { get; set; }
        protected bool IsClosed => closed;

        public int DefaultExecutionTimeout => defaultExecutionTimeout;
        public bool OrderedExecution => orderedExecution;
        public int OrderedExecutionMaxQueue => orderedExecutionMaxQueue;
        public IRpcConnectionTcp Connection { get; private set; }

        readonly ConcurrentDictionary<uint, RemotingRequest> requests;
        readonly RpcSerializer rpcSerializer;
        readonly bool orderedExecution;
        readonly int defaultExecutionTimeout;
        readonly int orderedExecutionMaxQueue;
        readonly object orderedExecutionTaskMutex = new object();
        readonly object lastRequestIdMutex = new object();
        readonly ILogger logger;
        readonly TaskScheduler taskScheduler;

        Task orderedExecutionTask;
        uint lastRequestId;
        volatile int executionQueueSize;
        volatile bool closed;
        object requestsMutex = new object();
        
        RemotingObjectScheme remotingObjectScheme;
        object remotingObject;

        public RpcSession(RpcSessionContext context)
        {
            if (context.Serializer == null)
                throw new ArgumentNullException(nameof(context.Serializer));
            if (context.Connection == null)
                throw new ArgumentNullException(nameof(context.Connection));
            if (context.LogManager == null)
                throw new ArgumentNullException(nameof(context.LogManager));
            if (context.TaskScheduler == null)
                throw new ArgumentNullException(nameof(context.TaskScheduler));
            
            this.requests = new ConcurrentDictionary<uint, RemotingRequest>();
            this.rpcSerializer = context.Serializer;
            this.orderedExecution = context.OrderedExecution;
            this.orderedExecutionTask = Task.CompletedTask;
            this.orderedExecutionMaxQueue = context.OrderedExecutionMaxQueue;
            this.taskScheduler = context.TaskScheduler;
            this.Connection = context.Connection;
            this.defaultExecutionTimeout = context.DefaultExecutionTimeout;
            this.logger = context.LogManager.GetLogger(nameof(RpcSession));
            this.logger.Meta["kind"] = this.GetType().Name;
        }

        internal void Initialize()
        {
            this.remotingObject = this.InitializeRemotingObject();
            RemotingObjectConfiguration configuration = new RemotingObjectConfiguration();
            remotingObjectScheme = new RemotingObjectScheme();
        }

        protected virtual object InitializeRemotingObject()
        {
            return this;
        }

        public virtual bool Close()
        {
            if (closed)
                return false;
            closed = true;

            lock (requestsMutex)
            {
                foreach (var pair in requests)
                {
                    pair.Value.SetError(new RemotingException($"{this.GetType().Name} was closed!"));
                }

                requests.Clear();
            }

            this.logger.Debug($"{this} closed!");
            return true;
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

        public override string ToString()
        {
            return $"{nameof(RpcSession)}[connection={Connection},tag={Tag}]";
        }

        void ExecuteResponseError(RemotingResponseError remotingResponseError)
        {
            bool exists = requests.TryRemove(remotingResponseError.RequestId, out RemotingRequest remotingRequest);
            if (exists)
                remotingRequest.SetError(remotingResponseError.Exception);
        }

        void ExecuteResponse(RemotingResponse remotingResponse)
        {
            bool exists = requests.TryRemove(remotingResponse.RequestId, out RemotingRequest remotingRequest);
            if (!exists)
            {
                logger.Warn($"{this} got response for unknown request id {remotingResponse.RequestId}");
            }
            else
            {
                remotingRequest.SetResult(remotingResponse);
            }
        }

        void ExecuteRequest(RemotingRequest request)
        {
            if (orderedExecution && executionQueueSize >= orderedExecutionMaxQueue)
            {
                ProcessLocalExecutionExceptionInternal(request, new RemotingException("Execution queue exceed it's limits"));
                return;
            }

            if (!orderedExecution)
            {
                Task.Factory.StartNew(() => ExecuteRequestOuterAsync(request), 
                    default, TaskCreationOptions.None,
                    taskScheduler ?? TaskScheduler.Default);
            }
            else
            {
                lock (orderedExecutionTaskMutex)
                {
                    Interlocked.Increment(ref executionQueueSize);
                    orderedExecutionTask = orderedExecutionTask.ContinueWith((t, o)
                        => ExecuteRequestOuterAsync(o as RemotingRequest), request,
                            default,
                            TaskContinuationOptions.None,
                            taskScheduler ?? TaskScheduler.Default).Unwrap();
                }
            }
        }

        async Task ExecuteRequestOuterAsync(RemotingRequest request)
        {
            if (this.closed)
            {
                logger.Debug($"Dropping incoming request {request}, connection already closed");
                return;
            }
            
            try
            {
                logger.Debug($"Executing {request} locally");
                
                try
                {
                    LocalExecutionStartingEventArgs eventArgs = new LocalExecutionStartingEventArgs(request);
                    OnLocalExecutionStarting(eventArgs);
                }
                catch (Exception ex)
                {
                    logger.Error($"{this} got an unhandled exception in {nameof(OnLocalExecutionStarting)}(): {ex}");
                }

                Stopwatch sw = new Stopwatch();
                sw.Start();
                var result = await ExecuteRequestAsync(request).ConfigureAwait(false);
                sw.Stop();
                

                double timeSeconds = sw.ElapsedTicks / (double)Stopwatch.Frequency;
                ulong timeSpanTicks = (ulong)(timeSeconds * TimeSpan.TicksPerSecond);
                float ms = timeSpanTicks / (float)TimeSpan.TicksPerMillisecond;
                logger.Debug($"Executed {request} locally in {ms.ToString("0.00")}ms");

                RemotingResponse response = null;
                if (request.ExpectResponse)
                {
                    response = new RemotingResponse();
                    response.HasArgument = result.HasResult;
                    response.Argument = result.Result;
                    response.RequestId = request.RequestId;
                    response.ExecutionTime = timeSpanTicks;
                }
                
                try
                {
                    LocalExecutionCompletedEventArgs eventArgs = new LocalExecutionCompletedEventArgs(request, response, ms);
                    OnLocalExecutionCompleted(eventArgs);
                }
                catch (Exception ex)
                {
                    logger.Error($"{this} got an unhandled exception in {nameof(OnLocalExecutionCompleted)}(): {ex}");
                }
                
                if (response != null)
                    SendMessage(response);
            }
            catch (Exception ex)
            {
                Exception innermost = ex.GetInnermostException();
                RemotingException remotingException = new RemotingException(innermost);
                ProcessLocalExecutionExceptionInternal(request, remotingException);
            }
            finally
            {
                Interlocked.Decrement(ref executionQueueSize);
            }
        }
        
        void ProcessRemoteExecutionExceptionInternal(RemotingRequest remotingRequest, RemotingException exception, ExecutionOptions options)
        {
            logger.Error($"Remote execution exception on {remotingRequest}: {exception}");

            try
            {
                OnRemoteExecutionException(new RemoteExecutionExceptionEventArgs(remotingRequest, exception, options));
            }
            catch (Exception ex)
            {
                logger.Error($"{this} got an unhandled exception in OnRemoteExecutionException(): {ex}");
            }
        }
        
        void ProcessLocalExecutionExceptionInternal(RemotingRequest remotingRequest, RemotingException exception)
        {
            logger.Error($"Local method execution exception on {remotingRequest}: {exception}");

            RemotingResponseError remotingResponseError = new RemotingResponseError(
                    remotingRequest.RequestId,
                    remotingRequest.MethodKey,
                    exception);

            SendMessage(remotingResponseError);

            try
            {
                LocalExecutionExceptionEventArgs eventArgs = new LocalExecutionExceptionEventArgs(exception,
                    remotingRequest);
                OnLocalExecutionException(eventArgs);
            }
            catch (Exception ex)
            {
                logger.Error($"{this} got an unhandled exception in {nameof(OnLocalExecutionException)}(): {ex}");
            }
        }
        
        protected virtual void OnLocalExecutionStarting(LocalExecutionStartingEventArgs args)
        {

        }
        
        protected virtual void OnLocalExecutionCompleted(LocalExecutionCompletedEventArgs args)
        {

        }

        protected virtual void OnLocalExecutionException(LocalExecutionExceptionEventArgs args)
        {

        }

        protected virtual void OnRemoteExecutionStarting(RemoteExecutionStartingEventArgs args)
        {

        }
        
        protected virtual void OnRemoteExecutionCompleted(RemoteExecutionCompletedEventArgs args)
        {

        }
        
        protected virtual void OnRemoteExecutionException(RemoteExecutionExceptionEventArgs args)
        {

        }

        protected virtual Task<ExecutionResult> ExecuteRequestAsync(RemotingRequest request)
        {
            if (remotingObjectScheme == null || UserSession == null)
                throw new InvalidOperationException("User session isn't started or already destroyed. Can't execute method");

            var container = remotingObjectScheme.GetInvocationContainer(request.MethodKey);

            object result = null;
            if (request.HasArgument)
                result = await container.InvokeAsync(UserSession, request.Argument).ConfigureAwait(false);
            else
                result = await container.InvokeAsync(UserSession).ConfigureAwait(false);

            ExecutionResult executionResult = new ExecutionResult();
            executionResult.HasResult = container.DoesReturnValue;
            executionResult.Result = result;

            return executionResult;
        }

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
                
                lock (lastRequestIdMutex)
                    request.RequestId = lastRequestId++;

                if (expectResponse)
                {
                    if (!requests.TryAdd(request.RequestId, request))
                        throw new InvalidOperationException("Could not create a new request");
                    request.CreateAwaiter();
                }
                return request;
            }
        }

        protected virtual Task RemoteExecutionWrapper(RemotingRequest request, ExecutionOptions options,
            Task executionTask)
        {
            return executionTask;
        }

        void SendMessage(ICustomMessage message)
        {
            this.logger.Trace($"{this} sending {message}");
            if (!this.Connection.SendReliable(message))
                throw new RemotingException("Transport connection is closed");
        }

        async Task SendAndWait(RemotingRequest request, ExecutionOptions options)
        {
            try
            {
                this.logger.Debug($"Executing {request} remotely with {options}");
                OnRemoteExecutionStarting(new RemoteExecutionStartingEventArgs(request, options));
                SendMessage(request);
                int timeout = defaultExecutionTimeout;
                if (options.Timeout > Timeout.Infinite)
                    timeout = options.Timeout;
                await RemoteExecutionWrapper(request, options,request.WaitAsync(timeout)).ConfigureAwait(false);
                float ms = request.Response.ExecutionTime / (float) TimeSpan.TicksPerMillisecond;
                this.logger.Debug($"Executed {request} remotely in {ms.ToString("0.00")}ms");
                OnRemoteExecutionCompleted(new RemoteExecutionCompletedEventArgs(request, request.Response, options, ms));
            }
            catch (Exception ex)
            {
                Exception inner = ex.GetInnermostException();
                RemotingException remotingException = inner as RemotingException;
                if (remotingException == null)
                    remotingException = new RemotingException(inner);
                
                ProcessRemoteExecutionExceptionInternal(request, remotingException, options);
                throw remotingException;
            }
        }
        
        public virtual Task ExecuteAsync(int methodIdentity) => ExecuteAsync_(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(string methodIdentity) => ExecuteAsync_(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(int methodIdentity, ExecutionOptions options) => ExecuteAsync_(methodIdentity, options);
        public virtual Task ExecuteAsync(string methodIdentity, ExecutionOptions options) => ExecuteAsync_(methodIdentity, options);

        Task ExecuteAsync_(object methodIdentity, ExecutionOptions options)
        {
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
            RemotingRequest request = GetRequest(methodIdentity, false);
            request.HasArgument = false;
            this.logger.Debug($"Sending {request}");
            SendMessage(request);
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
            SendMessage(request);
        }
    }
}
