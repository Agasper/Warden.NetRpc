using System;
using System.Net;
using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpServer
    {
        class InnerUdpServer : UdpServer
        {
            RpcUdpServerConfiguration configuration;

            public InnerUdpServer(RpcUdpServerConfiguration configuration) : base(configuration)
            {
                this.configuration = configuration;
            }
            
            protected override UdpConnection CreateConnection()
            {
                var connection = new RpcUdpConnection(this, configuration);
                connection.CreateSession();
                return connection;
            }
        }

        public RpcUdpServerConfiguration Configuration => configuration;

        InnerUdpServer innerUdpServer;
        RpcUdpServerConfiguration configuration;

        public RpcUdpServer(RpcUdpServerConfiguration configuration)
        {
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            if (configuration.SessionFactory == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            this.configuration = configuration;
            this.innerUdpServer = new InnerUdpServer(configuration);
        }

        public void Start()
        {
            this.innerUdpServer.Start();
        }

        public void Shutdown()
        {
            this.innerUdpServer.Shutdown();
        }

        public void Listen(int port)
        {
            this.innerUdpServer.Listen(port);
        }

        public void Listen(IPEndPoint endpoint)
        {
            this.innerUdpServer.Listen(endpoint);
        }

        public void Listen(string ip, int port)
        {
            this.innerUdpServer.Listen(ip, port);
        }
    }
}
