using System;
using System.Net;
using Warden.Networking.IO;
using Warden.Networking.Udp;
using Warden.Networking.Udp.Events;
using Warden.Networking.Udp.Messages;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpConnection : UdpConnection, IRpcConnectionAdvancedDelivery
    {
        public long Id => base.EndPoint.ConnectionKey;
        public EndPoint RemoteEndpoint => base.EndPoint.EndPoint;

        RpcUdpServerConfiguration configuration;
        RpcSession session;
        
        public RpcUdpConnection(UdpPeer peer, RpcUdpServerConfiguration configuration) : base(peer)
        {
            this.configuration = configuration;
        }

        RpcSessionContext CreateContext()
        {
            RpcSessionContext result = new RpcSessionContext();
            result.Connection = this;
            result.Serializer = configuration.Serializer;
            result.LogManager = configuration.LogManager;
            result.TaskScheduler = configuration.TaskScheduler;
            result.OrderedExecution = configuration.OrderedExecution;
            result.DefaultExecutionTimeout = configuration.DefaultExecutionTimeout;
            result.OrderedExecutionMaxQueue = configuration.OrderedExecutionMaxQueue;

            return result;
        }

        protected override void OnConnectionClosed(ConnectionClosedEventArgs args)
        {
            base.OnConnectionClosed(args);
            this.session?.Close();
        }

        public void CreateSession()
        {
            var session = configuration.SessionFactory.CreateSession(CreateContext());
            session.InitializeRemotingObject(session);
            this.session = session;
        }

        public bool SendReliable(ICustomMessage message)
        {
            return this.SendCustom(message, DeliveryType.ReliableOrdered, 0);
        }

        public bool SendCustom(ICustomMessage message, DeliveryType deliveryType, int channel)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ChannelDescriptor.CheckChannelValue(channel);

            using (var rawMessage = this.Parent.CreateMessage())
            {
                rawMessage.DeliveryType = deliveryType;
                rawMessage.Channel = channel;

                using (WardenStreamWriter sw = new WardenStreamWriter(rawMessage.BaseStream, true))
                {
                    message.WriteTo(new WriteFormatterInfo(sw, this.configuration.Serializer));
                }

                UdpSendStatus result = base.SendMessage(new Warden.Networking.Udp.Messages.MessageInfo(rawMessage, 
                    deliveryType, channel));

                return result != UdpSendStatus.Failed;
            }
        }
    }
}
