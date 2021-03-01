namespace Warden.Rpc.Net
{
    public interface ISessionFactory
    {
        RpcSession CreateSession(RpcSessionContext context);
    }
}
