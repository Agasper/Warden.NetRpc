using System;
using System.Net.Sockets;
using Warden.Logging;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Util.Polling;

namespace Warden.Networking.Tcp
{
    public delegate void DOnConnectionClosed(ConnectionClosedEventArgs args);
    public delegate void DOnConnectionOpened(ConnectionOpenedEventArgs args);

    public abstract class TcpPeer
    {
        public event DOnConnectionClosed OnConnectionClosedEvent;
        public event DOnConnectionOpened OnConnectionOpenedEvent;

        public object Tag { get; set; }
        public TcpConfigurationPeer Configuration => configuration;
        public bool IsStarted => poller.IsStarted;

        private protected readonly TcpConfigurationPeer configuration;
        private protected abstract ILogger Logger { get; }
        readonly Poller poller;

        public TcpPeer(TcpConfigurationPeer configuration)
        {
            configuration.Lock();
            this.configuration = configuration;
            this.poller = new Poller(5);
            this.poller.Delegate = PollEventsInternal_;
        }

        public virtual void Start()
        {
            this.poller.StartPolling();
        }

        public virtual void Shutdown()
        {
            this.poller.StopPolling(false);
        }

        protected void CheckStarted()
        {
            if (!IsStarted)
                throw new InvalidOperationException("Please call Start() first");
        }

        protected virtual TcpConnection CreateConnection()
        {
            return new TcpConnection(this);
        }

        protected void SetSocketOptions(Socket socket)
        {
            socket.ReceiveBufferSize = configuration.BufferSize;
            socket.SendBufferSize = configuration.BufferSize;
            socket.NoDelay = configuration.NoDelay;
            socket.Blocking = false;
            if (configuration.LingerOption != null)
                socket.LingerState = configuration.LingerOption;
        }

        protected Socket GetNewSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            if (configuration.ReuseAddress)
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Blocking = false;
            return socket;
        }

        public TcpRawMessage CreateMessage()
        {
            return new TcpRawMessage(configuration.MemoryStreamPool);
        }

        public TcpRawMessage CreateMessage(ArraySegment<byte> segment)
        {
            return new TcpRawMessage(configuration.MemoryStreamPool, segment, true);
        }

        public TcpRawMessage CreateMessage(int length)
        {
            return new TcpRawMessage(configuration.MemoryStreamPool, length, false);
        }

        internal virtual void OnConnectionClosedInternal(TcpConnection tcpConnection)
        {
            Logger.Debug($"Connection {tcpConnection} closed!");
            
            configuration.SynchronizeSafe(() =>
            {
                var args = new ConnectionClosedEventArgs(tcpConnection);
                try
                {
                    args.Connection.OnConnectionClosed(args);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unhandled exception on {args.Connection.GetType().Name}.{nameof(OnConnectionClosed)}: {ex}");
                }

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

                if (!tcpConnection.Stashed && !tcpConnection.Disposed)
                    tcpConnection.Dispose();


            }, this.Logger );
        }

        internal void OnConnectionOpenedInternal(TcpConnection tcpConnection)
        {
            Logger.Debug($"Connection {tcpConnection} opened!");

            configuration.SynchronizeSafe(() =>
            {
                ConnectionOpenedEventArgs args = new ConnectionOpenedEventArgs(tcpConnection);
                try
                {
                    args.Connection.OnConnectionOpened(args);
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        $"Unhandled exception on {args.Connection.GetType().Name}.{nameof(OnConnectionOpened)}: {ex}");
                }

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
                    Logger.Error(
                        $"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionOpenedEvent)}: {ex}");
                }
            }, this.Logger);
        }
        
        protected virtual void OnConnectionClosed(ConnectionClosedEventArgs args)
        {

        }

        protected virtual void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {

        }

        void PollEventsInternal_()
        {
            try
            {
                PollEventsInternal();
            }
            catch(Exception ex)
            {
                Logger.Error("Exception in polling thread: " + ex.ToString());
                this.Shutdown();
            }
        }

        protected virtual void PollEventsInternal()
        {
            
        }
    }
}
