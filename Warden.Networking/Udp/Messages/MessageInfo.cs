namespace Warden.Networking.Udp.Messages
{
    public struct MessageInfo
    {
        public UdpRawMessage Message { get; set; }
        public DeliveryType DeliveryType { get; set; }
        public int Channel
        {
            get => channel;
            set
            {
                ChannelDescriptor.CheckChannelValue(value);
                channel = value;
            }
        }

        int channel;

        public MessageInfo(UdpRawMessage message, DeliveryType deliveryType, int channel)
        {
            ChannelDescriptor.CheckChannelValue(channel);
            this.Message = message;
            this.DeliveryType = deliveryType;
            this.channel = channel;
        }
    }
}
