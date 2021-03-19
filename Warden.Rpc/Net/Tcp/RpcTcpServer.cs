using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Warden.Logging;
using Warden.Networking.Cryptography;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Rpc.Net.Events;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpServer : IRpcPeer
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

            protected override void PollEvents()
            {
                base.PollEvents();
                parent.PollEvents();
            }
        }

        public event DOnSessionOpened OnSessionOpenedEvent;
        public event DOnSessionClosed OnSessionClosedEvent;
        public RpcTcpConfigurationServer Configuration => configuration;

        readonly InnerTcpServer innerTcpServer;
        readonly RpcTcpConfigurationServer configuration;
        readonly ConcurrentStack<RpcTcpConnection> stashedConnections;
        protected readonly ILogger logger;

        public RpcTcpServer(RpcTcpConfigurationServer configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            configuration.Lock();
            this.configuration = configuration;
            this.innerTcpServer = new InnerTcpServer(this, configuration);
            this.stashedConnections = new ConcurrentStack<RpcTcpConnection>();
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

        protected virtual void PollEvents()
        {
            
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

        T CreateConnectionInternal<T>(Func<RpcTcpConnection> generator) where T: RpcTcpConnection
        {
            if (stashedConnections.TryPop(out RpcTcpConnection connection))
            {
                return connection as T;
            }
            return generator() as T;
        }

        internal virtual RpcTcpConnection CreateConnection()
        {
            RpcTcpConnection connection = null;
            if (configuration.IsCipherSet)
            {
                RpcTcpConnectionEncrypted encryptedConnection =
                    CreateConnectionInternal<RpcTcpConnectionEncrypted>(() =>
                        new RpcTcpConnectionEncrypted(innerTcpServer, this, configuration));
                encryptedConnection.SetCipher(configuration.CreateNewCipher());
                connection = encryptedConnection;
            }
            else
            {
                connection =
                    CreateConnectionInternal<RpcTcpConnection>(() =>
                        new RpcTcpConnection(innerTcpServer, this, configuration));
            }

            return connection;
        }

        
        internal void OnConnectionClosedInternal(ConnectionClosedEventArgs args)
        {
            
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

        protected virtual void OnSessionOpened(SessionOpenedEventArgs args)
        {

        }

        void IRpcPeer.OnSessionOpened(SessionOpenedEventArgs args)
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
        
        void IRpcPeer.OnSessionClosed(SessionClosedEventArgs args)
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
