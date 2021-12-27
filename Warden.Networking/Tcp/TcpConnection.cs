using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;

namespace Warden.Networking.Tcp
{
    public class TcpConnection : IDisposable
    {
        public bool IsClientConnection { get; private set; }
        public bool Stashed => stashed;
        public bool Disposed => disposed;
        public virtual object Tag { get; set; }
        public long Id { get; private set; }
        public EndPoint RemoteEndpoint
        {
            get
            {
                try
                {
                    var socket_ = this.socket;
                    if (socket_ == null)
                        return null;
                    return socket_.RemoteEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }
        public bool Connected
        {
            get
            {
                if (closed)
                    return false;
                var socket_ = this.socket;
                if (socket_ == null)
                    return false;
                return socket_.Connected;
            }
        }
        public DateTime Started { get; private set; }
        public TcpPeer Parent { get; private set; }
        public TcpConnectionStatistics Statistics { get; private set; }

        protected DateTime? LastKeepAliveRequestReceived { get; private set; }

        Socket socket;
        TcpRawMessage awaitingNextMessage;
        bool awaitingNextMessageHeaderValid;
        int awaitingNextMessageWrote;
        DateTime lastKeepAliveSent;
        bool keepAliveResponseGot;
        SemaphoreSlim sendSemaphore;

        volatile bool closed;
        volatile bool disposed;
        volatile bool stashed;
        volatile Task sendTask;
        readonly object sendMutex = new object();

        protected readonly ILogger logger;
        TcpRawMessageHeader awaitingMessageHeader;
        
        readonly byte[] recvBuffer;
        readonly byte[] sendBuffer;
        readonly ConcurrentQueue<DelayedMessage> latencySimulationRecvQueue;
        readonly ConcurrentQueue<DelayedMessage> latencySimulationSendQueue;

        struct DelayedMessage //for latency simulation
        {
            public TcpRawMessage message;
            public DateTime releaseTimestamp;

            TaskCompletionSource<DelayedMessage> taskCompletionSource;

            public Task GetTask()
            {
                if (taskCompletionSource == null)
                    taskCompletionSource = new TaskCompletionSource<DelayedMessage>();
                return taskCompletionSource.Task;
            }

            public void Complete(Task task)
            {
                if (taskCompletionSource == null)
                    return;
                if (task.IsCanceled)
                    taskCompletionSource.TrySetCanceled();
                if (task.IsFaulted)
                    taskCompletionSource.TrySetException(task.Exception ?? new Exception("Unknown exception"));
                if (task.IsCompleted && !task.IsFaulted)
                    taskCompletionSource.TrySetResult(this);
            }
        }

        public TcpConnection(TcpPeer parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            this.latencySimulationRecvQueue = new ConcurrentQueue<DelayedMessage>();
            this.latencySimulationSendQueue = new ConcurrentQueue<DelayedMessage>();
            this.stashed = true;
            this.Statistics = new TcpConnectionStatistics();
            this.sendSemaphore = new SemaphoreSlim(1, 1);
            this.recvBuffer = new byte[parent.Configuration.BufferSize];
            this.sendBuffer = new byte[parent.Configuration.BufferSize];
            this.awaitingMessageHeader = new TcpRawMessageHeader();
            this.Parent = parent;
            this.logger = parent.Configuration.LogManager.GetLogger(nameof(TcpConnection));
            this.logger.Meta["kind"] = this.GetType().Name;
            this.logger.Meta["connection_endpoint"] = new RefLogLabel<TcpConnection>(this, v => v.RemoteEndpoint);
            this.logger.Meta["connected"] = new RefLogLabel<TcpConnection>(this, s => s.Connected);
            this.logger.Meta["closed"] = new RefLogLabel<TcpConnection>(this, s => s.closed);
            this.logger.Meta["latency"] = new RefLogLabel<TcpConnection>(this, s =>
            {
                var lat = s.Statistics.Latency;
                if (lat.HasValue)
                    return lat.Value;
                else
                    return "";
            });

            // return $"{nameof(TcpConnection)}[id={Id}, connected={Connected}, endpoint={RemoteEndpoint}]";
        }

        internal void CheckParent(TcpPeer parent)
        {
            if (this.Parent != parent)
                throw new InvalidOperationException($"This connection belongs to the another parent");
        }

        public virtual void Init(long connectionId, Socket socket, bool isClientConnection)
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));
            if (!Stashed)
                throw new InvalidOperationException($"{nameof(TcpConnection)} is still being used (not stashed ot disposed)");

            this.stashed = false;
            this.IsClientConnection = isClientConnection;
            this.logger.Meta["connection_id"] = connectionId;
            this.keepAliveResponseGot = true;
            this.Id = connectionId;
            this.socket = socket;
            this.sendTask = Task.CompletedTask;
            this.closed = false;
            this.Started = DateTime.UtcNow;
            logger.Info($"Connection #{Id} initialized");
            
            Parent.OnConnectionOpenedInternal(this);
        }

        public virtual void Stash()
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));
            if (Stashed)
                return;

            Close();
            DestroySocket(null);

            this.awaitingMessageHeader.Reset();
            this.Statistics.Reset();
            this.lastKeepAliveSent = new DateTime(0);
            this.awaitingNextMessageWrote = 0;
            this.awaitingNextMessageHeaderValid = false;
            this.LastKeepAliveRequestReceived = null;

            if (this.awaitingNextMessage != null)
            {
                this.awaitingNextMessage.Dispose();
                this.awaitingNextMessage = null;
            }
            
            while (this.latencySimulationRecvQueue.TryDequeue(out DelayedMessage removed))
                removed.message.Dispose();
            while (this.latencySimulationSendQueue.TryDequeue(out DelayedMessage removed))
                removed.message.Dispose();

            stashed = true;
            logger.Debug($"Connection #{Id} stashed!");
        }

        /// <summary>
        /// Returns the memory used by this connection
        /// </summary>
        /// <param name="disposing">Whether we're disposing (true), or being called by the finalizer (false)</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;
            disposed = true;

            Close();
            DestroySocket(null);

            if (this.awaitingNextMessage != null)
            {
                this.awaitingNextMessage.Dispose();
                this.awaitingNextMessage = null;
            }

            if (sendSemaphore != null)
            {
                sendSemaphore.Dispose();
                sendSemaphore = null;
            }

            logger.Debug($"Connection #{Id} disposed!");
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void DestroySocket(int? timeout)
        {
            var socket_ = socket;
            if (socket_ != null)
            {
                logger.Debug("Connection #{Id} socket destroying...");
                if (timeout.HasValue)
                    socket_.Close(timeout.Value);
                else
                    socket_.Close();
                socket_.Dispose();
                this.socket = null;
            }
        }

        internal void StartReceive()
        {
            socket.BeginReceive(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ReceiveCallback, null);
        }

        protected internal virtual void OnConnectionClosed(ConnectionClosedEventArgs args)
        {

        }
        
        protected internal virtual void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {

        }

        public virtual void Close()
        {
            CloseInternal(null);
        }
        
        void CloseInternal(int? timeout)
        {
            if (closed)
                return;
            closed = true;
            
            try
            {
                logger.Debug($"Connection #{Id} closing!");
                
                //https://docs.microsoft.com/en-gb/windows/win32/winsock/graceful-shutdown-linger-options-and-socket-closure-2
                var socket_ = socket;
                if (socket_ != null)
                {
                    try
                    {
                        socket_.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException) { }
                    finally
                    {
                        DestroySocket(timeout);
                    }
                }
                
                logger.Info($"Connection #{Id} closed!");

                Parent.OnConnectionClosedInternal(this);
                
                if (!this.disposed && !this.stashed)
                    this.Dispose();
            }
            catch (Exception ex)
            {
                logger.Critical("Exception on connection close: " + ex);
            }
        }

        internal void ReceiveCallback(IAsyncResult result)
        {
            try
            {
                if (socket == null || !socket.Connected)
                    return;

                int bytesRead = socket.EndReceive(result);

                if (bytesRead == 0)
                {
                    Close();
                    return;
                }

                Statistics.BytesIn(bytesRead);
                logger.Trace($"Connection #{Id} recv data {bytesRead} bytes");

                int recvBufferPos = 0;
                int counter = 0;

                while (recvBufferPos <= bytesRead)
                {
                    int bytesLeft = bytesRead - recvBufferPos;
                    if (awaitingNextMessage == null)
                    {
                        if (!awaitingNextMessageHeaderValid && bytesLeft > 0)
                        {
                            awaitingNextMessageHeaderValid = awaitingMessageHeader.Write(
                                new ArraySegment<byte>(recvBuffer, recvBufferPos, bytesLeft), out int headerGotRead);
                            recvBufferPos += headerGotRead;
                        }
                        else if (awaitingNextMessageHeaderValid)
                        {
                            awaitingNextMessage = new TcpRawMessage(Parent.Configuration.MemoryStreamPool, awaitingMessageHeader.MessageSize);
                            awaitingNextMessageWrote = 0;
                        }
                        else if (bytesLeft == 0)
                            break;
                    }
                    else
                    {
                        if (awaitingNextMessageWrote < awaitingMessageHeader.MessageSize && bytesLeft > 0)
                        {
                            int toRead = bytesLeft;
                            if (toRead > awaitingMessageHeader.MessageSize - awaitingNextMessageWrote)
                                toRead = awaitingMessageHeader.MessageSize - awaitingNextMessageWrote;
                            if (toRead > 0)
                            {
                                awaitingNextMessage.BaseStream.Write(recvBuffer, recvBufferPos, toRead);
                                awaitingNextMessageWrote += toRead;
                                recvBufferPos += toRead;
                            }
                        }
                        else if (awaitingNextMessageWrote == awaitingMessageHeader.MessageSize)
                        {
                            Statistics.PacketIn();
                            awaitingNextMessage.BaseStream.Position = 0;
                            var message = awaitingNextMessage;
                            awaitingNextMessage = null;
                            message.Flags = awaitingMessageHeader.Options.Flags;
                            OnMessageReceivedInternalWithSimulation(message);
                            awaitingNextMessageWrote = 0;
                            awaitingMessageHeader = new TcpRawMessageHeader();
                            awaitingNextMessageHeaderValid = false;
                        }
                        else if (bytesLeft == 0)
                            break;
                    }

                    //Infinite loop protection
                    if (counter++ > recvBuffer.Length / 2 + 100)
                    {
                        logger.Critical($"Infinite loop in {this}");
                        throw new InvalidOperationException("Infinite loop");
                    }
                }

                if (socket != null)
                    StartReceive();

            }
            catch (ObjectDisposedException)
            { }
            catch (SocketException sex)
            {
                if (sex.SocketErrorCode != SocketError.ConnectionReset)
                    logger.Error($"Connection #{Id} broken due to exception: {sex}");
                else
                    logger.Info($"Connection #{Id} reset");
                Close();
            }
            catch (Exception ex)
            {
                logger.Error($"Connection #{Id} broken due to exception: {ex}");
                Close();
            }
        }

        void OnMessageReceivedInternalWithSimulation(TcpRawMessage message)
        {
            if (Parent.Configuration.ConnectionSimulation != null)
            {
                int delay = Parent.Configuration.ConnectionSimulation.GetHalfDelay();
                latencySimulationRecvQueue.Enqueue(new DelayedMessage()
                {
                    message = message,
                    releaseTimestamp = DateTime.UtcNow.AddMilliseconds(delay)
                });
                return;
            }

            OnMessageReceivedInternal(message);
        }

        void OnMessageReceivedInternal(TcpRawMessage message)
        {
            
            if (message.Flags.HasFlag(MessageHeaderFlags.KeepAliveRequest) ||
                    message.Flags.HasFlag(MessageHeaderFlags.KeepAliveResponse))
                logger.Trace($"Connection #{Id} recv message {message}");
            else
                logger.Debug($"Connection #{Id} recv message {message}");

            if (message.Flags.HasFlag(MessageHeaderFlags.KeepAliveRequest))
            {
                LastKeepAliveRequestReceived = DateTime.UtcNow;
                _ = SendMessageAsync(TcpRawMessage.GetEmpty(Parent.Configuration.MemoryStreamPool, MessageHeaderFlags.KeepAliveResponse));
                message.Dispose();
                return;
            }

            if (message.Flags.HasFlag(MessageHeaderFlags.KeepAliveResponse))
            {
                Statistics.UpdateLatency((float)(DateTime.UtcNow - this.lastKeepAliveSent).TotalMilliseconds);
                keepAliveResponseGot = true;
                message.Dispose();
                return;
            }

            Parent.Configuration.SynchronizeSafe(() =>
            {
                try
                {
                    OnMessageReceived(new MessageEventArgs(this, message));
                }
                catch (Exception ex)
                {
                    logger.Error($"Unhandled exception in #{Id} -> {this.GetType().Name}.OnMessageReceived: {ex}");
                }
            }, logger);
        }

        protected internal virtual void PollEventsInternal()
        {
            if (!closed && Parent.Configuration.KeepAliveEnabled)
            {
                TimeSpan timeSinceLastKeepAlive = DateTime.UtcNow - this.lastKeepAliveSent;

                if (keepAliveResponseGot)
                {
                    if (timeSinceLastKeepAlive.TotalMilliseconds > Parent.Configuration.KeepAliveInterval)
                    {
                        keepAliveResponseGot = false;
                        this.lastKeepAliveSent = DateTime.UtcNow;
                        _ = SendMessageAsync(TcpRawMessage.GetEmpty(Parent.Configuration.MemoryStreamPool, MessageHeaderFlags.KeepAliveRequest ));
                    }
                }
                else
                {
                    if (timeSinceLastKeepAlive.TotalMilliseconds > TcpConnectionStatistics.UNSTABLE_CONNECTION_TIMEOUT)
                    {
                        Statistics.SetUnstable();
                    }

                    if (Parent.Configuration.KeepAliveTimeout > Timeout.Infinite && timeSinceLastKeepAlive.TotalMilliseconds > Parent.Configuration.KeepAliveTimeout)
                    {
                        logger.Debug($"Connection #{Id} closing, KeepAliveTimeout exceeded");
                        Close();
                    }
                }
            }

            Statistics.PollEvents();
            
            while (latencySimulationSendQueue.Count > 0 && Connected)
            {
                if (latencySimulationSendQueue.TryPeek(out DelayedMessage msg))
                {
                    if (DateTime.UtcNow > msg.releaseTimestamp)
                    {
                        latencySimulationSendQueue.TryDequeue(out DelayedMessage _msg);
                        SendMessageSkipSimulationAsync(msg.message).ContinueWith(msg.Complete);
                    }
                    else
                        break;
                }
                else
                    break;
            }

            while (latencySimulationRecvQueue.Count > 0 && Connected)
            {
                if (latencySimulationRecvQueue.TryPeek(out DelayedMessage msg))
                {
                    if (DateTime.UtcNow > msg.releaseTimestamp)
                    {
                        latencySimulationRecvQueue.TryDequeue(out DelayedMessage _msg);
                        OnMessageReceivedInternal(msg.message);
                    }
                    else
                        break;
                }
                else
                    break;
            }
        }

        protected virtual void OnMessageReceived(MessageEventArgs args)
        {

        }

        //public void SendMessage(RawMessage message)
        //{
        //    SendMessageAsync(message);
        //}

        public virtual async Task SendMessageAsync(TcpRawMessage message)
        {
            if (Parent.Configuration.ConnectionSimulation != null)
            {
                int delay = Parent.Configuration.ConnectionSimulation.GetHalfDelay();
                var delayedMessage = new DelayedMessage()
                {
                    message = message,
                    releaseTimestamp = DateTime.UtcNow.AddMilliseconds(delay)
                };
                latencySimulationSendQueue.Enqueue(delayedMessage);
                await delayedMessage.GetTask();
                return;
            }

            await SendMessageSkipSimulationAsync(message);
        }

        async Task SendMessageSkipSimulationAsync(TcpRawMessage message)
        {
            if (!Connected)
            {
                message.Dispose();
                throw new InvalidOperationException($"Connection is not established");
            }
            
            Task newSendTask = null;
            lock (sendMutex)
            {
                sendTask = sendTask.ContinueWith(
                        (task, msg) =>
                        {
                            return SendMessageInternalAsync(msg as TcpRawMessage);
                        }, message, TaskContinuationOptions.ExecuteSynchronously)
                    .Unwrap();

                newSendTask = sendTask;
            }

            await newSendTask.ConfigureAwait(false);
        }

        async Task SendMessageInternalAsync(TcpRawMessage message)
        {
            try
            {
                var socket = this.socket;
                if (!Connected)
                    return; // throw new OperationCanceledException("Send operation cancelled. Connection not established");

                await sendSemaphore.WaitAsync().ConfigureAwait(false);

                if (message.Flags.HasFlag(MessageHeaderFlags.KeepAliveRequest) || 
                        message.Flags.HasFlag(MessageHeaderFlags.KeepAliveResponse))
                    logger.Trace($"Connection #{Id} sending {message}");
                else
                    logger.Debug($"Connection #{Id} sending {message}");

                TcpRawMessageHeader header = new TcpRawMessageHeader(message);

                var headerBytes = header.Build();
                Buffer.BlockCopy(headerBytes.Array, headerBytes.Offset, sendBuffer, 0, headerBytes.Count);
                int bufferPosition = headerBytes.Count;
                int totalBytesSent = 0;
                bool messageEof = false;

                message.Position = 0;

                do
                {
                    int bufferFreeSpace = sendBuffer.Length - bufferPosition;
                    int messageLeftBytes = (int)(message.BaseStream.Length - message.BaseStream.Position);
                    int toCopy = bufferFreeSpace;
                    if (messageLeftBytes <= toCopy)
                    {
                        toCopy = messageLeftBytes;
                        messageEof = true;
                    }
                    message.BaseStream.Read(sendBuffer, bufferPosition, toCopy);
                    bufferPosition += toCopy;

                    int bufferSendPosition = 0;

                    while (bufferSendPosition < bufferPosition)
                    {
                        int sent =  await Task.Factory.FromAsync(socket.BeginSend(sendBuffer, bufferSendPosition, bufferPosition-bufferSendPosition, SocketFlags.None, null, null), socket.EndSend)
                                .ConfigureAwait(false);
                        logger.Trace($"Connection #{Id} sent {sent} bytes");
                        bufferSendPosition += sent;
                        totalBytesSent += sent;
                    }

                    bufferPosition = 0;

                } while (!messageEof);

                Statistics.PacketOut();
                Statistics.BytesOut(totalBytesSent);
            }
            catch(SocketException)
            {
                Close();
            }
            catch (ObjectDisposedException)
            {
                Close();
            }
            catch (Exception ex)
            {
                logger.Error($"Exception in #{Id} on {message} sending: {ex}");
                Close();
            }
            finally
            {
                sendSemaphore.Release();
                message.Dispose();
            }
        }

        public override string ToString()
        {
            return $"{nameof(TcpConnection)}[id={Id}, connected={Connected}, endpoint={RemoteEndpoint}]";
        }
    }
}
