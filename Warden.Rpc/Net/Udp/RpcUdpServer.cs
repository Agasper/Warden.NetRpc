using System;
using System.Net;
using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpServer
    {
        class UdpServerOverride : UdpServer
        {
            public UdpServerOverride(UdpServerConfiguration configuration) : base(configuration) { }
            protected override UdpConnection CreateConnection()
            {
                return new RpcUdpConnection(this);
            }
        }

        UdpServerOverride udpServer;
        RpcUdpServerConfiguration configuration;

        public RpcUdpServer(RpcUdpServerConfiguration configuration)
        {
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
