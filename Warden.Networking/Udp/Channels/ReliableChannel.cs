using System;
using System.Collections.Concurrent;
using Warden.Logging;
using Warden.Networking.Udp.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Channels
{
    class ReliableChannel : ChannelBase
    {
        const int WINDOW_SIZE = 64;

        int recvWindowStart;
        bool[] recvEarlyReceived;
        Datagram[] recvWithheld;

        object ackMutex = new object();
        int ackWindowStart;
        PendingPacket[] sendPendingPackets;
        ConcurrentQueue<Datagram> sendDelayedPackets;

        bool ordered;

        public ReliableChannel(MemoryStreamPool memoryStreamPool, ILogManager logManager,
            ChannelDescriptor descriptor, IChannelConnection connection, bool ordered)
                : base(memoryStreamPool, logManager, descriptor, connection)
        {
            this.ordered = ordered;
            this.recvWindowStart = 0;
            this.ackWindowStart = 0;
            if (ordered)
                this.recvWithheld = new Datagram[WINDOW_SIZE];
            else
                this.recvEarlyReceived = new bool[WINDOW_SIZE];
            this.sendPendingPackets = new PendingPacket[WINDOW_SIZE];
            this.sendDelayedPackets = new ConcurrentQueue<Datagram>();
        }

        public override void Dispose()
        {
            if (recvWithheld != null)
            {
                for(int i = 0; i < recvWithheld.Length; i++)
                {
                    var record = recvWithheld[i];
                    if (record != null)
                        record.Dispose();
                    recvWithheld[i] = null;
                }
            }

            for (int i = 0; i < sendPendingPackets.Length; i++)
            {
                sendPendingPackets[i].Clear();
            }

            while(sendDelayedPackets.TryDequeue(out Datagram dequeued))
            {
                dequeued.Dispose();
            }
        }

        void AdvanceWindow()
        {
            if (ordered)
                recvWithheld[recvWindowStart % WINDOW_SIZE] = null;
            else
                recvEarlyReceived[recvWindowStart % WINDOW_SIZE] = false;
            recvWindowStart = (recvWindowStart + 1) % MAX_SEQUENCE;
        }

        public override void OnAckReceived(Datagram datagram)
        {
            lock (ackMutex)
            {
                int relate = RelativeSequenceNumber(datagram.Sequence, ackWindowStart);
                if (relate < 0)
                {
                    logger.Trace("Got duplicate/late ack");
                    datagram.Dispose();
                    return; //late/duplicate ack
                }

                if (relate == 0)
                {
                    logger.Trace($"Got ack just in time, clearing pending {datagram}");
                    //connection.UpdateLatency(sendPendingPackets[ackWindowStart % WINDOW_SIZE].GetDelay());

                    sendPendingPackets[ackWindowStart % WINDOW_SIZE].Clear();
                    ackWindowStart = (ackWindowStart + 1) % MAX_SEQUENCE;

                    while (sendPendingPackets[ackWindowStart % WINDOW_SIZE].AckReceived)
                    {
                        logger.Trace($"Clearing early pending {datagram}");
                        sendPendingPackets[ackWindowStart % WINDOW_SIZE].Clear();
                        ackWindowStart = (ackWindowStart + 1) % MAX_SEQUENCE;
                    }

                    datagram.Dispose();

                    lock (this.sequenceOutMutex)
                    {
                        TrySendDelayedPackets();
                    }

                    return;
                }

                int sendRelate = RelativeSequenceNumber(datagram.Sequence, lastSequenceOut);
                if (sendRelate < 0)
                {
                    if (sendRelate < -WINDOW_SIZE)
                    {
                        logger.Trace("Very old ack received");
                        datagram.Dispose();
                        return;
                    }
                    //we have sent this message, it's just early
                    if (sendPendingPackets[datagram.Sequence % WINDOW_SIZE].GotAck())
                    {
                        //connection.UpdateLatency(sendPendingPackets[ackWindowStart % WINDOW_SIZE].GetDelay());
                        logger.Trace($"Got early ack {datagram} {sendRelate} {relate}");
                    }
                    else
                        logger.Debug($"Got ack {datagram} for packet we're not waiting for {sendRelate} {relate}");
                }
                else if (sendRelate > 0)
                {
                    logger.Debug("Got Ack for message we have not sent");
                }

                datagram.Dispose();

                //Probably gap in ack sequence need faster message resend
                //int curSequence = datagram.Sequence;
                //do
                //{
                //    curSequence--;
                //    if (curSequence < 0)
                //        curSequence = MAX_SEQUENCE - 1;

                //    int slot = curSequence % WINDOW_SIZE;
                //    if (!ackReceived[slot])
                //    {
                //        if (sendPendingPackets[slot].ReSendNum == 1)
                //        {
                //            sendPendingPackets[slot].TryReSend(SendImmidiately, connection.GetInitialResendDelay(), false);
                //        }
                //    }

                //} while (curSequence != ackWindowStart);
            }
        }

        public override void OnDatagram(Datagram datagram)
        {
            CheckDatagramValid(datagram);

            connection.SendDatagramAsync(datagram.CreateAck());

            lock (sequenceInMutex)
            {
                int relate = RelativeSequenceNumber(datagram.Sequence, recvWindowStart);
                if (relate == 0) 
                {
                    //right in time
                    AdvanceWindow();
                    connection.ReleaseDatagram(datagram);

                    int nextSeqNr = (datagram.Sequence + 1) % MAX_SEQUENCE;

                    if (ordered)
                    {
                        while (recvWithheld[nextSeqNr % WINDOW_SIZE] != null)
                        {
                            connection.ReleaseDatagram(recvWithheld[nextSeqNr % WINDOW_SIZE]);
                            AdvanceWindow();
                            nextSeqNr++;
                        }
                    }
                    else
                    {
                        while (recvEarlyReceived[nextSeqNr % WINDOW_SIZE])
                        {
                            AdvanceWindow();
                            nextSeqNr++;
                        }
                    }

                    return;
                }

                if (relate < 0)
                {
                    //duplicate
                    logger.Trace($"Dropped duplicate {datagram}");
                    datagram.Dispose();
                    return;
                }

                if (relate > WINDOW_SIZE)
                {
                    //too early message
                    logger.Trace($"Dropped too early {datagram}");
                    datagram.Dispose();
                    return;
                }

                if (ordered)
                {
                    if (recvWithheld[datagram.Sequence % WINDOW_SIZE] != null)
                    {
                        //duplicate
                        logger.Trace($"Dropped duplicate {datagram}");
                        datagram.Dispose();
                        return;
                    }

                    recvWithheld[datagram.Sequence % WINDOW_SIZE] = datagram;
                }
                else
                {
                    if (recvEarlyReceived[datagram.Sequence % WINDOW_SIZE])
                    {
                        //duplicate
                        logger.Trace($"Dropped duplicate {datagram}");
                        datagram.Dispose();
                        return;
                    }

                    recvEarlyReceived[datagram.Sequence % WINDOW_SIZE] = true;
                    connection.ReleaseDatagram(datagram);
                }
            }
        }

        public override void PollEvents()
        {
            lock (ackMutex)
            {
                for (int pendingSeq = ackWindowStart; pendingSeq != this.lastSequenceOut; pendingSeq = (pendingSeq + 1) % MAX_SEQUENCE)
                {
                    var delay = DateTime.UtcNow - sendPendingPackets[pendingSeq % WINDOW_SIZE].Timestamp;
                    if (sendPendingPackets[pendingSeq % WINDOW_SIZE].TryReSend(SendPendingPacket, connection.GetInitialResendDelay(), true))
                    {
                        //Console.WriteLine($"Resend {sendPendingPackets[pendingSeq % WINDOW_SIZE].Datagram} after {delay.TotalMilliseconds} ({connection.GetInitialResendDelay()}) with num {sendPendingPackets[pendingSeq % WINDOW_SIZE].ReSendNum}");
                        logger.Debug($"Resend {sendPendingPackets[pendingSeq % WINDOW_SIZE].Datagram} after {delay.TotalMilliseconds} with num {sendPendingPackets[pendingSeq % WINDOW_SIZE].ReSendNum}");
                    }
                }
            }
        }

        void TrySendDelayedPackets()
        {
            while (CanSendImmidiately() && sendDelayedPackets.TryDequeue(out Datagram toSend))
            {
                SendImmidiately(toSend);
            }
        }

        void SendPendingPacket(Datagram datagram)
        {
            _ = connection.SendDatagramAsync(datagram);
        }

        UdpSendStatus SendImmidiately(Datagram datagram)
        {
            datagram.Sequence = GetNextSequenceOut();
            sendPendingPackets[datagram.Sequence % WINDOW_SIZE].Init(datagram);
            _ = connection.SendDatagramAsync(datagram);
            return UdpSendStatus.Enqueued;
        }

        public override UdpSendStatus SendDatagram(Datagram datagram)
        {
            CheckDatagramValid(datagram);

            lock (this.sequenceOutMutex)
            {
                TrySendDelayedPackets();
                if (!CanSendImmidiately())
                {
                    logger.Debug($"Can't send right now (window is full) {datagram}. Delaying...");
                    sendDelayedPackets.Enqueue(datagram);
                    return UdpSendStatus.Enqueued;
                }
                else
                    return SendImmidiately(datagram);
            }
        }

        bool CanSendImmidiately()
        {
            lock (this.sequenceOutMutex)
            {
                lock (ackMutex)
                {
                    int relate = RelativeSequenceNumber(this.lastSequenceOut, ackWindowStart);
                    return relate < WINDOW_SIZE;
                }
            }
        }
    }
}
