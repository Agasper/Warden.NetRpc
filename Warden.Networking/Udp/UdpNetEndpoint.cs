using System.Collections.Generic;
using System.Net;

namespace Warden.Networking.Udp
{
    public struct UdpNetEndpoint
    {
        public EndPoint EndPoint { get; private set; }
        public ushort ConnectionKey { get; private set; }

        public UdpNetEndpoint(EndPoint endPoint, ushort connectionId)
        {
            this.EndPoint = endPoint;
            this.ConnectionKey = connectionId;
        }

        public override string ToString()
        {
            string strEp = "null";
            var ep = EndPoint;
            if (ep != null)
                strEp = ep.ToString();
            return $"{nameof(UdpNetEndpoint)}[ep={strEp},id={ConnectionKey}]";
        }
    }

    public class UdpNetEndpointEqualityComparer : IEqualityComparer<UdpNetEndpoint>
    {
        public bool Equals(UdpNetEndpoint x, UdpNetEndpoint y)
        {
            return x.EndPoint == y.EndPoint && x.ConnectionKey == y.ConnectionKey;
        }

        public int GetHashCode(UdpNetEndpoint x)
        {
            return x.EndPoint.GetHashCode() ^ x.ConnectionKey;
        }
    }

}
