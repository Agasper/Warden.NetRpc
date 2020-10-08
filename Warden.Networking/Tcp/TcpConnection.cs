using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public bool Stashed => stashed;
        public bool Disposed => disposed;
        public virtual object Tag { get; set; }
        public long Id { get; private set; }
        public EndPoint RemoteEndpoint
        {
            get
            {
                var socket_ = this.socket;
                if (socket_ == null)
                    return null;
                return socket_.RemoteEndPoint;
            }
        }
        public bool Connected
        {
            get
            {
                var socket_ = this.socket;
                if (socket_ == null)
                    return false;
                return socket_.Connected;
            }
        }
        public DateTime Started { get; private set; }
        public TcpPeer Parent { get; private set; }
        public CancellationToken CancellationToken => cancellationTokenSource != null ? cancellationTokenSource.Token : default;
        public TcpConnectionStatistics Statistics { get; private set; }

        protected DateTime? LastKeepAliveRequestReceived { get; private set; }

        Socket socket;
        TcpRawMessage awaitingNextMessage;
        bool awaitingNextMessageHeaderValid;
        int awaitingNextMessageWrote;
        DateTime lastKeepAliveSent;
        bool keepAliveResponseGot;
        SemaphoreSlim sendSemaphore;
        CancellationTokenSource cancellationTokenSource;

        volatile bool closing;
        volatile bool disposed;
        volatile bool stashed;
        volatile Task sendTask;
        readonly object sendMutex = new object();
        
        protected readonly ILogger logger;
        TcpRawMessageHeader awaitingMessageHeader;
        readonly byte[] recvBuffer;
        readonly byte[] sendBuffer;

        readonly ConcurrentQueue<DelayedMessage> latencySimulationRecvBag;

        struct DelayedMessage //for latency simulation
        {
            public TcpRawMessage message;
            public TcpRawMessageHeader header;
            public DateTime releaseTimestamp;
        }

        public TcpConnection(TcpPeer parent)
        {
            this.latencySimulationRecvBag = new ConcurrentQueue<DelayedMessage>();
            this.stashed = true;
            this.Statistics = new TcpConnectionStatistics();
            this.sendSemaphore = new SemaphoreSlim(1, 1);
            this.recvBuffer = new byte[parent.Configuration.BufferSize];
            this.sendBuffer = new byte[parent.Configuration.BufferSize];
            this.awaitingMessageHeader = new TcpRawMessageHeader();
            this.Parent = parent;
            this.logger = parent.Configuration.LogManager.GetLogger(nameof(TcpConnection));
            this.logger.Meta["kind"] = this.GetType().Name;
        }

        internal void CheckParent(TcpPeer parent)
        {
            if (this.Parent != parent)
                throw new InvalidOperationException($"This connection belongs to the another parent");
        }

        public virtual void Init(long connectionId, Socket socket)
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));
            if (!Stashed)
                throw new InvalidOperationException($"{nameof(TcpConnection)} is still being used (not stashed ot disposed)");

            this.stashed = false;

            this.logger.Meta["connection_id"] = connectionId;
            this.keepAliveResponseGot = true;
            this.Id = connectionId;
            this.cancellationTokenSource = new CancellationTokenSource();
            this.socket = socket;
            this.sendTask = Task.CompletedTask;
            this.closing = false;
            this.Started = DateTime.UtcNow;
            logger.Info($"Connection initialized {this}");
        }

        public virtual void Stash()
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(TcpConnection));
            if (Stashed)
                return;

            Close();
            DestroySocket();

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

            if (this.cancellationTokenSource != null)
            {
                this.cancellationTokenSource.Dispose();
                this.cancellationTokenSource = null;
            }

            while (this.latencySimulationRecvBag.TryDequeue(out DelayedMessage removed))
                removed.message.Dispose();

            stashed = true;
            logger.Debug($"{this} stashed!");
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
            DestroySocket();

            if (this.awaitingNextMessage != null)
            {
                this.awaitingNextMessage.Dispose();
                this.awaitingNextMessage = null;
            }

            if (this.cancellationTokenSource != null)
            {
                this.cancellationTokenSource.Dispose();
                this.cancellationTokenSource = null;
            }

            if (sendSemaphore != null)
            {
                sendSemaphore.Dispose();
                sendSemaphore = null;
            }

            logger.Debug($"{this} disposed!");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void DestroySocket()
        {
            var socket_ = socket;
            if (socket_ != null)
            {
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

        public void Close()
        {
            if (closing)
                return;
            closing = true;

            CloseInternal();
        }

        void CloseInternal()
        {
            try
            {
                logger.Debug($"{this} closing!");

                this.cancellationTokenSource.Cancel();

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
                        DestroySocket();
                    }
                }

                logger.Info($"{this} closed!");

                Parent.OnConnectionClosedInternal(this);
            }
            catch (Exception ex)
            {
                logger.Critical("Exception on connection close: " + ex.ToString());
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
                logger.Trace($"{this} recv data {bytesRead} bytes");

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
                            OnMessageReceivedInternalWithSimulation(awaitingMessageHeader, message);
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
                        logger.Critical($"Ininite loop in {this}");
                        throw new InvalidOperationException("Infinite loop");
                    }
                }

                if (socket != null)
                    StartReceive();

            }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                logger.Error($"{this} broken due to exception: {ex}");
                Close();
            }
        }

        void OnMessageReceivedInternalWithSimulation(TcpRawMessageHeader header, TcpRawMessage message)
        {
            if (Parent.Configuration.ConnectionSimulation != null)
            {
                int delay = Parent.Configuration.ConnectionSimulation.GetHalfDelay();
                latencySimulationRecvBag.Enqueue(new DelayedMessage()
                {
                    header = header,
                    message = message,
                    releaseTimestamp = DateTime.UtcNow.AddMilliseconds(delay)
                });
                return;
            }

            OnMessageReceivedInternal(header, message);
        }

        void OnMessageReceivedInternal(TcpRawMessageHeader header, TcpRawMessage message)
        {
            logger.Debug($"{this} recv message {message} with flags {header.Options.flags}");

            if (header.Options.flags.HasFlag(MessageHeaderFlags.KeepAliveRequest))
            {
                LastKeepAliveRequestReceived = DateTime.UtcNow;
                _ = SendMessageAsync(TcpRawMessage.GetEmpty(Parent.Configuration.MemoryStreamPool), new TcpRawMessageOptions() { flags = MessageHeaderFlags.KeepAliveResponse });
                message.Dispose();
                return;
            }

            if (header.Options.flags.HasFlag(MessageHeaderFlags.KeepAliveResponse))
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
                    logger.Error($"Unhandled exception in {this.GetType().Name}.OnMessageReceived: {ex}");
                }
            }, logger);
        }

        internal protected virtual void PollEventsInternal()
        {
            if (Parent.Configuration.KeepAliveEnabled)
            {
                TimeSpan timeSinceLastKeepAlive = DateTime.UtcNow - this.lastKeepAliveSent;

                if (keepAliveResponseGot)
                {
                    if (timeSinceLastKeepAlive.TotalMilliseconds > Parent.Configuration.KeepAliveInterval)
                    {
                        keepAliveResponseGot = false;
                        this.lastKeepAliveSent = DateTime.UtcNow;
                        _ = SendMessageAsync(TcpRawMessage.GetEmpty(Parent.Configuration.MemoryStreamPool), new TcpRawMessageOptions() { flags = MessageHeaderFlags.KeepAliveRequest });
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
                        logger.Debug($"{this} closing, KeepAliveTimeout exceded");
                        Close();
                    }
                }
            }

            Statistics.PollEvents();

            while (latencySimulationRecvBag.Count > 0 && !closing)
            {
                if (latencySimulationRecvBag.TryPeek(out DelayedMessage msg))
                {
                    if (DateTime.UtcNow > msg.releaseTimestamp)
                    {
                        latencySimulationRecvBag.TryDequeue(out DelayedMessage _msg);
                        OnMessageReceivedInternal(msg.header, msg.message);
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

        public Task SendMessageAsync(TcpRawMessage message)
        {
            return SendMessageAsync(message, TcpRawMessageOptions.None);
        }

        async Task SendMessageAsync(TcpRawMessage message, TcpRawMessageOptions options)
        {
            if (closing)
            {
                message.Dispose();
                throw new InvalidOperationException($"Connection is closed");
            }

            if (Parent.Configuration.ConnectionSimulation != null)
            {
                int delay = Parent.Configuration.ConnectionSimulation.GetHalfDelay();
                if (delay > 0)
                    await Task.Delay(delay).ConfigureAwait(false);
            }

            lock (sendMutex)
            {
                sendTask = sendTask.ContinueWith(
                    (task, tuple) =>
                {
                    if (this.CancellationToken.IsCancellationRequested)
                    {
                        ((SendTuple)tuple).Message.Dispose();
                        return Task.FromCanceled(this.CancellationToken);
                    }

                    return SendMessageInternalAsync((SendTuple)tuple);
                }, new SendTuple(message, options))
                        .Unwrap();
            }

            await sendTask.ConfigureAwait(false);
        }

        public Task FlushSendQueueAsync()
        {
            if (closing)
                throw new InvalidOperationException($"Connection is closing");

            return sendTask;
        }

        public Task FlushSendQueueAndCloseAsync()
        {
            if (closing)
                throw new InvalidOperationException($"Connection is closing");

            lock(sendMutex)
                sendTask = sendTask.ContinueWith((t) => Close(), this.CancellationToken);
            return sendTask;
        }

        async Task SendMessageInternalAsync(SendTuple sendTuple)
        {
            try
            {
                var socket = this.socket;
                if (closing || socket == null || !socket.Connected)
                    return;


                await sendSemaphore.WaitAsync().ConfigureAwait(false);

                TcpRawMessage message = sendTuple.Message;
                logger.Debug($"{this} sending {message} with options {sendTuple.Options}");

                TcpRawMessageHeader header = new TcpRawMessageHeader((int)message.BaseStream.Length, sendTuple.Options);

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
                        logger.Trace($"{this} sent {sent} bytes");
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
                logger.Error($"Exception on {sendTuple.Message} sending: {ex}");
                Close();
            }
            finally
            {
                sendSemaphore.Release();
                sendTuple.Message.Dispose();
            }
        }

        public override string ToString()
        {
            return $"{nameof(TcpConnection)}[id={Id}, connected={Connected}, endpoint={RemoteEndpoint}]";
        }
    }
}
