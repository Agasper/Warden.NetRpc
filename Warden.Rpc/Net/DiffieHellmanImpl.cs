using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Messages;

namespace Warden.Rpc.Net
{
    public class DiffieHellmanImpl
    {
        public enum DhStatus
        {
            None,
            WaitingForServerMod,
            CommonKeySet
        }

        public DhStatus Status => status;
        public byte[] CommonKey => commonKey;
        
        static readonly BigInteger mod = BigInteger.Parse("8344036200867339188401421868243599800768302958029168098393701372645433245359142296592083846452559047641776847523169623760010321326001893234240700419675989");
        static RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();
        const ushort mark = 21078;
        
        BigInteger privateKey;
        BigInteger publicKey;
        byte[] commonKey;

        DhStatus status;
        ICipher cipher;

        public DiffieHellmanImpl(ICipher cipher)
        {
            this.cipher = cipher;
        }

        public void SendHandshakeRequest(Stream handshakeRequest)
        {
            if (status != DhStatus.None)
                throw new InvalidOperationException($"Wrong status {status}, expected: {DhStatus.None}");
            
            byte[] privateKeyBytes = new byte[64];
            byte[] publicKeyBytes = new byte[64];
            rngCsp.GetBytes(privateKeyBytes);
            rngCsp.GetBytes(publicKeyBytes);
            privateKey = new BigInteger(privateKeyBytes);
            publicKey = new BigInteger(publicKeyBytes);
            
            BigInteger clientMod = ((privateKey * publicKey) % mod);
            
            using (WardenStreamWriter writer = new WardenStreamWriter(handshakeRequest, true))
            {
                writer.Write(mark);
                writer.Write(publicKeyBytes.Length);
                writer.Write(publicKeyBytes);
                byte[] clientModBytes = clientMod.ToByteArray();
                writer.Write(clientModBytes.Length);
                writer.Write(clientModBytes);
            }

            status = DhStatus.WaitingForServerMod;
        }

        public void RecvHandshakeRequest(Stream handshakeRequest, Stream handshakeResponse)
        {
            if (status != DhStatus.None)
                throw new InvalidOperationException($"Wrong status {status}, expected: {DhStatus.None}");
            
            BigInteger clientMod;
            
            using (WardenStreamReader reader = new WardenStreamReader(handshakeRequest, true))
            {
                ushort gotMark = reader.ReadUInt16();
                if (mark != gotMark)
                    throw new InvalidOperationException("Handshake failed, wrong mark. Perhaps the other peer is trying to connect with unsecure connection");
                int publicKeySize = reader.ReadInt32();
                publicKey = new BigInteger(reader.ReadBytes(publicKeySize));
                int clientModSize = reader.ReadInt32();
                clientMod = new BigInteger(reader.ReadBytes(clientModSize));
            }
            
            byte[] keyBytes = new byte[64];
            rngCsp.GetBytes(keyBytes);
            privateKey = new BigInteger(keyBytes);
            BigInteger serverMod = (privateKey * publicKey) % mod;
            commonKey = ((privateKey * clientMod) % mod).ToByteArray();
            
            using (WardenStreamWriter writer = new WardenStreamWriter(handshakeResponse, true))
            {
                byte[] serverModBytes = serverMod.ToByteArray();
                writer.Write(serverModBytes.Length);
                writer.Write(serverModBytes);
            }

            SetCipherKey();
        }

        public void RecvHandshakeResponse(Stream handshakeResponse)
        {
            if (status != DhStatus.WaitingForServerMod)
                throw new InvalidOperationException($"Wrong status {status}, expected: {DhStatus.WaitingForServerMod}");
            
            using (WardenStreamReader reader = new WardenStreamReader(handshakeResponse, true))
            {
                int serverModSize = reader.ReadInt32();
                BigInteger serverMod = new BigInteger(reader.ReadBytes(serverModSize));
                commonKey = ((privateKey * serverMod) % mod).ToByteArray();
            }

            SetCipherKey();
        }
        
        void SetCipherKey()
        {
            if (commonKey == null)
                throw new InvalidOperationException("Handshake sequence has not been completed. Couldn't set cipher key");

            if (cipher.KeySize == 128)
            {
                using (MD5 hash = MD5.Create())
                    cipher.SetKey(hash.ComputeHash(commonKey));
            }
            else
                throw new InvalidOperationException($"{nameof(DiffieHellmanImpl)} supports only ciphers with 128 bit keys");

            status = DhStatus.CommonKeySet;
        }
    }
}