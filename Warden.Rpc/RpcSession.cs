using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.IO;
using Warden.Rpc.Events;
using Warden.Rpc.Payload;
using Warden.Util;

namespace Warden.Rpc
{
    public abstract class RpcSession : IRpcSession
    {
        public object Tag { get; set; }
        public bool IsClosed => closed;

        public int DefaultExecutionTimeout => defaultExecutionTimeout;
        public bool OrderedExecution => orderedExecution;
        public int OrderedExecutionMaxQueue => orderedExecutionMaxQueue;
        public IRpcConnection Connection { get; private set; }

        readonly ConcurrentDictionary<uint, RemotingRequest> requests;
        readonly RpcSerializer rpcSerializer;
        readonly RemotingObjectConfiguration remotingObjectConfiguration;
        readonly bool orderedExecution;
        readonly int defaultExecutionTimeout;
        readonly int orderedExecutionMaxQueue;

        readonly object orderedExecutionTaskMutex = new object();
        readonly object lastRequestIdMutex = new object();
        protected readonly ILogger logger;
        readonly TaskScheduler taskScheduler;
        
        Task orderedExecutionTask;
        uint lastRequestId;
        volatile int executionQueueSize;
        volatile bool closed;
        object requestsMutex = new object();
        
        protected RemotingObjectScheme remotingObjectScheme;
        protected object remotingObject;

        public RpcSession(RpcSessionContext sessionContext)
        {
            if (sessionContext.Serializer == null)
                throw new ArgumentNullException(nameof(sessionContext.Serializer));
            if (sessionContext.Connection == null)
                throw new ArgumentNullException(nameof(sessionContext.Connection));
            if (sessionContext.LogManager == null)
                throw new ArgumentNullException(nameof(sessionContext.LogManager));
            if (sessionContext.TaskScheduler == null)
                throw new ArgumentNullException(nameof(sessionContext.TaskScheduler));
            
            this.requests = new ConcurrentDictionary<uint, RemotingRequest>();
            this.rpcSerializer = sessionContext.Serializer;
            this.orderedExecution = sessionContext.OrderedExecution;
            this.orderedExecutionTask = Task.CompletedTask;
            this.orderedExecutionMaxQueue = sessionContext.OrderedExecutionMaxQueue;
            this.remotingObjectConfiguration = sessionContext.RemotingObjectConfiguration;
            this.taskScheduler = sessionContext.TaskScheduler;
            this.Connection = sessionContext.Connection;
            this.defaultExecutionTimeout = sessionContext.DefaultExecutionTimeout;
            this.logger = sessionContext.LogManager.GetLogger(nameof(RpcSession));
            this.logger.Meta["kind"] = this.GetType().Name;
            this.logger.Meta["connection_id"] = this.Connection.Id;
            this.logger.Meta["connection_endpoint"] = new RefLogLabel<IRpcConnection>(this.Connection, s => s.RemoteEndpoint);
            this.logger.Meta["closed"] = new RefLogLabel<RpcSession>(this, s => s.closed);
            this.logger.Meta["tag"] = new RefLogLabel<RpcSession>(this, s => s.Tag);
            this.logger.Meta["latency"] = new RefLogLabel<RpcSession>(this, s =>
            {
                var lat = s.Connection.Latency;
                if (lat.HasValue)
                    return lat.Value;
                else
                    return "";
            });
            this.logger.Debug($"{sessionContext.Connection} created {this}");
        }

        public virtual void InitializeRemotingObject(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            this.remotingObject = obj;
            this.remotingObjectScheme = new RemotingObjectScheme(this.remotingObjectConfiguration, obj.GetType());
        }

        public virtual void DeleteRemotingObject()
        {
            this.remotingObject = null;
            this.remotingObjectScheme = null;
        }

        public bool Close()
        {
            if (closed)
                return false;
            closed = true;
            
            Connection.Close();

            lock (requestsMutex)
            {
                foreach (var pair in requests)
                {
                    pair.Value.SetError(new RemotingException($"{this.GetType().Name} was closed prematurely!"));
                }

                requests.Clear();
            }

            try
            {
                OnClose();
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnClose)}: {e}");
            }
            
            
            this.remotingObject = null;
            this.remotingObjectScheme = null;

            this.logger.Debug($"Closed!");
            
            return true;
        }

        protected virtual void OnClose()
        {
            
        }

        protected void CheckClosed()
        {
            if (closed)
                throw new InvalidOperationException($"{nameof(RpcSession)} already closed");
        }

        internal virtual void OnExtraMessage(MessageType messageType, ReadFormatterInfo readFormatterInfo)
        {
            throw new ArgumentException($"Wrong message type: {messageType}, perhaps encryption or compression mismatch");
        }

        public void OnMessage(IReader reader)
        {
            try
            {
                if (closed)
                {
                    logger.Debug($"Got message when closed. Ignoring...");
                    return;
                }

                MessageType messageType = (MessageType) reader.ReadByte();
                ReadFormatterInfo readFormatterInfo = new ReadFormatterInfo(reader, rpcSerializer);
                switch (messageType)
                {
                    case MessageType.RpcRequest:
                        RemotingRequest remotingRequest = new RemotingRequest();
                        remotingRequest.MergeFrom(readFormatterInfo);
                        LogMessageReceived(remotingRequest);
                        EnqueueRequest(remotingRequest);
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
                        OnExtraMessage(messageType, readFormatterInfo);
                        break;
                }
            }
            catch (Exception outerException)
            {
                logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnMessage)}(): {outerException}");
                Connection.Close();
            }
        }


        protected internal void LogMessageReceived(ICustomMessage message)
        {
            this.logger.Trace($"Received {message}");
        }

        public override string ToString()
        {
            return $"{nameof(RpcSession)}[connection={Connection},tag={Tag},closed={closed}]";
        }

        void ExecuteResponseError(RemotingResponseError remotingResponseError)
        {
            bool exists = requests.TryRemove(remotingResponseError.RequestId, out RemotingRequest remotingRequest);
            if (!exists)
            {
                logger.Warn($"Got response error for unknown request id {remotingResponseError.RequestId}");
                return;
            }
            
            remotingRequest.SetError(remotingResponseError.Exception);
            ProcessRemoteExecutionExceptionInternal(remotingRequest, remotingResponseError.Exception);
        }

        void ExecuteResponse(RemotingResponse remotingResponse)
        {
            bool exists = requests.TryRemove(remotingResponse.RequestId, out RemotingRequest remotingRequest);
            if (!exists)
            {
                logger.Warn($"Got response for unknown request id {remotingResponse.RequestId}");
                return;
            }

            remotingRequest.SetResult(remotingResponse);
        }

        void EnqueueRequest(RemotingRequest request)
        {
            if (orderedExecution && executionQueueSize >= orderedExecutionMaxQueue)
            {
                ProcessLocalExecutionExceptionInternal(request, new RemotingException("Execution queue exceed it's limits"));
                return;
            }

            if (!orderedExecution)
            {
                Task.Factory.StartNew(() => ExecuteRequestInternalAsync(request), 
                    default, TaskCreationOptions.None,
                    taskScheduler ?? TaskScheduler.Default);
            }
            else
            {
                lock (orderedExecutionTaskMutex)
                {
                    Interlocked.Increment(ref executionQueueSize);
                    orderedExecutionTask = orderedExecutionTask.ContinueWith((t, o)
                        => ExecuteRequestInternalAsync(o as RemotingRequest), request,
                            default,
                            TaskContinuationOptions.ExecuteSynchronously,
                            taskScheduler ?? TaskScheduler.Default).Unwrap();
                }
            }
        }
        
        async Task ExecuteRequestInternalAsync(RemotingRequest request)
        {
            try
            {
                if (this.closed)
                {
                    logger.Debug($"Dropping incoming request {request}, connection already closed");
                    return;
                }

                logger.Debug($"Executing {request} locally");

                ExecutionRequest executionRequest = new ExecutionRequest(request);

                LocalExecutionStartingEventArgs eventArgsStarting = new LocalExecutionStartingEventArgs(executionRequest);
                OnLocalExecutionStarting(eventArgsStarting);

                Stopwatch sw = new Stopwatch();
                sw.Start();
                var executionResponse = await ExecuteRequestAsync(new ExecutionRequest(request)).ConfigureAwait(false);
                sw.Stop();


                double timeSeconds = sw.ElapsedTicks / (double) Stopwatch.Frequency;
                ulong timeSpanTicks = (ulong) (timeSeconds * TimeSpan.TicksPerSecond);
                float ms = timeSpanTicks / (float) TimeSpan.TicksPerMillisecond;
                logger.Debug($"Executed {request} locally in {ms.ToString("0.00")}ms");

                LocalExecutionCompletedEventArgs eventArgsCompleted =
                    new LocalExecutionCompletedEventArgs(executionRequest, executionResponse, ms);
                OnLocalExecutionCompleted(eventArgsCompleted);

                if (request.ExpectAck)
                {
                    RemotingResponse response = new RemotingResponse();
                    response.RequestId = request.RequestId;
                    response.ExecutionTime = timeSpanTicks;
                    response.HasArgument = false;
                    if (request.ExpectResponse)
                    {
                        response.HasArgument = executionResponse.HasResult;
                        response.Argument = executionResponse.Result;
                    }
                    
                    SendMessage(response, false);
                }
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
        
        void ProcessRemoteExecutionExceptionInternal(RemotingRequest remotingRequest, RemotingException exception)
        {
            logger.Error($"Remote execution exception on {remotingRequest}: {exception}");

            try
            {
                OnRemoteExecutionException(new RemoteExecutionExceptionEventArgs(new ExecutionRequest(remotingRequest), exception));
            }
            catch (Exception ex)
            {
                logger.Error($"Got an unhandled exception in OnRemoteExecutionException(): {ex}");
            }
        }
        
        void ProcessLocalExecutionExceptionInternal(RemotingRequest remotingRequest, RemotingException exception)
        {
            logger.Error($"Local method execution exception on {remotingRequest}: {exception}");

            if (remotingRequest.ExpectAck)
            {
                RemotingResponseError remotingResponseError = new RemotingResponseError(
                    remotingRequest.RequestId,
                    remotingRequest.MethodKey,
                    exception);


                SendMessage(remotingResponseError, false);
            }

            try
            {
                LocalExecutionExceptionEventArgs eventArgs = new LocalExecutionExceptionEventArgs(exception,
                    new ExecutionRequest(remotingRequest));
                OnLocalExecutionException(eventArgs);
            }
            catch (Exception ex)
            {
                logger.Error($"Got an unhandled exception in {nameof(OnLocalExecutionException)}(): {ex}");
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

        protected virtual async Task<ExecutionResponse> ExecuteRequestAsync(ExecutionRequest request)
        {
            if (remotingObjectScheme == null || remotingObject == null)
                throw new NullReferenceException($"{nameof(RpcSession)} isn't initialized properly. Remoting object is null");

            var container = remotingObjectScheme.GetInvocationContainer(request.MethodKey);

            object result;
            if (request.HasArgument)
                result = await container.InvokeAsync(remotingObject, request.Argument).ConfigureAwait(false);
            else
                result = await container.InvokeAsync(remotingObject).ConfigureAwait(false);

            ExecutionResponse executionResponse = new ExecutionResponse(container.DoesReturnValue, result);

            return executionResponse;
        }

        protected internal RemotingRequest GetRequest(object methodIdentity, bool expectResponse, bool expectAck)
        {
            if (!(methodIdentity is int || methodIdentity is string))
                throw new ArgumentException($"{nameof(methodIdentity)} should be int or string");
            if (!expectAck && expectResponse)
                throw new ArgumentException("You can't reset expectAck and set expectResponse", nameof(expectAck));

            lock (requestsMutex)
            {
                CheckClosed();
                RemotingRequest request = new RemotingRequest();
                request.MethodKey = methodIdentity;
                request.HasArgument = false;
                request.Argument = null;
                request.ExpectResponse = expectResponse;
                request.ExpectAck = expectAck;
                
                lock (lastRequestIdMutex)
                    request.RequestId = lastRequestId++;

                if (expectAck && !requests.TryAdd(request.RequestId, request))
                    throw new InvalidOperationException("Could not create a new request");
                
                if (expectResponse)
                    request.CreateAwaiter();
                
                return request;
            }
        }

        protected virtual Task RemoteExecutionWrapper(RemotingRequest request, ExecutionOptions options,
            Task executionTask)
        {
            return executionTask;
        }

        protected internal void SendMessage(ICustomMessage message, bool throwIfFailed)
        {
            this.logger.Debug($"Sending {message}");
            if (!this.Connection.SendReliable(message))
            {
                if (throwIfFailed)
                    throw new RemotingException("Transport connection is closed");
                else
                    logger.Debug($"Couldn't send {message}, transport connection is closed");
            }
            this.logger.Trace($"Sent {message}");
        }

        async Task SendAndWait(RemotingRequest request, ExecutionOptions options)
        {
            try
            {
                this.logger.Debug($"Executing {request} remotely with {options}");
                ExecutionRequest executionRequest = new ExecutionRequest(request);
                OnRemoteExecutionStarting(new RemoteExecutionStartingEventArgs(executionRequest, options));
                SendMessage(request, true);
                int timeout = defaultExecutionTimeout;
                if (options.Timeout > Timeout.Infinite)
                    timeout = options.Timeout;
                await RemoteExecutionWrapper(request, options, request.WaitAsync(timeout)).ConfigureAwait(false);
                float ms = request.Response.ExecutionTime / (float) TimeSpan.TicksPerMillisecond;
                this.logger.Debug($"Executed {request} remotely in {ms.ToString("0.00")}ms");
                OnRemoteExecutionCompleted(new RemoteExecutionCompletedEventArgs(executionRequest,
                    new ExecutionResponse(request.Response), options, ms));
            }
            catch (Exception ex)
            {
                Exception inner = ex.GetInnermostException();
                RemotingException remotingException = inner as RemotingException;
                if (remotingException == null)
                    remotingException = new RemotingException(inner);
                
                throw remotingException;
            }
        }
        
        public virtual Task ExecuteAsync(int methodIdentity) => ExecuteAsyncInternal(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(string methodIdentity) => ExecuteAsyncInternal(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(int methodIdentity, ExecutionOptions options) => ExecuteAsyncInternal(methodIdentity, options);
        public virtual Task ExecuteAsync(string methodIdentity, ExecutionOptions options) => ExecuteAsyncInternal(methodIdentity, options);

        Task ExecuteAsyncInternal(object methodIdentity, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true, true);
            request.HasArgument = false;
            return SendAndWait(request, options);
        }

        public virtual Task ExecuteAsync<A>(int methodIdentity, A arg) => ExecuteAsyncInternal(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task ExecuteAsync<A>(string methodIdentity, A arg) => ExecuteAsyncInternal(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task ExecuteAsync<A>(int methodIdentity, A arg, ExecutionOptions options) => ExecuteAsyncInternal(methodIdentity, arg, options);
        public virtual Task ExecuteAsync<A>(string methodIdentity, A arg, ExecutionOptions options) => ExecuteAsyncInternal(methodIdentity, arg, options);

        Task ExecuteAsyncInternal<A>(object methodIdentity, A arg, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true, true);
            request.HasArgument = true;
            request.Argument = arg;
            return SendAndWait(request, options);
        }

        public virtual Task<R> ExecuteAsync<R>(int methodIdentity) => ExecuteAsyncInternal<R>(methodIdentity, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R>(string methodIdentity) => ExecuteAsyncInternal<R>(methodIdentity, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R>(int methodIdentity, ExecutionOptions options) => ExecuteAsyncInternal<R>(methodIdentity, options);
        public virtual Task<R> ExecuteAsync<R>(string methodIdentity, ExecutionOptions options) => ExecuteAsyncInternal<R>(methodIdentity, options);

        async Task<R> ExecuteAsyncInternal<R>(object methodIdentity, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true, true);
            request.HasArgument = false;
            await SendAndWait(request, options).ConfigureAwait(false);
            return (R)request.Result;
        }

        public virtual Task<R> ExecuteAsync<R, A>(int methodIdentity, A arg) => ExecuteAsyncInternal<R, A>(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R, A>(string methodIdentity, A arg) => ExecuteAsyncInternal<R, A>(methodIdentity, arg, ExecutionOptions.Default);
        public virtual Task<R> ExecuteAsync<R, A>(int methodIdentity, A arg, ExecutionOptions options) => ExecuteAsyncInternal<R, A>(methodIdentity, arg, options);
        public virtual Task<R> ExecuteAsync<R, A>(string methodIdentity, A arg, ExecutionOptions options) => ExecuteAsyncInternal<R, A>(methodIdentity, arg, options);

        async Task<R> ExecuteAsyncInternal<R, A>(object methodIdentity, A arg, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true, true);
            request.HasArgument = true;
            request.Argument = arg;
            await SendAndWait(request, options).ConfigureAwait(false);
            return (R)request.Result;
        }

        public virtual void Send(int methodIdentity) => SendInternal(methodIdentity, SendingOptions.Default);
        public virtual void Send(string methodIdentity) => SendInternal(methodIdentity, SendingOptions.Default);
        public virtual void Send(int methodIdentity, SendingOptions sendingOptions) => SendInternal(methodIdentity, sendingOptions);
        public virtual void Send(string methodIdentity, SendingOptions sendingOptions) => SendInternal(methodIdentity, sendingOptions);

        void SendInternal(object methodIdentity, SendingOptions sendingOptions)
        {
            if (sendingOptions.ThrowIfFailedToSend)
                CheckClosed();
            else if (closed)
                return;
            RemotingRequest request = GetRequest(methodIdentity, false, !sendingOptions.NoAck);
            request.HasArgument = false;
            SendMessage(request, sendingOptions.ThrowIfFailedToSend);
        }

        public virtual void Send<T>(int methodIdentity, T arg) => SendInternal(methodIdentity, arg, SendingOptions.Default);
        public virtual void Send<T>(string methodIdentity, T arg) => SendInternal(methodIdentity, arg, SendingOptions.Default);
        public virtual void Send<T>(int methodIdentity, T arg, SendingOptions sendingOptions) => SendInternal(methodIdentity, arg, sendingOptions);
        public virtual void Send<T>(string methodIdentity, T arg, SendingOptions sendingOptions) => SendInternal(methodIdentity, arg, sendingOptions);

        void SendInternal<T>(object methodIdentity, T arg, SendingOptions sendingOptions)
        {
            if (sendingOptions.ThrowIfFailedToSend)
                CheckClosed();
            else if (closed)
                return;
            RemotingRequest request = GetRequest(methodIdentity, false, !sendingOptions.NoAck);
            request.HasArgument = true;
            request.Argument = arg;
            SendMessage(request, sendingOptions.ThrowIfFailedToSend);
        }
    }
}
