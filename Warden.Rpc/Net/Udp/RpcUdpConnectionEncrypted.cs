using System;
using System.Net;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Networking.Udp;
using Warden.Networking.Udp.Messages;
using Warden.Rpc.Net.Events;
using Warden.Rpc.Net.Tcp;
using ConnectionClosedEventArgs = Warden.Networking.Udp.Events.ConnectionClosedEventArgs;
using ConnectionOpenedEventArgs = Warden.Networking.Udp.Events.ConnectionOpenedEventArgs;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpConnectionEncrypted : RpcUdpConnection
    {
        DiffieHellmanImpl dh;
        ICipher cipher;
        
        internal RpcUdpConnectionEncrypted(UdpPeer parent, ICipher cipher, IRpcPeer rpcPeer, RpcConfiguration configuration) : base(parent, rpcPeer, configuration)
        {
            if (cipher == null)
                throw new ArgumentNullException(nameof(cipher));
            this.cipher = cipher;
            this.dh = new DiffieHellmanImpl(cipher);
        }

        protected override void OnConnectionClosed(ConnectionClosedEventArgs args)
        {
            this.cipher?.Dispose();
            this.cipher = null;
            this.dh = null;
            
            base.OnConnectionClosed(args);
        }

        protected override void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {
            if (!this.IsClientConnection)
                return;

            var message = Parent.CreateMessage();
            dh.SendHandshakeRequest(message.BaseStream);
            _ = SendMessage(new Networking.Udp.Messages.MessageInfo(message, DeliveryType.ReliableOrdered, 0));
            logger.Debug($"Secure handshake request sent!");
        }

        protected override void OnMessageReceived(Networking.Udp.Messages.MessageInfo messageInfo)
        {
            try
            {
                if (dh.Status == DiffieHellmanImpl.DhStatus.None)
                {
                    logger.Debug($"Got secure handshake request");
                    var responseMessage = Parent.CreateMessage();
                    dh.RecvHandshakeRequest(messageInfo.Message.BaseStream, responseMessage.BaseStream);
                    _ = SendMessage(new Networking.Udp.Messages.MessageInfo(responseMessage, DeliveryType.ReliableOrdered, 0));
                    logger.Debug($"Sent secure handshake response, common key set!");
                    base.InitSession();
                    return;
                }

                if (dh.Status == DiffieHellmanImpl.DhStatus.WaitingForServerMod)
                {
                    dh.RecvHandshakeResponse(messageInfo.Message.BaseStream);
                    logger.Debug($"Got secure handshake response, common key set!");
                    base.InitSession();
                    return;
                }
            }
            catch (Exception e)
            {
                logger.Error($"Handshake failed for {this}. Closing... {e}");
                Close();
                return;
            }

            try
            {
                using (messageInfo.Message)
                {
                    using (var decryptedMessage = messageInfo.Message.Decrypt(cipher))
                    {
                        base.OnMessageReceived(new Networking.Udp.Messages.MessageInfo(decryptedMessage, messageInfo.DeliveryType, messageInfo.Channel));
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error($"Unhandled exception in {nameof(RpcTcpConnectionEncrypted)}.{nameof(OnMessageReceived)}: {e}");
                Close();
            }
        }

        protected override UdpSendStatus SendRawMessage(Networking.Udp.Messages.MessageInfo messageInfo)
        {
            using (messageInfo.Message)
            {
                UdpRawMessage encryptedMessage = messageInfo.Message.Encrypt(cipher);
                return base.SendRawMessage(new Networking.Udp.Messages.MessageInfo(encryptedMessage, messageInfo.DeliveryType,
                    messageInfo.Channel));
            }
        }
    }
}
