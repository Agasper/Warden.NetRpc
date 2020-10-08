using System;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp.Channels
{
    struct PendingPacket
    {
        public delegate void DSendDatagram(Datagram datagram);

        public Datagram Datagram { get; private set; }
        public int ReSendNum { get; private set; }
        public DateTime Timestamp { get; private set; }
        public bool AckReceived { get; private set; }

        public void Init(Datagram datagram)
        {
            this.Timestamp = DateTime.UtcNow;
            this.Datagram = datagram;
            this.Datagram.DontDisposeOnSend = true;
            this.ReSendNum = 0;
            this.AckReceived = false;
        }

        public int GetDelay()
        {
            return (int)(DateTime.UtcNow - Timestamp).TotalMilliseconds;
        }

        public bool GotAck()
        {
            if (Datagram == null)
                return false;
            this.AckReceived = true;
            return true;
        }

        public void Clear()
        {
            var datagram = this.Datagram;
            this.Datagram = null;
            if (datagram != null)
            {
                datagram.Dispose();
            }
            this.AckReceived = false;
        }

        public bool TryReSend(DSendDatagram action, int resendDelay, bool multiplyOnSendNum)
        {
            var datagram = Datagram;
            if (datagram == null)
                return false;
            if (this.AckReceived)
                return false;

            int actualDelay = resendDelay;
            if (multiplyOnSendNum)
                actualDelay *= (ReSendNum + 1);
            if ((DateTime.UtcNow - Timestamp).TotalMilliseconds < actualDelay)
                return false;

            ReSendNum++;
            Timestamp = DateTime.UtcNow;
            action(datagram);
            return true;
        }
    }
}
