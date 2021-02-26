using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc;
using Warden.Rpc.Net.Tcp.Events;
using Warden.Rpc.Net.Udp;

//using static Warden.Rpc.Net.Tcp.RpcTcpServer;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConnection : TcpConnection, IRpcConnection
    {
        public RpcSession Session => session;
        
        RpcTcpConfiguration configuration;
        RpcSession session;
        IRpcPeerEventListener eventListener;

        internal RpcTcpConnection(TcpPeer parent, IRpcPeerEventListener eventListener, RpcTcpConfiguration configuration) : base(parent)
        {
            this.configuration = configuration;
            this.eventListener = eventListener;
        }

        protected internal virtual void CreateSession()
        {
            var session_ = configuration.SessionFactory.CreateSession(CreateContext());
            session_.InitializeRemotingObject(session);
            this.session = session_;
            
            eventListener.OnSessionOpened(new SessionOpenedEventArgs(session_, this));
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
            if (this.session != null)
            {
                this.session?.Close();
                eventListener.OnSessionClosed(new SessionClosedEventArgs(this.session, this));
            }
        }

        protected override void OnMessageReceived(MessageEventArgs args)
        {
            using (args.Message)
            {
                using (WardenStreamReader sr = new WardenStreamReader(args.Message.BaseStream, true))
                {
                    session.OnMessage(sr);
                }
            }
            
            base.OnMessageReceived(args);
        }

        public bool SendReliable(ICustomMessage message)
        {
            if (!this.Connected)
                return false;
            
            TcpRawMessage rawMessage = Parent.CreateMessage();
            using (WardenStreamWriter sw = new WardenStreamWriter(rawMessage.BaseStream, true))
            {
                message.WriteTo(new WriteFormatterInfo(sw, configuration.Serializer));
            }
            
            SendRawMessage(rawMessage);
            return true;
        }

        protected virtual void SendRawMessage(TcpRawMessage message)
        {
            _ = this.SendMessageAsync(message);
        }
    }
}
