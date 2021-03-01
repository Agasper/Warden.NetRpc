using System;
using System.Collections.Generic;
using System.Net;
using Warden.Logging;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Rpc.Net.Tcp.Events;

namespace Warden.Rpc.Net.Tcp
{
    public abstract class RpcTcpServer : IRpcPeerEventListener
    {
        internal class InnerTcpServer : TcpServer
        {
            RpcTcpServer parent;

            public InnerTcpServer(RpcTcpServer parent, RpcTcpConfigurationServer configuration) : base(configuration.TcpConfiguration)
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
                args.Connection.Stash();
            }
        }

        public event DOnSessionOpened OnSessionOpenedEvent;
        public event DOnSessionClosed OnSessionClosedEvent;
        public RpcTcpConfigurationServer Configuration => configuration;

        readonly InnerTcpServer innerTcpServer;
        readonly RpcTcpConfigurationServer configuration;
        protected readonly ILogger logger;

        public RpcTcpServer(RpcTcpConfigurationServer configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            if (configuration.SessionFactory == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            configuration.Lock();
            this.configuration = configuration;
            this.innerTcpServer = new InnerTcpServer(this, configuration);
            this.logger = configuration.LogManager.GetLogger(nameof(RpcTcpServer));
            this.logger.Meta["kind"] = this.GetType().Name;
        }

        public int SessionsCount => innerTcpServer.Connections.Count;

        public IEnumerable<RpcSession> Sessions
        {
            get
            {
                foreach (var connection in innerTcpServer.Connections.Values)
                {
                    var session = (connection as RpcTcpConnection)?.Session;
                    if (session != null)
                        yield return session;
                }
            }
        }

        public void Start()
        {
            innerTcpServer.Start();
        }
        
        public void Shutdown()
        {
            innerTcpServer.Shutdown();
        }
        
        public void Listen(int port)
        {
            innerTcpServer.Listen(port);
        }

        public void Listen(string host, int port)
        {
            innerTcpServer.Listen(host, port);
        }

        public void Listen(IPEndPoint endPoint)
        {
            innerTcpServer.Listen(endPoint);
        }

        internal virtual RpcTcpConnection CreateConnection()
        {
            RpcTcpConnection connection = null;
            if (configuration.Cipher != null)
            {
                connection =
                    new RpcTcpConnectionEncrypted(innerTcpServer, configuration.Cipher, this, configuration);
            }
            else
            {
                connection = new RpcTcpConnection(innerTcpServer, this, configuration);
            }
            
            connection.RpcInit();

            return connection;
        }

        
        internal void OnConnectionClosedInternal(ConnectionClosedEventArgs args)
        {
            
        }

        protected virtual void OnSessionOpened(SessionOpenedEventArgs args)
        {

        }

        void IRpcPeerEventListener.OnSessionOpened(SessionOpenedEventArgs args)
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

        }

        protected virtual void OnSessionClosed(SessionClosedEventArgs args)
        {
            
        }
        
        void IRpcPeerEventListener.OnSessionClosed(SessionClosedEventArgs args)
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
        }
    }
}
