namespace Warden.Rpc.Net.Tcp.Events
{
    public interface IRpcPeer
    {
        RpcSession CreateSession(RpcSessionContext context);
        void OnSessionOpened(SessionOpenedEventArgs args);
        void OnSessionClosed(SessionClosedEventArgs args);
    }
}