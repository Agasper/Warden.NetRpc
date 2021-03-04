using System;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc.Net.Events;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConnectionEncrypted : RpcTcpConnection
    {
        DiffieHellmanImpl dh;
        ICipher cipher;

        internal RpcTcpConnectionEncrypted(TcpPeer parent, IRpcPeer rpcPeer, RpcConfiguration configuration) : base(
            parent, rpcPeer, configuration)
        {
        }

        internal void SetCipher(ICipher cipher)
        {
            this.cipher = cipher;
            this.dh = new DiffieHellmanImpl(cipher);
        }

        public override void Stash()
        {
            base.Stash();
            this.cipher?.Dispose();
            this.cipher = null;
            this.dh = null;
        }

        protected override void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {
            if (!this.IsClientConnection)
                return;

            var message = Parent.CreateMessage();
            dh.SendHandshakeRequest(message.BaseStream);
            _ = SendMessageAsync(message);
            logger.Debug($"Secure handshake request sent!");
        }

        protected override void OnMessageReceived(MessageEventArgs args)
        {
            using (args.Message)
            {
                try
                {
                    if (dh.Status == DiffieHellmanImpl.DhStatus.None)
                    {
                        logger.Debug($"Got secure handshake request");
                        var responseMessage = Parent.CreateMessage();
                        dh.RecvHandshakeRequest(args.Message.BaseStream, responseMessage.BaseStream);
                        _ = SendMessageAsync(responseMessage);
                        logger.Debug($"Sent secure handshake response, common key set!");
                        base.InitSession();
                        return;
                    }

                    if (dh.Status == DiffieHellmanImpl.DhStatus.WaitingForServerMod)
                    {
                        dh.RecvHandshakeResponse(args.Message.BaseStream);
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
                    using (var decryptedMessage = args.Message.Decrypt(cipher))
                    {
                        base.OnMessageReceived(new MessageEventArgs(this, decryptedMessage));
                    }

                }
                catch (Exception e)
                {
                    logger.Error(
                        $"Unhandled exception in {nameof(RpcTcpConnectionEncrypted)}.{nameof(OnMessageReceived)}: {e}");
                    Close();
                }
            }
        }


        private protected override void SendRawMessage(TcpRawMessage message)
        {
            using (message)
            {
                TcpRawMessage encryptedMessage = message.Encrypt(cipher);
                base.SendRawMessage(encryptedMessage);
            }
        }

    }
}