using System;
using Warden.Networking.IO;
using Warden.Networking.Tcp;
using Warden.Networking.Tcp.Events;
using Warden.Networking.Tcp.Messages;
using Warden.Rpc;
//using static Warden.Rpc.Net.Tcp.RpcTcpServer;

namespace Warden.Rpc.Net.Tcp
{
    //public class RpcTcpConnection : TcpConnection, IRpcConnection
    //{
    //    RpcTcpSession session;
    //    RpcSerializer serializer;
    //    RpcTcpServerConfiguration configuration;

    //    internal RpcTcpConnection(InnerTcpServer parent, RpcTcpServerConfiguration configuration) : base(parent)
    //    {
    //        this.configuration = configuration;

    //        RpcConfiguration rpcConfiguration = new RpcConfiguration();
    //        rpcConfiguration.Connection = this;
    //        rpcConfiguration.DefaultExecutionTimeout = configuration.DefaultExecutionTimeout;
    //        rpcConfiguration.OrderedExecution = configuration.OrderedExecution;
    //        rpcConfiguration.OrderedExecutionMaxQueue = configuration.OrderedExecutionMaxQueue;
    //        rpcConfiguration.Serializer = configuration.Serializer;
    //        serializer = configuration.Serializer;
    //        var obj = configuration.SessionFactory.CreateSession(this);
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
