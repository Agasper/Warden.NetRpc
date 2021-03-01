using System;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc.Net.Tcp.Events;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConnectionEncrypted : RpcTcpConnection
    {
        static RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();
        
        BigInteger privateKey;
        BigInteger publicKey;
        byte[] commonKey;
        
        bool handshakeSend;

        readonly BigInteger mod = BigInteger.Parse("8344036200867339188401421868243599800768302958029168098393701372645433245359142296592083846452559047641776847523169623760010321326001893234240700419675989");
        
        ICipher cipher;
        
        internal RpcTcpConnectionEncrypted(TcpPeer parent, IRpcPeer rpcPeer, RpcTcpConfiguration configuration) : base(parent, rpcPeer, configuration)
        {
        }

        internal void SetCipher(ICipher cipher)
        {
            this.cipher = cipher;
        }

        public override void Stash()
        {
            base.Stash();
            this.cipher?.Dispose();
            this.cipher = null;
        }

        protected override void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {
            if (!this.IsClientConnection)
                return;

            byte[] privateKeyBytes = new byte[64];
            byte[] publicKeyBytes = new byte[64];
            rngCsp.GetBytes(privateKeyBytes);
            rngCsp.GetBytes(publicKeyBytes);
            privateKey = new BigInteger(privateKeyBytes);
            publicKey = new BigInteger(publicKeyBytes);
            
            BigInteger clientMod = ((privateKey * publicKey) % mod);
            
            var message = Parent.CreateMessage();
            using (WardenStreamWriter writer = new WardenStreamWriter(message.BaseStream, true))
            {
                writer.Write(publicKeyBytes.Length);
                writer.Write(publicKeyBytes);
                byte[] clientModBytes = clientMod.ToByteArray();
                writer.Write(clientModBytes.Length);
                writer.Write(clientModBytes);
            }

            _ = SendMessageAsync(message);
            handshakeSend = true;
            logger.Debug($"Secure handshake request sent!");
        }

        protected override void OnMessageReceived(MessageEventArgs args)
        {
            try
            {
                if (commonKey == null && !handshakeSend)
                {
                    logger.Debug($"Got secure handshake request");
                    BigInteger clientMod;

                    using (args.Message)
                    {
                        using (WardenStreamReader reader = new WardenStreamReader(args.Message.BaseStream, false))
                        {
                            int publicKeySize = reader.ReadInt32();
                            publicKey = new BigInteger(reader.ReadBytes(publicKeySize));
                            int clientModSize = reader.ReadInt32();
                            clientMod = new BigInteger(reader.ReadBytes(clientModSize));
                        }
                    }

                    byte[] keyBytes = new byte[64];
                    rngCsp.GetBytes(keyBytes);
                    privateKey = new BigInteger(keyBytes);
                    BigInteger serverMod = (privateKey * publicKey) % mod;
                    commonKey = ((privateKey * clientMod) % mod).ToByteArray();

                    var message = Parent.CreateMessage();
                    using (WardenStreamWriter writer = new WardenStreamWriter(message.BaseStream, true))
                    {
                        byte[] serverModBytes = serverMod.ToByteArray();
                        writer.Write(serverModBytes.Length);
                        writer.Write(serverModBytes);
                    }

                    _ = SendMessageAsync(message);
                    logger.Debug($"Sent secure handshake response");

                    cipher.SetKey(GenerateCipherKey128());
                    logger.Debug($"Common key set!");
                    base.InitSession();

                    return;
                }

                if (commonKey == null && handshakeSend)
                {
                    using (args.Message)
                    {
                        using (WardenStreamReader reader = new WardenStreamReader(args.Message.BaseStream, false))
                        {
                            int serverModSize = reader.ReadInt32();
                            BigInteger serverMod = new BigInteger(reader.ReadBytes(serverModSize));
                            commonKey = ((privateKey * serverMod) % mod).ToByteArray();
                        }
                    }

                    cipher.SetKey(GenerateCipherKey128());
                    logger.Debug($"Common key set!");
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

            using (args.Message)
            {
                using (var decryptedMessage = args.Message.Decrypt(cipher))
                {
                    base.OnMessageReceived(new MessageEventArgs(this, decryptedMessage));
                }
            }
        }

        protected override void SendRawMessage(TcpRawMessage message)
        {
            using (message)
            {
                TcpRawMessage encryptedMessage = message.Encrypt(cipher);
                base.SendRawMessage(encryptedMessage);
            }
        }

        public byte[] GenerateCipherKey128()
        {
            if (commonKey == null)
                throw new InvalidOperationException("Handshake sequence has not been completed. Couldn't set cipher key");
            
            using (MD5 hash = MD5.Create())
            {
                return hash.ComputeHash(commonKey);
            }
        }
    }
}