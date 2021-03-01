using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Rpc.Net.Tcp.Events;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpClient : IRpcPeerEventListener
    {
        internal class InnerTcpClient : TcpClient
        {
            RpcTcpClient parent;

            public InnerTcpClient(RpcTcpClient parent, RpcTcpConfigurationClient configuration) : base(configuration.TcpConfiguration)
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
        }
        
        public RpcTcpConfigurationClient Configuration => configuration;
        public RpcSession Session { get; private set; }
        public bool Ready => Session != null;
        public event DOnSessionOpened OnSessionOpenedEvent;
        public event DOnSessionClosed OnSessionClosedEvent;
        
        readonly InnerTcpClient innerTcpClient;
        readonly RpcTcpConfigurationClient configuration;
        protected readonly ILogger logger;
        
        TaskCompletionSource<SessionOpenedEventArgs> tcsSessionOpened;

        public RpcTcpClient(RpcTcpConfigurationClient configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            if (configuration.SessionFactory == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            configuration.Lock();
            this.configuration = configuration;
            this.innerTcpClient = new InnerTcpClient(this, configuration);
            this.logger = configuration.LogManager.GetLogger(nameof(RpcTcpServer));
            this.logger.Meta["kind"] = this.GetType().Name;
        }
        
        public void Start()
        {
            innerTcpClient.Start();
        }
        
        public void Shutdown()
        {
            innerTcpClient.Shutdown();
        }

        public virtual async Task StartSession(string host, int port)
        {
            if (Session != null)
                throw new InvalidOperationException("Session already started");
            
            this.tcsSessionOpened = new TaskCompletionSource<SessionOpenedEventArgs>();
            
            await innerTcpClient.ConnectAsync(host, port)
                .ConfigureAwait(false);
            await tcsSessionOpened.Task
                .ConfigureAwait(false);
        }

        public virtual async Task StartSession(IPEndPoint endpoint)
        {
            if (Session != null)
                throw new InvalidOperationException("Session already started");
            
            this.tcsSessionOpened = new TaskCompletionSource<SessionOpenedEventArgs>();

            try
            {
                await innerTcpClient.ConnectAsync(endpoint)
                    .ConfigureAwait(false);
                var openedArgs = await tcsSessionOpened.Task
                    .ConfigureAwait(false);
                await Authenticate(openedArgs);

                this.Session = openedArgs.Session;

                try
                {
                    OnSessionOpened(openedArgs);
                }
                catch (Exception e)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnSessionOpened)}: {e}");
                }

                try
                {
                    OnSessionOpenedEvent?.Invoke(openedArgs);
                }
                catch (Exception e)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnSessionOpenedEvent)}: {e}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Exception on establishing session: {ex}");
                this.CloseSession();
            }
        }

        protected virtual Task Authenticate(SessionOpenedEventArgs args)
        {
            return Task.CompletedTask;
        }

        public void CloseSession()
        {
            innerTcpClient.Disconnect();
        }

        protected internal virtual RpcTcpConnection CreateConnection()
        {
            RpcTcpConnection connection = null;
            if (configuration.Cipher != null)
            {
                connection =
                    new RpcTcpConnectionEncrypted(innerTcpClient, configuration.Cipher, this, configuration);
            }
            else
            {
                connection = new RpcTcpConnection(innerTcpClient, this, configuration);
            }

            connection.RpcInit();
            return connection;
        }

        internal void OnConnectionClosedInternal(ConnectionClosedEventArgs args)
        {
            tcsSessionOpened?.TrySetException(new InvalidOperationException("Connection closed prematurely"));
        }

        void OnSessionOpened(SessionOpenedEventArgs args)
        {
            
        }
        
        void IRpcPeerEventListener.OnSessionOpened(SessionOpenedEventArgs args)
        {
            tcsSessionOpened?.TrySetResult(args);
        }

        void OnSessionClosed(SessionClosedEventArgs args)
        {
            
        }
        
        void IRpcPeerEventListener.OnSessionClosed(SessionClosedEventArgs args)
        {
            this.Session = null;
            
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
        }
    }
}