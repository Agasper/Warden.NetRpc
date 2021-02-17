using System;
using Warden.Logging;
using Warden.Networking.Udp.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Channels
{
    abstract class ChannelBase : IChannel
    {
        public const int MAX_SEQUENCE = ushort.MaxValue-1;

        public ChannelDescriptor Descriptor { get; private set; }

        readonly protected MemoryStreamPool memoryStreamPool;
        readonly protected ILogger logger;
        readonly protected IChannelConnection connection;

        protected int lastSequenceIn = -1;
        protected int lastSequenceOut = 0;

        readonly protected object channelMutex = new object();


        public ChannelBase(MemoryStreamPool memoryStreamPool, ILogManager logManager,
            ChannelDescriptor descriptor, IChannelConnection connection)
        {
            this.memoryStreamPool = memoryStreamPool;
            this.Descriptor = descriptor;
            this.connection = connection;
            this.logger = logManager.GetLogger(this.GetType().Name);
        }

        public virtual void Dispose()
        {

        }

        public abstract void OnDatagram(Datagram datagram);
        public abstract UdpSendStatus SendDatagram(Datagram datagram);

        public virtual void PollEvents()
        {

        }

        public virtual void OnAckReceived(Datagram datagram)
        {
            throw new NotSupportedException("This channel doesn't support acks");
        }

        protected void CheckDatagramValid(Datagram datagram)
        {
            if (datagram.DeliveryType != this.Descriptor.DeliveryType)
                throw new ArgumentException($"{nameof(Datagram)} doesn't fit channel by delivery type", nameof(datagram.DeliveryType));
            if (datagram.Channel != this.Descriptor.Channel)
                throw new ArgumentException($"{nameof(Datagram)} doesn't fit channel by index", nameof(datagram.Channel));
        }

        protected virtual ushort GetNextSequenceOut()
        {
            ushort newSequence = 0;
            lock (channelMutex)
            {
                newSequence = (ushort)lastSequenceOut;
                lastSequenceOut = (ushort)((lastSequenceOut + 1) % MAX_SEQUENCE);
            }
            return newSequence;
        }

        public static int RelativeSequenceNumber(int nr, int expected)
        {
            return (nr - expected + MAX_SEQUENCE + (MAX_SEQUENCE / 2)) % MAX_SEQUENCE - (MAX_SEQUENCE / 2);
        }
    }
}
