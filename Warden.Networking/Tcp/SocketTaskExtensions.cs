using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Warden.Networking.Tcp
{
    static class SocketTaskExtensions
    {
        public static Task<int> SendAsync(
                    this Socket socket,
                    IList<ArraySegment<byte>> buffers,
                    SocketFlags socketFlags)
        {
            return Task<int>.Factory.FromAsync(
                (targetBuffers, flags, callback, state) => ((Socket)state).BeginSend(targetBuffers, flags, callback, state),
                asyncResult => ((Socket)asyncResult.AsyncState).EndSend(asyncResult),
                buffers,
                socketFlags,
                state: socket);
        }
    }
}
