using System;
namespace Warden.Networking.Udp
{
    public enum DisconnectReason
    {
        Error,
        ClosedByThisPeer,
        ClosedByOtherPeer,
        Timeout
    }
}
