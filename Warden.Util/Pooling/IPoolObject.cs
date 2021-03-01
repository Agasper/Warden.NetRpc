namespace Warden.Util.Pooling
{
    public interface IPoolObject
    {
        void OnTookFromPool();
        void OnReturnToPool();
    }
}
