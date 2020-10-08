using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp
{
    struct SendDatagramAsyncResult
    {
        public UdpConnection Connection { get; private set; }
        public Datagram Datagram { get; private set; }
        public Task<SocketError> Task => tcs.Task;

        TaskCompletionSource<SocketError> tcs;

        public SendDatagramAsyncResult(UdpConnection connection, Datagram datagram)
        {
            this.Datagram = datagram;
            this.Connection = connection;
            this.tcs = new TaskCompletionSource<SocketError>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void SetComplete(SocketError socketError)
        {
            tcs?.TrySetResult(socketError);
        }

        public void SetException(Exception exception)
        {
            tcs?.TrySetException(exception);
        }
    }
}
