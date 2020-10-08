﻿using System;
using System.Threading;
using Warden.Logging;
using Warden.Networking.Udp.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Channels
{
    class UnreliableSequencedChannel : ChannelBase
    {
        public UnreliableSequencedChannel(MemoryStreamPool memoryStreamPool, ILogManager logManager,
            ChannelDescriptor descriptor, IChannelConnection connection)
            : base(memoryStreamPool, logManager, descriptor, connection)
        {

        }


        public override UdpSendStatus SendDatagram(Datagram datagram)
        {
            CheckDatagramValid(datagram);
            datagram.Sequence = GetNextSequenceOut();
            _ = connection.SendDatagramAsync(datagram);
            return UdpSendStatus.Enqueued;
        }

        public override void OnDatagram(Datagram datagram)
        {
            CheckDatagramValid(datagram);
            lock (sequenceInMutex)
            {
                int relate = RelativeSequenceNumber(datagram.Sequence, lastSequenceIn + 1);
                if (relate < 0)
                {
                    logger.Debug($"Dropping old {datagram}");
                    datagram.Dispose();
                    return; //drop old
                }
                lastSequenceIn = datagram.Sequence;
            }
            connection.ReleaseDatagram(datagram);
        }
    }
}
