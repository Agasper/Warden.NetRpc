namespace Warden.Rpc.Net.Tcp.Events
{
    public interface IRpcPeerEventListener
    {
        void OnSessionOpened(SessionOpenedEventArgs args);
        void OnSessionClosed(SessionClosedEventArgs args);
    }
}