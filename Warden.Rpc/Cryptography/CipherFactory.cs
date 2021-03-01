using Warden.Networking.Cryptography;

namespace Warden.Rpc.Cryptography
{
    interface ICipherFactory
    {
        ICipher CreateNewCipher();
    }
    
    class CipherFactory<T> : ICipherFactory where T : ICipher, new()
    {
        public CipherFactory()
        {
        }

        public ICipher CreateNewCipher()
        {
            return new T();
        }
    }
}