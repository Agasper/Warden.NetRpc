using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Udp.Events;
using Warden.Networking.Udp.Messages;
using Warden.Util.Polling;
using Warden.Util.Pooling;

namespace Warden.Networking.Udp
{
    public delegate void DOnConnectionClosed(ConnectionClosedEventArgs args);
    public delegate void DOnConnectionOpened(ConnectionOpenedEventArgs args);
    public delegate void DOnConnectionStatusChanged(ConnectionStatusChangedEventArgs args);

    public abstract class UdpPeer
    {
        public event DOnConnectionClosed OnConnectionClosedEvent;
        public event DOnConnectionOpened OnConnectionOpenedEvent;
        public event DOnConnectionStatusChanged OnConnectionStatusChangedEvent;

        struct DelayedDatagram //for latency simulation
        {
            public Datagram datagram;
            public UdpNetEndpoint endpoint;
            public DateTime releaseTimestamp;
        }


        public object Tag { get; set; }
        public bool IsStarted => poller.IsStarted;
        public UdpPeerConfiguration Configuration => configuration;

        private protected abstract ILogger Logger { get; }

        private protected bool IsBound => socket != null && socket.IsBound;

        UdpPeerConfiguration configuration;
        Poller poller;
        Socket socket;
        IPEndPoint lastBoundTo;
        GenericPool<SocketAsyncEventArgs> socketArgsPool;
        Random random;
        long connectionId;

        ConcurrentQueue<DelayedDatagram> latencySimulationRecvBag;

        public UdpPeer(UdpPeerConfiguration configuration)
        {
            this.socketArgsPool = new GenericPool<SocketAsyncEventArgs>(() =>
            {
                var arg = new SocketAsyncEventArgs();
                arg.SetBuffer(new byte[ushort.MaxValue], 0, ushort.MaxValue);
                arg.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                arg.Completed += IO_Complete;
                return arg;
            }, 1);

            this.configuration = configuration;
            this.random = new Random();
            this.poller = new Poller(5, PollEventsInternal);
            this.latencySimulationRecvBag = new ConcurrentQueue<DelayedDatagram>();
        }

        internal virtual void OnConnectionClosedInternalSynchronized(ConnectionClosedEventArgs args)
        {
            try
            {
                OnConnectionClosed(args);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception on {args.Connection.GetType().Name}.{nameof(OnConnectionClosed)}: {ex}");
            }

            try
            {
                OnConnectionClosedEvent?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception on {args.Connection.GetType().Name}.{nameof(OnConnectionClosedEvent)}: {ex}");
            }
        }

        internal virtual void OnConnectionOpenedInternalSynchronized(ConnectionOpenedEventArgs args)
        {
            try
            {
                OnConnectionOpened(args);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionOpened)}: {ex}");
            }

            try
            {
                OnConnectionOpenedEvent?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionOpenedEvent)}: {ex}");
            }
        }

        internal virtual void OnConnectionStatusChangedSynchronized(ConnectionStatusChangedEventArgs args)
        {
            try
            {
                OnConnectionStatusChanged(args);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionStatusChanged)}: {ex}");
            }

            try
            {
                OnConnectionStatusChangedEvent?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionStatusChangedEvent)}: {ex}");
            }
        }

        protected virtual void OnConnectionClosed(ConnectionClosedEventArgs args)
        {

        }

        protected virtual void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {

        }

        protected virtual void OnConnectionStatusChanged(ConnectionStatusChangedEventArgs args)
        {

        }

        internal long GetNextConnectionId()
        {
            return Interlocked.Increment(ref connectionId);
        }

        private void IO_Complete(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.LastOperation == SocketAsyncOperation.ReceiveFrom)
                    EndReceive(e);
                if (e.LastOperation == SocketAsyncOperation.SendTo)
                    EndSend(e);
            }
            finally
            {
                socketArgsPool.Return(e);
            }
        }

        public virtual void Start()
        {
            this.poller.StartPolling();
        }

        public virtual void Shutdown()
        {
            this.socketArgsPool.Clear(true);
            this.poller.StopPolling(true);
            DestroySocket();
        }

        protected internal virtual bool OnAcceptConnection(OnAcceptConnectionEventArgs args)
        {
            return true;
        }

        protected void CheckStarted()
        {
            if (!IsStarted)
                throw new InvalidOperationException("Please call Start() first");
        }

        public UdpRawMessage CreateMessage()
        {
            return new UdpRawMessage(configuration.MemoryStreamPool);
        }

        public UdpRawMessage CreateMessage(ArraySegment<byte> segment)
        {
            return new UdpRawMessage(configuration.MemoryStreamPool, segment, true);
        }

        public UdpRawMessage CreateMessage(int length)
        {
            return new UdpRawMessage(configuration.MemoryStreamPool, length, false);
        }

        protected virtual void PollEvents()
        {

        }

        private protected virtual void PollEventsInternal()
        {
            for (int i = 0; i < latencySimulationRecvBag.Count; i++)
            {
                if (latencySimulationRecvBag.TryDequeue(out DelayedDatagram d))
                {
                    if (DateTime.UtcNow > d.releaseTimestamp)
                        ActuallyOnDatagram(d.datagram, d.endpoint);
                    else
                        latencySimulationRecvBag.Enqueue(d);
                }
            }

            try
            {
                PollEvents();
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception in {this.GetType().Name}.{nameof(PollEvents)}: {ex}");
                this.Shutdown();
            }
        }

        protected virtual UdpConnection CreateConnection()
        {
            return new UdpConnection(this);
        }

        void Rebind()
        {
            if (lastBoundTo == null)
                throw new InvalidOperationException("Rebind failed: socket never have been bound");
            Bind(lastBoundTo);
        }

        protected void DestroySocket()
        {
            var socket_ = socket;
            if (socket_ != null)
            {
                socket_.Close();
                this.socket = null;
            }
        }

        protected virtual void Bind(int port)
        {
            Bind(null, port);
        }

        protected virtual void Bind(string host, int port)
        {
            IPEndPoint myEndpoint;
            if (string.IsNullOrEmpty(host))
                myEndpoint = new IPEndPoint(IPAddress.Any, port);
            else
                myEndpoint = new IPEndPoint(IPAddress.Parse(host), port);

            Bind(myEndpoint);
        }

        protected virtual void Bind(IPEndPoint endPoint)
        {
            CheckStarted();
            if (socket == null)
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            if (configuration.ReuseAddress)
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Blocking = false;
            //socket.ReceiveBufferSize = ushort.MaxValue;
            //socket.SendBufferSize = ushort.MaxValue;

            socket.Bind(endPoint);
            lastBoundTo = endPoint;

            try
            {
                const uint IOC_IN = 0x80000000;
                const uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            }
            catch
            {
                Logger.Debug("SIO_UDP_CONNRESET not supported on this platform");
                // ignore; SIO_UDP_CONNRESET not supported on this platform
            }

            //try
            //{
            //    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DontFragment, 1);
            //}
            //catch
            //{
            //    Logger.Debug("DONT_FRAGMENT not supported on this platform");
            //}

            for (int i = 0; i < configuration.NetworkReceiveThreads; i++)
                StartReceive();
        }

        void StartReceive()
        {
            try
            {
                if (this.socket == null)
                    return;
                SocketAsyncEventArgs arg = socketArgsPool.Pop();
                arg.SetBuffer(arg.Buffer, 0, arg.Buffer.Length);
                if (!socket.ReceiveFromAsync(arg))
                    IO_Complete(this, arg);
            }
            catch(ObjectDisposedException)
            {
                return;
            }
        }

        void EndReceive(SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError != SocketError.Success)
                {
                    switch (e.SocketError)
                    {
                        case SocketError.ConnectionReset:
                            // connection reset by peer, aka connection forcibly closed aka "ICMP port unreachable"
                            // we should shut down the connection; but m_senderRemote seemingly cannot be trusted, so which connection should we shut down?!
                            // So, what to do?
                            return;

                        case SocketError.NotConnected:
                            Logger.Debug($"Socket has been unbound. Rebinding");
                            // socket is unbound; try to rebind it (happens on mobile when process goes to sleep)
                            Rebind();
                            return;

                        case SocketError.OperationAborted:
                            //Socket was closed
                            return;

                        default:
                            Logger.Error("Socket error on receive: " + e.SocketError);
                            return;
                    }
                }

                if (configuration.ConnectionSimulation != null &&
                    random.NextDouble() < configuration.ConnectionSimulation.PacketLoss)
                {
                    Logger.Debug($"We got a datagram from {e.RemoteEndPoint}, but according to connection simulation rules we dropped it");
                    return;
                }

                ArraySegment<byte> segment = new ArraySegment<byte>(e.Buffer, 0, e.BytesTransferred);
                Datagram datagram = Datagram.CreateFromRaw(configuration.MemoryStreamPool, segment);
                var ep = new UdpNetEndpoint(e.RemoteEndPoint, datagram.ConnectionKey);


                if (configuration.ConnectionSimulation != null)
                {
                    int delay = configuration.ConnectionSimulation.GetHalfDelay();
                    if (delay > 0)
                    {
                        latencySimulationRecvBag.Enqueue(new DelayedDatagram()
                        {
                            releaseTimestamp = DateTime.UtcNow.AddMilliseconds(delay),
                            datagram = datagram,
                            endpoint = ep
                        });
                    }
                    else
                        ActuallyOnDatagram(datagram, ep);
                }
                else
                    ActuallyOnDatagram(datagram, ep);

            }
            catch(Exception ex)
            {
                Logger.Error($"Unhandled exception in EndReceive: {ex}");
            }
            finally
            {
                StartReceive();
            }
        }

        void ActuallyOnDatagram(Datagram datagram, UdpNetEndpoint remoteEndpoint)
        {
            Logger.Debug($"Received {datagram} from {remoteEndpoint}");
            OnDatagram(datagram, remoteEndpoint);
        }

        private protected abstract void OnDatagram(Datagram datagram, UdpNetEndpoint remoteEndpoint);

        internal async Task<SocketError> SendDatagramAsync(UdpConnection connection, Datagram datagram)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (datagram == null)
                throw new ArgumentNullException(nameof(datagram));
            if (datagram.IsDisposed)
            {
                Logger.Error("Got disposed datagram on Send. Perhaps race condition");
                return SocketError.InvalidArgument;
            }

            if (configuration.ConnectionSimulation != null)
            {
                if (random.NextDouble() < configuration.ConnectionSimulation.PacketLoss)
                {
                    Logger.Debug($"We're sending datadram to {connection.EndPoint.EndPoint}, but according to connection simulation rules we dropped it");
                    return SocketError.Success;
                }

                int delay = configuration.ConnectionSimulation.GetHalfDelay();
                if (delay > 0)
                    await Task.Delay(delay).ConfigureAwait(false);
            }

            SendDatagramAsyncResult operationInfo = new SendDatagramAsyncResult(connection, datagram);
            SocketAsyncEventArgs arg = socketArgsPool.Pop();
            int bytes = datagram.WriteTo(new ArraySegment<byte>(arg.Buffer, 0, arg.Buffer.Length));
            arg.SetBuffer(arg.Buffer, 0, bytes);
            arg.RemoteEndPoint = connection.EndPoint.EndPoint;
            arg.UserToken = operationInfo;
            Logger.Debug($"Sending {datagram} to {arg.RemoteEndPoint}");
            if (!socket.SendToAsync(arg))
                IO_Complete(this, arg);
            return await operationInfo.Task.ConfigureAwait(false);
        }

        void EndSend(SocketAsyncEventArgs e)
        {
            try
            {
                SendDatagramAsyncResult opInfo = (SendDatagramAsyncResult)e.UserToken;
                if (e.SocketError != SocketError.Success)
                {
                    switch (e.SocketError)
                    {
                        case SocketError.WouldBlock:
                            // send buffer full?
                            //    LogWarning("Socket threw exception; would block - send buffer full? Increase in NetPeerConfiguration");
                            Logger.Error("Send error SocketError.WouldBlock: probably buffer is full");
                            break;

                        case SocketError.ConnectionReset:
                            Logger.Debug($"Remote peer responded with connection reset");
                            (e.UserToken as UdpConnection).CloseImmidiately(DisconnectReason.ClosedByOtherPeer);
                            // connection reset by peer, aka connection forcibly closed aka "ICMP port unreachable" 
                            break;
                        default:
                            Logger.Error($"Sending {opInfo.Datagram} failed: {e.SocketError}");
                            break;
                    }
                }

                opInfo.SetComplete(e.SocketError);
                if (!opInfo.Datagram.DontDisposeOnSend)
                    opInfo.Datagram.Dispose();
            }
            finally
            {
                e.UserToken = null;
            }
        }
    }
}
