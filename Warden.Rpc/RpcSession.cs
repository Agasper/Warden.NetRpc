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
        public IRpcConnection Connection { get; private set; }

        readonly ConcurrentDictionary<uint, RemotingRequest> requests;
        readonly RpcSerializer rpcSerializer;
        readonly RemotingObjectConfiguration remotingObjectConfiguration;
        readonly bool orderedExecution;
        readonly int defaultExecutionTimeout;
        readonly int orderedExecutionMaxQueue;

        readonly object orderedExecutionTaskMutex = new object();
        readonly object lastRequestIdMutex = new object();
        readonly protected internal ILogger logger;
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
            if (sessionContext.RemotingObjectConfiguration == null)
                throw new ArgumentNullException(nameof(sessionContext.RemotingObjectConfiguration));
            
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
        }


        public void MergeFrom(ReadFormatterInfo readFormatterInfo)
        {
            
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
            
            Connection.Close();
            
            closed = true;

            lock (requestsMutex)
            {
                foreach (var pair in requests)
                {
                    pair.Value.SetError(new RemotingException($"{this.GetType().Name} was closed!"));
                }

                requests.Clear();
            }

            this.remotingObject = null;
            this.remotingObjectScheme = null;

            this.logger.Debug($"{this} closed!");
            
            try
            {
                OnClose();
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnClose)}: {e}");
            }
            
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
            throw new ArgumentException($"Wrong message type: {messageType}");
        }

        public void OnMessage(IReader reader)
        {
            try
            {
                CheckClosed();
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
                logger.Error($"Closing connection due to unhandled exception on {this.GetType().Name}.{nameof(OnMessage)}(): {outerException}");
                Connection.Close();
            }
        }


        protected internal void LogMessageReceived(ICustomMessage message)
        {
            this.logger.Trace($"{this} received {message}");
        }

        public override string ToString()
        {
            return $"{nameof(RpcSession)}[connection={Connection},tag={Tag},closed={closed}]";
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
                            TaskContinuationOptions.None,
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

                LocalExecutionStartingEventArgs eventArgsStarting = new LocalExecutionStartingEventArgs(request);
                OnLocalExecutionStarting(eventArgsStarting);

                Stopwatch sw = new Stopwatch();
                sw.Start();
                var result = await ExecuteRequestAsync(new ExecutionRequest(request)).ConfigureAwait(false);
                sw.Stop();


                double timeSeconds = sw.ElapsedTicks / (double) Stopwatch.Frequency;
                ulong timeSpanTicks = (ulong) (timeSeconds * TimeSpan.TicksPerSecond);
                float ms = timeSpanTicks / (float) TimeSpan.TicksPerMillisecond;
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
                
                LocalExecutionCompletedEventArgs eventArgsCompleted =
                    new LocalExecutionCompletedEventArgs(request, response, ms);
                OnLocalExecutionCompleted(eventArgsCompleted);

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

        protected virtual void OnSessionReady()
        {
            
        }

        internal void SetReady()
        {
            OnSessionReady();
        }

        protected virtual async Task<ExecutionResult> ExecuteRequestAsync(ExecutionRequest request)
        {
            if (remotingObjectScheme == null || remotingObject == null)
                throw new NullReferenceException("RpcSession isn't initialized properly. Remoting object is null");

            var container = remotingObjectScheme.GetInvocationContainer(request.MethodKey);

            object result = null;
            if (request.HasArgument)
                result = await container.InvokeAsync(remotingObject, request.Argument).ConfigureAwait(false);
            else
                result = await container.InvokeAsync(remotingObject).ConfigureAwait(false);

            ExecutionResult executionResult = new ExecutionResult(container.DoesReturnValue, result);

            return executionResult;
        }

        protected internal RemotingRequest GetRequest(object methodIdentity, bool expectResponse)
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

        protected internal void SendMessage(ICustomMessage message)
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
        
        public virtual Task ExecuteAsync(int methodIdentity) => ExecuteAsyncInternal(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(string methodIdentity) => ExecuteAsyncInternal(methodIdentity, ExecutionOptions.Default);
        public virtual Task ExecuteAsync(int methodIdentity, ExecutionOptions options) => ExecuteAsyncInternal(methodIdentity, options);
        public virtual Task ExecuteAsync(string methodIdentity, ExecutionOptions options) => ExecuteAsyncInternal(methodIdentity, options);

        Task ExecuteAsyncInternal(object methodIdentity, ExecutionOptions options)
        {
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, true);
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
            RemotingRequest request = GetRequest(methodIdentity, true);
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
            RemotingRequest request = GetRequest(methodIdentity, true);
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
            RemotingRequest request = GetRequest(methodIdentity, true);
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
            CheckClosed();
            RemotingRequest request = GetRequest(methodIdentity, false);
            request.HasArgument = false;
            this.logger.Debug($"Sending {request}");
            SendMessage(request);
        }

        public virtual void Send<T>(int methodIdentity, T arg) => SendInternal(methodIdentity, arg, SendingOptions.Default);
        public virtual void Send<T>(string methodIdentity, T arg) => SendInternal(methodIdentity, arg, SendingOptions.Default);
        public virtual void Send<T>(int methodIdentity, T arg, SendingOptions sendingOptions) => SendInternal(methodIdentity, arg, sendingOptions);
        public virtual void Send<T>(string methodIdentity, T arg, SendingOptions sendingOptions) => SendInternal(methodIdentity, arg, sendingOptions);

        void SendInternal<T>(object methodIdentity, T arg, SendingOptions sendingOptions)
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
