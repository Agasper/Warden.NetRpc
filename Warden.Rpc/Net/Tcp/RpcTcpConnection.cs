using System;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc;

namespace Warden.Rpc.Net.Tcp
{
    //public class RpcTcpConnection<T> : TcpConnection, IRpcConnection
    //{
    //    public T Object => obj;

    //    T obj;
    //    RpcTcpSession session;
    //    RpcSerializer serializer;

    //    public RpcTcpConnection(RpcTcpServer<T> parent) : base(parent)
    //    {
    //        RpcConfiguration rpcConfiguration = new RpcConfiguration();
    //        rpcConfiguration.Connection = this;
    //        rpcConfiguration.DefaultExecutionTimeout = parent.Configuration.DefaultExecutionTimeout;
    //        rpcConfiguration.OrderedExecution = false;
    //        rpcConfiguration.Serializer = parent.Configuration.Serializer;
    //        serializer = parent.Configuration.Serializer;
    //        obj = parent.CreateSessionInternal();
    //        session = new RpcTcpSession(obj, rpcConfiguration);
    //    }

    //    public void SendMessage(ICustomMessage message, SendingOptions sendingOptions)
    //    {
    //        TcpRawMessage rawMessage = Parent.CreateMessage();
    //        using (WardenStreamWriter sw = new WardenStreamWriter(rawMessage.BaseStream, true))
    //        {
    //            message.WriteTo(new WriteFormatterInfo(sw, serializer));
    //        }

    //        this.SendMessageAsync(rawMessage);
    //    }

    //    public void SendMessage(ICustomMessage message)
    //    {
    //        this.SendMessage(message, SendingOptions.Default);
    //    }

    //    protected override void OnMessageReceived(MessageEventArgs args)
    //    {
    //        using (WardenStreamReader reader = new WardenStreamReader(args.Message.BaseStream, false))
    //        {
    //            session.OnMessage(reader);
    //        }
    //    }
    //}
}
