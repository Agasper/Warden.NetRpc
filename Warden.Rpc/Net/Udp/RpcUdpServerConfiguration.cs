using System;
using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpServerConfiguration : UdpServerConfiguration
    {
        public ISessionFactory SessionFactory { get => sessionFactory; set { CheckLocked(); CheckNull(value); sessionFactory = value; } }

        ISessionFactory sessionFactory;

        public RpcUdpServerConfiguration()
        {
        }
    }
}
