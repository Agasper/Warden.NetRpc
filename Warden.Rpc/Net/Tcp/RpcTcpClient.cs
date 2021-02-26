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
        
        InnerTcpClient innerTcpClient;
        RpcTcpConfigurationClient configuration;
        ILogger logger;
        TaskCompletionSource<object> tcsSessionReady;

        public RpcTcpClient(RpcTcpConfigurationClient configuration)
        {
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

        public async Task StartSession(string host, int port)
        {
            this.tcsSessionReady = new TaskCompletionSource<object>();
            
            await innerTcpClient.ConnectAsync(host, port)
                .ConfigureAwait(false);
            await tcsSessionReady.Task
                .ConfigureAwait(false);
        }

        public async Task StartSession(IPEndPoint endpoint)
        {
            this.tcsSessionReady = new TaskCompletionSource<object>();
            
            await innerTcpClient.ConnectAsync(endpoint)
                .ConfigureAwait(false);
            await tcsSessionReady.Task
                .ConfigureAwait(false);
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
                RpcTcpConnectionEncrypted encryptedConnection =
                    new RpcTcpConnectionEncrypted(innerTcpClient, configuration.Cipher, this, configuration);
                encryptedConnection.SendHandshake();
                connection = encryptedConnection;
            }
            else
            {
                connection = new RpcTcpConnection(innerTcpClient, this, configuration);
                connection.CreateSession();
            }

            return connection;
        }

        internal void OnConnectionClosedInternal(ConnectionClosedEventArgs args)
        {
            tcsSessionReady?.TrySetException(new InvalidOperationException("Connection closed prematurely"));
        }

        void OnSessionOpened(SessionOpenedEventArgs args)
        {
            
        }
        
        void IRpcPeerEventListener.OnSessionOpened(SessionOpenedEventArgs args)
        {
            this.Session = args.Session;
            tcsSessionReady?.TrySetResult(null);
            
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