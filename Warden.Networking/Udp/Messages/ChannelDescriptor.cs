using System;
using System.Collections.Generic;

namespace Warden.Networking.Udp.Messages
{
    public struct ChannelDescriptor
    {
        public int Channel
        {
            get => channel;
            set
            {
                CheckChannelValue(value);
                channel = value;
            }
        }
        public DeliveryType DeliveryType => deliveryType;

        int channel;
        DeliveryType deliveryType;

        public ChannelDescriptor(int channel, DeliveryType deliveryType)
        {
            CheckChannelValue(channel);
            this.channel = channel;
            this.deliveryType = deliveryType;
        }

        public static void CheckChannelValue(int channel)
        {
            if (channel > MAX_CHANNEL || channel < 0)
                throw new ArgumentOutOfRangeException(nameof(channel), $"Channel should be in range 0-{MAX_CHANNEL}");
        }

        public const int MAX_CHANNEL = 7;

        public class EqualityComparer : IEqualityComparer<ChannelDescriptor>
        {
            public bool Equals(ChannelDescriptor x, ChannelDescriptor y)
            {
                return x.channel == y.channel && x.deliveryType == y.deliveryType;
            }

            public int GetHashCode(ChannelDescriptor x)
            {
                return ((byte)x.deliveryType << 4) & x.channel;
            }

        }
    }
}
