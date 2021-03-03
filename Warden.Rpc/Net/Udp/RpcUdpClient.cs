using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Cryptography;
using Warden.Networking.Udp;
using Warden.Networking.Udp.Events;
using Warden.Rpc.Net.Events;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpClient : IRpcPeer
    {
        internal class InnerUdpClient : UdpClient
        {
            RpcUdpClient parent;

            public InnerUdpClient(RpcUdpClient parent, RpcUdpConfigurationClient configuration) : base(configuration.UdpConfiguration)
            {
                this.parent = parent;
            }
            
            protected override UdpConnection CreateConnection()
            {
                return parent.CreateConnection();
            }

            protected override void OnConnectionClosed(ConnectionClosedEventArgs args)
            {
                parent.OnConnectionClosedInternal(args);
                base.OnConnectionClosed(args);
            }
        }
        
        public RpcUdpConfigurationClient Configuration => configuration;
        public RpcSession Session { get; private set; }
        public UdpConnection Connection => innerUdpClient?.Connection;
        public bool Ready => Session != null;
        public event DOnSessionOpened OnSessionOpenedEvent;
        public event DOnSessionClosed OnSessionClosedEvent;
        
        readonly InnerUdpClient innerUdpClient;
        readonly RpcUdpConfigurationClient configuration;
        protected readonly ILogger logger;
        
        TaskCompletionSource<SessionOpenedEventArgs> tcsSessionOpened;

        public RpcUdpClient(RpcUdpConfigurationClient configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            configuration.Lock();
            this.configuration = configuration;
            this.innerUdpClient = new InnerUdpClient(this, configuration);
            this.logger = configuration.LogManager.GetLogger(nameof(RpcUdpClient));
            this.logger.Meta["kind"] = this.GetType().Name;
        }
        
        public void Start()
        {
            innerUdpClient.Start();
        }
        
        public void Shutdown()
        {
            innerUdpClient.Shutdown();
        }

        public virtual async Task StartSession(string host, int port)
        {
            if (Session != null)
                throw new InvalidOperationException("Session already started");
            
            this.tcsSessionOpened = new TaskCompletionSource<SessionOpenedEventArgs>();
            try
            {

                await innerUdpClient.ConnectAsync(host, port)
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
                await innerUdpClient.ConnectAsync(endpoint)
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
            innerUdpClient.Disconnect();
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

        protected internal virtual RpcUdpConnection CreateConnection()
        {
            RpcUdpConnection connection = null;
            
            if (configuration.IsCipherSet)
            {
                connection =
                    new RpcUdpConnectionEncrypted(innerUdpClient, configuration.CreateNewCipher(), this, configuration);
            }
            else
            {
                connection = new RpcUdpConnection(innerUdpClient, this, configuration);
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