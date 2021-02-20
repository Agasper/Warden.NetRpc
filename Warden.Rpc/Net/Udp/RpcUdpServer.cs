using System;
using System.Net;
using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpServer
    {
        class UdpServerOverride : UdpServer
        {
            RpcUdpServerConfiguration configuration;

            public UdpServerOverride(RpcUdpServerConfiguration configuration) : base(configuration)
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

        UdpServerOverride udpServer;
        RpcUdpServerConfiguration configuration;

        public RpcUdpServer(RpcUdpServerConfiguration configuration)
        {
            if (configuration.Serializer == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            if (configuration.SessionFactory == null)
                throw new ArgumentNullException(nameof(configuration.Serializer));
            this.configuration = configuration;
            this.udpServer = new UdpServerOverride(configuration);
        }

        public void Start()
        {
            this.udpServer.Start();
        }

        public void Shutdown()
        {
            this.udpServer.Shutdown();
        }

        public void Listen(int port)
        {
            this.udpServer.Listen(port);
        }

        public void Listen(IPEndPoint endpoint)
        {
            this.udpServer.Listen(endpoint);
        }

        public void Listen(string ip, int port)
        {
            this.udpServer.Listen(ip, port);
        }
    }
}
