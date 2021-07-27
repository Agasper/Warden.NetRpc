using System;
using System.Net;
using Warden.Networking.IO;
using Warden.Networking.Udp;
using Warden.Networking.Udp.Messages;
using Warden.Rpc.Net.Events;
using ConnectionClosedEventArgs = Warden.Networking.Udp.Events.ConnectionClosedEventArgs;
using ConnectionOpenedEventArgs = Warden.Networking.Udp.Events.ConnectionOpenedEventArgs;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpConnection : UdpConnection, IRpcConnectionAdvancedDelivery
    {
        public RpcSession Session => session;
        public long Id => base.EndPoint.ConnectionKey;
        public float? Latency => this.Statistics.Latency;
        public EndPoint RemoteEndpoint => base.EndPoint.EndPoint;
        
        RpcSession session;
        readonly RpcConfiguration configuration;
        readonly IRpcPeer rpcPeer;
        
        internal RpcUdpConnection(UdpPeer parent, IRpcPeer rpcPeer, RpcConfiguration configuration) : base(parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (rpcPeer == null)
                throw new ArgumentNullException(nameof(rpcPeer));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            this.configuration = configuration;
            this.rpcPeer = rpcPeer;
        }

        protected override void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {
            InitSession();
        }

        private protected virtual void InitSession()
        {
            try
            {
                var session = CreateSession();
                rpcPeer.OnSessionOpened(new SessionOpenedEventArgs(session, this));
            }
            catch (Exception e)
            {
                logger.Error($"Failed to create {nameof(RpcSession)}, closing connection: {e}");
                this.Close();
            }
        }

        protected virtual RpcSession CreateSession()
        {
            var session_ = rpcPeer.CreateSession(CreateContext());
            session_.InitializeRemotingObject(session_);
            this.session = session_;

            return session_;
        }

        protected virtual RpcSessionContext CreateContext()
        {
            RpcSessionContext result = new RpcSessionContext();
            result.Connection = this;
            result.Serializer = configuration.Serializer;
            result.LogManager = configuration.LogManager;
            result.TaskScheduler = configuration.TaskScheduler;
            result.OrderedExecution = configuration.OrderedExecution;
            result.DefaultExecutionTimeout = configuration.DefaultExecutionTimeout;
            result.OrderedExecutionMaxQueue = configuration.OrderedExecutionMaxQueue;
            result.RemotingObjectConfiguration = configuration.RemotingConfiguration;

            return result;
        }

        protected override void OnConnectionClosed(ConnectionClosedEventArgs args)
        {
            if (this.session != null)
            {
                this.session?.Close();
                rpcPeer.OnSessionClosed(new SessionClosedEventArgs(this.session, this));
            }
            base.OnConnectionClosed(args);
        }

        protected override void OnMessageReceived(Networking.Udp.Messages.MessageInfo messageInfo)
        {
            try
            {
                using (messageInfo.Message)
                {
                    using (WardenStreamReader sr = new WardenStreamReader(messageInfo.Message.BaseStream, true))
                    {
                        session.OnMessage(sr);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception in {nameof(RpcUdpConnection)}.{nameof(OnMessageReceived)}: {e}");
                Close();
            }
        }

        public bool SendReliable(IWardenMessage message)
        {
            return this.SendCustom(message, DeliveryType.ReliableOrdered, 0);
        }

        public virtual bool SendCustom(IWardenMessage message, DeliveryType deliveryType, int channel)
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

                UdpSendStatus result = SendRawMessage(new Warden.Networking.Udp.Messages.MessageInfo(rawMessage, 
                    deliveryType, channel));

                return result != UdpSendStatus.Failed;
            }
        }
        
        protected virtual UdpSendStatus SendRawMessage(Warden.Networking.Udp.Messages.MessageInfo messageInfo)
        {
            return base.SendMessage(messageInfo);
        }
    }
}
