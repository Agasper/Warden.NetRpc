using System;
using System.Threading.Tasks;
using Warden.Networking.IO;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp
{
	public partial class UdpConnection
	{
        const int INITIAL_MTU = 508;

        enum MtuExpansionStatus
        {
            NotStarted = 0,
            Started = 1,
            Finished = 2
        }

        int mtuFailedAttempts = -1;
        int smallestFailedMtu = -1;
        MtuExpansionStatus mtuStatus;
        DateTime lastMtuExpandSent;

        public void ExpandMTU()
        {
            if (mtuStatus == MtuExpansionStatus.NotStarted)
                mtuStatus = MtuExpansionStatus.Started;
        }


        void OnMtuSuccess(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;

            using (WardenStreamReader reader = new WardenStreamReader(datagram.BaseStream, true))
            {
                int size = reader.ReadVarInt32();
                bool fix = reader.ReadByte() == 1;
                if (size > this.Mtu)
                {
                    logger.Debug($"MTU Successfully expanded to {size}");
                    this.Mtu = size;
                    if (!fix)
                        SendNextMtuExpand();
                }

                if (fix)
                {
                    logger.Debug($"Other side asks us to fix MTU on {size}");
                    FixMtu();
                }
            }
        }

        void FixMtu()
        {
            if (mtuStatus != MtuExpansionStatus.Started)
                return;

            logger.Debug($"Fixing MTU {this.Mtu}");
            mtuStatus = MtuExpansionStatus.Finished;
        }

        void OnMtuExpand(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;

            int size = datagram.GetTotalSize();
            byte fix = 0;
            if (size > peer.Configuration.LimitMtu)
            {
                size = peer.Configuration.LimitMtu;
                fix = 1;
            }

            logger.Debug($"MTU Successfully expanded to {size} by request from other side");
            var mtuDatagram = CreateSpecialDatagram(MessageType.ExpandMTUSuccess, 5);
            using(WardenStreamWriter writer = new WardenStreamWriter(mtuDatagram.BaseStream, true))
            {
                writer.WriteVarInt(size);
                writer.Write(fix);
            }
            if (size > this.Mtu)
                this.Mtu = size;
            _ = SendDatagramAsync(mtuDatagram);

            datagram.Dispose();
        }

        void SendNextMtuExpand()
        {
            int nextMtu = 0;

            if (smallestFailedMtu < 0)
                nextMtu = (int)(this.Mtu * 1.25);
            else
                nextMtu = (int)(((float)smallestFailedMtu + (float)Mtu) / 2.0f);

            if (nextMtu > peer.Configuration.LimitMtu)
                nextMtu = peer.Configuration.LimitMtu;

            if (nextMtu == Mtu)
            {
                FixMtu();
                return;
            }

            lastMtuExpandSent = DateTime.UtcNow;
            int size = nextMtu - Datagram.GetHeaderSize(false);
            var mtuDatagram = CreateSpecialDatagram(MessageType.ExpandMTURequest, size);
            mtuDatagram.BaseStream.SetLength(size);
            if (mtuDatagram.GetTotalSize() != nextMtu)
                throw new Exception("Datagram total size doesn't match header+body size. Perhaps header size calculation failed");

            logger.Debug($"Expanding MTU to {nextMtu}");
            SendDatagramAsync(mtuDatagram).ContinueWith(t => {
                if(t.Result != System.Net.Sockets.SocketError.Success)
                {
                    logger.Debug($"MTU {nextMtu} expand send failed with {t.Result}");
                    if (smallestFailedMtu < 1 || nextMtu < smallestFailedMtu)
                    {
                        smallestFailedMtu = nextMtu;
                        mtuFailedAttempts++;
                        if (mtuFailedAttempts >= peer.Configuration.MtuExpandMaxFailAttempts)
                        {
                            FixMtu();
                            return;
                        }

                        SendNextMtuExpand();
                    }
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        void MtuCheck()
        {
            if (mtuStatus != MtuExpansionStatus.Started)
                return;

            if ((DateTime.UtcNow - lastMtuExpandSent).TotalMilliseconds > peer.Configuration.MtuExpandFrequency)
            {
                mtuFailedAttempts++;
                if (mtuFailedAttempts >= peer.Configuration.MtuExpandMaxFailAttempts)
                {
                    FixMtu();
                    return;
                }

                SendNextMtuExpand();
                return;
            }
        }
    }
}
