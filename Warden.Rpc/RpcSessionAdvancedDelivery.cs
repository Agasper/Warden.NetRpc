using System;
using Warden.Networking.Udp.Messages;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public class RpcSessionAdvancedDelivery : RpcSession
    {
        readonly IRpcConnectionAdvancedDelivery advancedDeliveryConnection;

        public RpcSessionAdvancedDelivery(RpcSessionContext context) : base(context)
        {
            this.advancedDeliveryConnection = context.Connection as IRpcConnectionAdvancedDelivery;
            if (this.advancedDeliveryConnection == null)
                throw new InvalidCastException($"You must use {nameof(IRpcConnectionAdvancedDelivery)} for {nameof(RpcSessionAdvancedDelivery)}");
        }

        void SendMessageCustom(IWardenMessage message, DeliveryType deliveryType, int channel)
        {
            this.logger.Trace($"{this} sending {message}");
            if (!this.advancedDeliveryConnection.SendCustom(message, deliveryType, channel))
                throw new RemotingException("Transport connection is closed");
        }
        
        public virtual void Send(int methodIdentity, DeliveryType deliveryType) =>
            Send_(methodIdentity, deliveryType, ChannelDescriptor.DEFAULT_CHANNEL, SendingOptions.Default);

        public virtual void Send(string methodIdentity, DeliveryType deliveryType) =>
            Send_(methodIdentity, deliveryType, ChannelDescriptor.DEFAULT_CHANNEL, SendingOptions.Default);

        public virtual void Send(int methodIdentity, DeliveryType deliveryType, int channel) =>
            Send_(methodIdentity, deliveryType, channel, SendingOptions.Default);

        public virtual void Send(string methodIdentity, DeliveryType deliveryType, int channel) =>
            Send_(methodIdentity, deliveryType, channel, SendingOptions.Default);

        public virtual void Send(int methodIdentity, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions) => Send_(methodIdentity, deliveryType, channel, sendingOptions);

        public virtual void Send(string methodIdentity, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions) => Send_(methodIdentity, deliveryType, channel, sendingOptions);

        void Send_(object methodIdentity, DeliveryType deliveryType, int channel, SendingOptions sendingOptions)
        {
            RemotingRequest request = GetRequest(methodIdentity, false, !sendingOptions.NoAck);
            request.HasArgument = false;
            this.logger.Debug($"Sending {request}");
            SendMessageCustom(request, deliveryType, channel);
        }
        
        public virtual void Send<T>(int methodIdentity, T arg, DeliveryType deliveryType) =>
            Send_(methodIdentity, arg, deliveryType, ChannelDescriptor.DEFAULT_CHANNEL, SendingOptions.Default);

        public virtual void Send<T>(string methodIdentity, T arg, DeliveryType deliveryType) =>
            Send_(methodIdentity, arg, deliveryType, ChannelDescriptor.DEFAULT_CHANNEL, SendingOptions.Default);

        public virtual void Send<T>(int methodIdentity, T arg, DeliveryType deliveryType, int channel) =>
            Send_(methodIdentity, arg, deliveryType, channel, SendingOptions.Default);

        public virtual void Send<T>(string methodIdentity, T arg, DeliveryType deliveryType, int channel) =>
            Send_(methodIdentity, arg, deliveryType, channel, SendingOptions.Default);

        public virtual void Send<T>(int methodIdentity, T arg, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions) => Send_(methodIdentity, arg, deliveryType, channel, sendingOptions);

        public virtual void Send<T>(string methodIdentity, T arg, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions) => Send_(methodIdentity, arg, deliveryType, channel, sendingOptions);

        void Send_<T>(object methodIdentity, T arg, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions)
        {
            RemotingRequest request = GetRequest(methodIdentity, false, !sendingOptions.NoAck);
            request.HasArgument = true;
            request.Argument = arg;
            this.logger.Debug($"Sending {request}");
            SendMessageCustom(request, deliveryType, channel);
        }
    }
}