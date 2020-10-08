using System;
using System.Security.Cryptography;

namespace Warden.Networking.Cryptography
{
    public class Aes128Cipher : ICipher, IDisposable
    {
        Aes cipher;

        public Aes128Cipher(byte[] key) : this()
        {
            SetKey(key);
        }

        public Aes128Cipher()
        {
            cipher = Aes.Create();
            cipher.KeySize = 128;
            cipher.BlockSize = 128;
            cipher.Padding = PaddingMode.ISO10126;
            cipher.Mode = CipherMode.CBC;
        }

        public void SetKey(byte[] key)
        {
            cipher.Key = key;
            byte[] iv = new byte[cipher.BlockSize / 8];
            Buffer.BlockCopy(key, 0, iv, 0, iv.Length);
            cipher.IV = iv;
        }

        public ICryptoTransform CreateEncryptor()
        {
            return cipher.CreateEncryptor();
        }

        public ICryptoTransform CreateDecryptor()
        {
            return cipher.CreateDecryptor();
        }

        public void Dispose()
        {
            cipher.Dispose();
        }
    }
}
