using System;
using System.IO.Compression;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc.Net.Events;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConnection : TcpConnection, IRpcConnection
    {
        public RpcSession Session => session;
        public float? Latency => this.Statistics.Latency;
        
        RpcSession session;
        readonly RpcConfiguration configuration;
        readonly IRpcPeer rpcPeer;

        internal RpcTcpConnection(TcpPeer parent, IRpcPeer rpcPeer, RpcConfiguration configuration) : base(parent)
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

        protected override void OnMessageReceived(MessageEventArgs args)
        {
            try
            {
                using (args.Message)
                {
                    if (args.Message.HasCompressionMark())
                    {
                        logger.Trace($"Message {args.Message} has compression mark, decompressing it");
                        using (var uncompressedMessage = args.Message.Decompress())
                        {
                            logger.Trace($"Decompressing {args.Message} to {uncompressedMessage}");
                            using (WardenStreamReader sr = new WardenStreamReader(uncompressedMessage.BaseStream, true))
                            {
                                session.OnMessage(sr);
                            }
                        }
                    }
                    else
                    {
                        using (WardenStreamReader sr = new WardenStreamReader(args.Message.BaseStream, true))
                        {
                            session.OnMessage(sr);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception in {nameof(RpcTcpConnection)}.{nameof(OnMessageReceived)}: {e}");
                Close();
            }
        }

        public bool SendReliable(IWardenMessage message)
        {
            if (!this.Connected)
                return false;

            TcpRawMessage rawMessage = Parent.CreateMessage();
            using (WardenStreamWriter sw = new WardenStreamWriter(rawMessage.BaseStream, true))
            {
                message.WriteTo(new WriteFormatterInfo(sw, configuration.Serializer));
            }

            if (rawMessage.Length >= configuration.CompressionThreshold)
            {
                logger.Trace($"Message {message} size exceeds compression threshold {configuration.CompressionThreshold}, compressing it");
                using (rawMessage)
                {
                    var compressedMessage = rawMessage.Compress(CompressionLevel.Optimal);
                    logger.Trace($"Compressing {rawMessage} to {compressedMessage}");
                    compressedMessage.Position = 0;
                    SendRawMessage(compressedMessage);
                }
            }
            else
                SendRawMessage(rawMessage);

            return true;
        }

        private protected virtual void SendRawMessage(TcpRawMessage message)
        {
            _ = this.SendMessageAsync(message);
        }
    }
}
