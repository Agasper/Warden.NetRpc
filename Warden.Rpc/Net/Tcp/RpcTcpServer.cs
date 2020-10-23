using System;
using System.Collections.Generic;
using System.Linq;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Rpc.Net.Tcp.Events;

namespace Warden.Rpc.Net.Tcp
{
    //public delegate void DOnSessionClosed(SessionClosedEventArgs<T> args);
    //public delegate void DOnSessionOpened(SessionOpenedEventArgs<T> args);

    //public abstract class RpcTcpServer
    //{
    //    class InnerTcpServer : TcpServer
    //    {

    //    }

    //    public event DOnSessionClosed OnSessionClosedEvent;
    //    public event DOnSessionOpened OnSessionOpenedEvent;
    //    public IEnumerable<T> Sessions => this.Connections.Values.Select(c => (c as RpcTcpConnection<T>).Object);

    //    public new RpcTcpServerConfiguration Configuration { get; private set; }

    //    public RpcTcpServer(RpcTcpServerConfiguration configuration) : base(configuration)
    //    {
    //        this.Configuration = configuration;
    //    }

    //    protected sealed override TcpConnection CreateConnection()
    //    {
    //        return new RpcTcpConnection<T>(this);
    //    }

    //    internal T CreateSessionInternal() => CreateSession();

    //    protected abstract T CreateSession();

    //    protected sealed override void OnConnectionOpened(ConnectionOpenedEventArgs args)
    //    {
    //        OnSessionStarted(new SessionOpenedEventArgs<T>(args.Connection as RpcTcpConnection<T>));
    //    }

    //    protected sealed override void OnConnectionClosed(ConnectionClosedEventArgs args)
    //    {
    //        OnSessionClosed(new SessionClosedEventArgs<T>(args.Connection as RpcTcpConnection<T>));
    //    }

    //    protected virtual void OnSessionStarted(SessionOpenedEventArgs<T> args)
    //    {

    //    }

    //    protected virtual void OnSessionClosed(SessionClosedEventArgs<T> args)
    //    {

    //    }
    //}
}
