using System;
using Warden.Networking.Udp.Channels;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp
{
	public partial class UdpConnection
	{

        void OnPing(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;

            SendPong(datagram);
            datagram.Dispose();
        }

        void SendPing()
        {
            lastPingSequence = (ushort)((lastPingSequence + 1) % ChannelBase.MAX_SEQUENCE);
            var pingDatagram = Datagram.CreateEmpty(peer.Configuration.MemoryStreamPool);
            pingDatagram.Type = MessageType.Ping;
            pingDatagram.Sequence = lastPingSequence;
            lastPingSent = DateTime.UtcNow;
            SendDatagramAsync(pingDatagram);
        }

        void OnPong(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;

            int relate = ChannelBase.RelativeSequenceNumber(datagram.Sequence, lastPingSequence);
            if (relate != 0)
            {
                logger.Debug($"Got wrong pong, relate: {relate}");
                datagram.Dispose();
                return;
            }

            float latency = (float)(DateTime.UtcNow - lastPingSent).TotalMilliseconds;
            UpdateLatency(latency);

            datagram.Dispose();
        }


        void IChannelConnection.UpdateLatency(int latency)
        {
            UpdateLatency(latency);
        }

        void UpdateLatency(float latency)
        {
            UpdateTimeoutDeadline();
            this.latency = latency;
            if (avgLatency.HasValue)
                avgLatency = (avgLatency * 0.7f) + (latency * 0.3f);
            else
                avgLatency = latency;

            this.Statistics.UpdateLatency(latency, avgLatency.Value);
            logger.Trace($"Updated latency {latency}, avg {avgLatency}");
        }

        void SendPong(Datagram ping)
        {
            var pongDatagram = CreateSpecialDatagram(MessageType.Pong);
            pongDatagram.Type = MessageType.Pong;
            pongDatagram.Sequence = ping.Sequence;
            SendDatagramAsync(pongDatagram);
        }


	}
}
