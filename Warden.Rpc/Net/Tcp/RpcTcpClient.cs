using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Cryptography;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Rpc.Net.Tcp.Events;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpClient : IRpcPeer
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
        public TcpConnection Connection => innerTcpClient?.Connection;
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
            configuration.Lock();
            this.configuration = configuration;
            this.innerTcpClient = new InnerTcpClient(this, configuration);
            this.logger = configuration.LogManager.GetLogger(nameof(RpcTcpClient));
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
            try
            {

                await innerTcpClient.ConnectAsync(host, port)
                    .ConfigureAwait(false);
                var openedArgs = await tcsSessionOpened.Task
                    .ConfigureAwait(false);
                await Authenticate(openedArgs).ConfigureAwait(false);

                OnSessionStarted(openedArgs);
            }
            catch (Exception ex)
            {
                logger.Error($"Exception on establishing session: {ex}");
                this.CloseSession();
                throw;
            }
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
                await Authenticate(openedArgs).ConfigureAwait(false);;
                
                OnSessionStarted(openedArgs);
            }
            catch (Exception ex)
            {
                this.CloseSession();
                logger.Error($"Exception on establishing session: {ex}");
                throw;
            }
        }

        void OnSessionStarted(SessionOpenedEventArgs args)
        {
            this.Session = args.Session;
            
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

        protected virtual Task Authenticate(SessionOpenedEventArgs args)
        {
            return Task.CompletedTask;
        }

        public void CloseSession()
        {
            innerTcpClient.Disconnect();
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
                ICipher cipher = configuration.CreateNewCipher();
                RpcTcpConnectionEncrypted encryptedConnection =
                    new RpcTcpConnectionEncrypted(innerTcpClient, this, configuration);
                encryptedConnection.SetCipher(cipher);
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
        
        void IRpcPeer.OnSessionClosed(SessionClosedEventArgs args)
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