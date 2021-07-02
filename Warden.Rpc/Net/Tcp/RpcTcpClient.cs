using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Cryptography;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Rpc.Net.Events;

namespace Warden.Rpc.Net.Tcp
{
    public enum RpcClientStatus
    {
        Disconnected,
        Connecting,
        Ready
    }
    
    public class RpcTcpClient : IRpcPeer
    {
        internal class InnerTcpClient : TcpClient
        {
            public IPEndPoint LastEndpoint => base.lastEndpoint;
            RpcTcpClient parent;

            public InnerTcpClient(RpcTcpClient parent, RpcTcpConfigurationClient configuration) : base(configuration
                .TcpConfiguration)
            {
                this.parent = parent;
            }

            protected override TcpConnection CreateConnection()
            {
                return parent.CreateConnection();
            }

            protected override void OnConnectionClosed(ConnectionClosedEventArgs args)
            {
                parent.OnConnectionClosedInternal(args);
                base.OnConnectionClosed(args);
            }

            protected override void PollEvents()
            {
                parent.PollEventsInternal();
                base.PollEvents();
            }
        }

        public string Tag { get; set; }
        public RpcTcpConfigurationClient Configuration => configuration;
        public RpcSession Session { get; private set; }
        public TcpConnectionStatistics Statistics => innerTcpClient?.Connection?.Statistics;
        public RpcClientStatus Status { get; private set; }
        public event DOnSessionOpened OnSessionOpenedEvent;
        public event DOnSessionClosed OnSessionClosedEvent;
        public event DOnClientStatusChanged OnStatusChangedEvent;
        
        readonly InnerTcpClient innerTcpClient;
        readonly RpcTcpConfigurationClient configuration;
        protected readonly ILogger logger;
        
        bool canReconnect;
        DateTime reconnectTimerStartFrom;
        
        TaskCompletionSource<SessionOpenedEventArgs> tcsSessionOpened;

        public RpcTcpClient(RpcTcpConfigurationClient configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            configuration.Lock();
            this.configuration = configuration;
            this.innerTcpClient = new InnerTcpClient(this, configuration);
            this.logger = configuration.LogManager.GetLogger(nameof(RpcTcpClient));
            this.logger.Meta["kind"] = this.GetType().Name;
            this.logger.Meta["tag"] = new RefLogLabel<RpcTcpClient>(this, s => s.Tag);
        }
        
        public void Start()
        {
            innerTcpClient.Start();
        }
        
        public void Shutdown()
        {
            innerTcpClient.Shutdown();
        }

        public virtual async Task StartSessionAsync(string host, int port)
        {
            if (Status != RpcClientStatus.Disconnected)
                throw new InvalidOperationException($"Wrong status {Status}, expected {RpcClientStatus.Disconnected}");
            
            this.tcsSessionOpened = new TaskCompletionSource<SessionOpenedEventArgs>();
            try
            {
                logger.Trace($"Connecting to {host}:{port}");
                ChangeStatus(RpcClientStatus.Connecting);
                canReconnect = true;
                await innerTcpClient.ConnectAsync(host, port);
                logger.Trace($"Waiting for session...");
                var openedArgs = await tcsSessionOpened.Task;
                logger.Trace($"Waiting for auth...");
                await Authenticate(openedArgs);
                OnSessionStarted(openedArgs);
                ChangeStatus(RpcClientStatus.Ready);
            }
            catch (Exception ex)
            {
                logger.Error($"Exception on establishing session: {ex}");
                this.CloseSessionInternal();
                throw;
            }
        }

        public virtual async Task StartSessionAsync(IPEndPoint endpoint)
        {
            if (Status != RpcClientStatus.Disconnected)
                throw new InvalidOperationException($"Wrong status {Status}, expected {RpcClientStatus.Disconnected}");
            
            this.tcsSessionOpened = new TaskCompletionSource<SessionOpenedEventArgs>();

            try
            {
                logger.Trace($"Connecting to {endpoint}");
                ChangeStatus(RpcClientStatus.Connecting);
                await innerTcpClient.ConnectAsync(endpoint);
                logger.Trace($"Waiting for session...");
                var openedArgs = await tcsSessionOpened.Task;
                logger.Trace($"Waiting for auth...");
                await Authenticate(openedArgs);
                OnSessionStarted(openedArgs);
                ChangeStatus(RpcClientStatus.Ready);
                canReconnect = true;
            }
            catch (Exception ex)
            {
                logger.Error($"Exception on establishing session: {ex}");
                this.CloseSessionInternal();
                throw;
            }
        }

        void OnSessionStarted(SessionOpenedEventArgs args)
        {
            this.Session = args.Session;
            this.configuration.SynchronizeSafe(() =>
            {
                try
                {
                    OnSessionOpened(args);
                }
                catch (Exception e)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnSessionOpened)}: {e}");
                }

                try
                {
                    OnSessionOpenedEvent?.Invoke(args);
                }
                catch (Exception e)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnSessionOpenedEvent)}: {e}");
                }
            }, logger);
        }

        protected virtual Task Authenticate(SessionOpenedEventArgs args)
        {
            return Task.CompletedTask;
        }

        void CloseSessionInternal()
        {
            innerTcpClient.Disconnect();
            reconnectTimerStartFrom = DateTime.UtcNow;
            ChangeStatus(RpcClientStatus.Disconnected);
        }

        public void CloseSession(bool stopAutoReconnecting = true)
        {
            CloseSessionInternal();
            canReconnect = !stopAutoReconnecting;
        }

        RpcSession IRpcPeer.CreateSession(RpcSessionContext context)
        {
            return CreateSession(context);
        }

        protected virtual RpcSession CreateSession(RpcSessionContext context)
        {
            if (configuration.SessionFactory == null)
                throw new ArgumentNullException(nameof(configuration.SessionFactory));

            return configuration.SessionFactory.CreateSession(context);
        }

        protected internal virtual RpcTcpConnection CreateConnection()
        {
            RpcTcpConnection connection = null;
            
            if (configuration.IsCipherSet)
            {
                RpcTcpConnectionEncrypted encryptedConnection =
                    new RpcTcpConnectionEncrypted(innerTcpClient, this, configuration);
                encryptedConnection.SetCipher(configuration.CreateNewCipher());
                connection = encryptedConnection;
            }
            else
            {
                connection = new RpcTcpConnection(innerTcpClient, this, configuration);
            }
            return connection;
        }

        internal void OnConnectionClosedInternal(ConnectionClosedEventArgs args)
        {
            tcsSessionOpened?.TrySetException(new InvalidOperationException("Connection closed prematurely"));
        }

        protected virtual void OnSessionOpened(SessionOpenedEventArgs args)
        {
            
        }
        
        void IRpcPeer.OnSessionOpened(SessionOpenedEventArgs args)
        {
            tcsSessionOpened?.TrySetResult(args);
        }

        protected virtual void OnSessionClosed(SessionClosedEventArgs args)
        {
            
        }
        
        protected virtual void OnStatusChanged(RpcClientStatusChangedEventArgs args)
        {
            
        }

        void ChangeStatus(RpcClientStatus newStatus)
        {
            if (Status == newStatus)
                return;
            logger.Debug($"Changed status from {Status} to {newStatus}");
            RpcClientStatusChangedEventArgs args = new RpcClientStatusChangedEventArgs(this, Status, newStatus);
            Status = newStatus;
            
            try
            {
                OnStatusChanged(args);
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnStatusChanged)}: {e}");
            }
            
            try
            {
                OnStatusChangedEvent?.Invoke(args);
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnStatusChangedEvent)}: {e}");
            }
        }
        
        void IRpcPeer.OnSessionClosed(SessionClosedEventArgs args)
        {
            this.configuration.SynchronizeSafe(() =>
            {
                try
                {
                    OnSessionClosed(args);
                }
                catch (Exception e)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnSessionClosed)}: {e}");
                }

                try
                {
                    OnSessionClosedEvent?.Invoke(args);
                }
                catch (Exception e)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnSessionClosedEvent)}: {e}");
                }

                ChangeStatus(RpcClientStatus.Disconnected);
            }, logger);
            
            reconnectTimerStartFrom = DateTime.UtcNow;
            this.Session = null;
        }
        
        protected virtual bool OnReconnecting()
        {
            return true;
        }

        protected virtual void PollEvents()
        {
            
        }

        internal void PollEventsInternal()
        {
            if (Status == RpcClientStatus.Disconnected &&
                canReconnect &&
                configuration.AutoReconnect &&
                (DateTime.UtcNow - reconnectTimerStartFrom).TotalMilliseconds > configuration.AutoReconnectDelay)
            {
                if (!OnReconnecting())
                {
                    reconnectTimerStartFrom = DateTime.UtcNow;
                }
                else
                {
                    logger.Debug($"Reconnecting to {innerTcpClient.LastEndpoint}...");
                    _ = StartSessionAsync(innerTcpClient.LastEndpoint);
                }
            }

            PollEvents();
        }
    }
}