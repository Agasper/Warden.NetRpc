using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Warden.Logging;
using Warden.Networking.Udp;
using Warden.Networking.Udp.Events;
using Warden.Rpc.Net.Events;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpServer : IRpcPeer
    {
        internal class InnerUdpServer : UdpServer
        {
            RpcUdpServer parent;

            public InnerUdpServer(RpcUdpServer parent, RpcUdpConfigurationServer configuration) : base(configuration.UdpConfiguration)
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

        public event DOnSessionOpened OnSessionOpenedEvent;
        public event DOnSessionClosed OnSessionClosedEvent;
        public RpcUdpConfigurationServer Configuration => configuration;

        readonly InnerUdpServer innerUdpServer;
        readonly RpcUdpConfigurationServer configuration;
        protected readonly ILogger logger;

        public RpcUdpServer(RpcUdpConfigurationServer configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            configuration.Lock();
            this.configuration = configuration;
            this.innerUdpServer = new InnerUdpServer(this, configuration);
            this.logger = configuration.LogManager.GetLogger(nameof(RpcUdpServer));
            this.logger.Meta["kind"] = this.GetType().Name;
        }

        public int SessionsCount => innerUdpServer.Connections.Count;

        public IEnumerable<RpcSession> Sessions
        {
            get
            {
                foreach (var connection in innerUdpServer.Connections.Values)
                {
                    var session = (connection as RpcUdpConnection)?.Session;
                    if (session != null)
                        yield return session;
                }
            }
        }

        public void Start()
        {
            innerUdpServer.Start();
        }
        
        public void Shutdown()
        {
            innerUdpServer.Shutdown();
        }
        
        public void Listen(int port)
        {
            innerUdpServer.Listen(port);
        }

        public void Listen(string host, int port)
        {
            innerUdpServer.Listen(host, port);
        }

        public void Listen(IPEndPoint endPoint)
        {
            innerUdpServer.Listen(endPoint);
        }

        internal virtual RpcUdpConnection CreateConnection()
        {
            RpcUdpConnection connection = null;
            if (configuration.IsCipherSet)
            {
                connection = new RpcUdpConnectionEncrypted(innerUdpServer, configuration.CreateNewCipher(),this, configuration);
            }
            else
            {
                connection =
                    new RpcUdpConnection(innerUdpServer, this, configuration);
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
