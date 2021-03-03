using System;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc.Net.Tcp.Events;

//using static Warden.Rpc.Net.Tcp.RpcTcpServer;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConnection : TcpConnection, IRpcConnection
    {
        public RpcSession Session => session;
        
        RpcSession session;
        readonly RpcTcpConfiguration configuration;
        readonly IRpcPeer rpcPeer;

        internal RpcTcpConnection(TcpPeer parent, IRpcPeer rpcPeer, RpcTcpConfiguration configuration) : base(parent)
        {
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
