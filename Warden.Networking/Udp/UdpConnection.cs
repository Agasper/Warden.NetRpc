using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Udp.Messages;
using Warden.Networking.Udp.Channels;
using Warden.Networking.Udp.Exceptions;
using Warden.Networking.Udp.Events;

namespace Warden.Networking.Udp
{
    public enum UdpConnectionStatus
    {
        Waiting = 0,
        Connecting = 1,
        Connected = 2,
        Disconnecting = 3,
        Disconnected = 4
    }

    public partial class UdpConnection : IChannelConnection, IDisposable
    {
        public UdpPeer Parent => peer;
        public long ConnectionId { get; private set; }
        public int Mtu { get; private set; } = INITIAL_MTU;
        public UdpConnectionStatistics Statistics { get; private set; }
        public UdpConnectionStatus Status { get; private set; }
        public UdpNetEndpoint EndPoint { get; private set; }
        public object Tag { get; set; }

        UdpPeer peer;
        ILogger logger;
        ConcurrentDictionary<ChannelDescriptor, IChannel> channels;

        DateTime lastStatusChange;
        DateTime lastStatusPacketSent;
        ushort lastPingSequence = 0;
        DateTime lastPingSent;
        DateTime connectionTimeoutDeadline;

        float? latency;
        float? avgLatency;

        TaskCompletionSource<object> connectTcs;
        TaskCompletionSource<object> disconnectTcs;

        Datagram disconnectReq;
        Datagram connectReq;

        public UdpConnection(UdpPeer peer)
        {
            Random rnd = new Random();
            this.ConnectionId = peer.GetNextConnectionId();
            this.Status = UdpConnectionStatus.Waiting;
            this.fragments = new ConcurrentDictionary<ushort, FragmentHolder>();
            this.channels = new ConcurrentDictionary<ChannelDescriptor, IChannel>(new ChannelDescriptor.EqualityComparer());
            this.lastStatusChange = DateTime.UtcNow;
            this.peer = peer;
            this.logger = peer.Configuration.LogManager.GetLogger(nameof(UdpConnection));
            this.logger.Meta["kind"] = this.GetType().Name;
            this.Statistics = new UdpConnectionStatistics();
        }

        public virtual void Dispose()
        {
            var disconnectReq_ = disconnectReq;
            if (disconnectReq_ != null)
            {
                disconnectReq_.Dispose();
                disconnectReq = null;
            }

            var connectReq_ = connectReq;
            if (connectReq_ != null)
            {
                connectReq_.Dispose();
                connectReq = null;
            }

            foreach (var channel in channels.Values)
            {
                channel.Dispose();
            }
        }

        protected virtual void OnRawMessage(MessageInfo messageInfo)
        {

        }

        protected virtual void OnStatusChanged(ConnectionStatusChangedEventArgs args)
        {

        }

        protected virtual void OnConnectionClosed(ConnectionClosedEventArgs args)
        {

        }

        protected virtual void OnConnectionOpened(ConnectionOpenedEventArgs args)
        {

        }

        internal bool IsPurgeable
        {
            get
            {
                if (Status == UdpConnectionStatus.Disconnected &&
                    (DateTime.UtcNow - lastStatusChange).TotalMilliseconds > peer.Configuration.ConnectionLingerTimeout)
                    return true;
                return false;
            }
        }

        void ChangeStatus(UdpConnectionStatus status)
        {
            var oldStatus = this.Status;
            if (oldStatus != status)
            {
                this.Status = status;
                this.logger.Info($"{this} status changed to {status}");
                this.lastStatusChange = DateTime.UtcNow;
                UpdateTimeoutDeadline();

                if (Status == UdpConnectionStatus.Connected)
                {
                    var connectTcs_ = this.connectTcs;
                    if (connectTcs_ != null)
                        connectTcs_.TrySetResult(null);

                    this.lastPingSent = DateTime.UtcNow;

                    if (peer.Configuration.AutoMtuExpand)
                        ExpandMTU();

                    var openedArgs = new ConnectionOpenedEventArgs(this);
                    peer.Configuration.SynchronizeSafe(() =>
                    {
                        try
                        {
                            OnConnectionOpened(openedArgs);
                        }
                        catch(Exception ex)
                        {
                            logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionOpened)}: {ex}");
                        }

                        peer.OnConnectionOpenedInternalSynchronized(openedArgs);
                    }, logger);

                }

                var statusChangedArgs = new ConnectionStatusChangedEventArgs(this, status);
                peer.Configuration.SynchronizeSafe(() =>
                {
                    try
                    {
                        OnStatusChanged(statusChangedArgs);
                    }
                    catch(Exception ex)
                    {
                        logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnStatusChanged)}: {ex}");
                    }

                    peer.OnConnectionStatusChangedSynchronized(statusChangedArgs);
                }, logger);
            }
        }

        void UpdateTimeoutDeadline()
        {
            this.connectionTimeoutDeadline = DateTime.UtcNow.AddMilliseconds(peer.Configuration.ConnectionTimeout);
        }

        void IChannelConnection.UpdateTimeoutDeadline()
        {
            UpdateTimeoutDeadline();
        }

        internal void Init(UdpNetEndpoint udpNetEndpoint)
        {
            this.EndPoint = udpNetEndpoint;
            UpdateTimeoutDeadline();
            logger.Debug($"{this} initialized");
        }

        internal void StartConnect(UdpRawMessage payload)
        {
            if (!CheckStatus(UdpConnectionStatus.Waiting))
                throw new InvalidOperationException($"Couldn't connect wrong status: {Status}, expected {UdpConnectionStatus.Waiting}");
            logger.Info($"Connecting to {EndPoint.EndPoint}");
            ChangeStatus(UdpConnectionStatus.Connecting);
            lastStatusPacketSent = DateTime.UtcNow;
            if (payload == null)
                connectReq = CreateSpecialDatagram(MessageType.ConnectReq);
            else
            {
                connectReq = CreateSpecialDatagram(MessageType.ConnectReq, payload);
                payload.Dispose();
            }
            connectReq.DontDisposeOnSend = true;
            SendDatagramAsync(connectReq);
        }

        internal async Task Connect()
        {
            try
            {
                connectTcs = new TaskCompletionSource<object>( TaskCreationOptions.RunContinuationsAsynchronously);

                StartConnect(null);

                await connectTcs.Task;
            }
            finally
            {
                connectTcs = null;
            }
        }

        internal async Task Connect(UdpRawMessage payload)
        {
            try
            {
                connectTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                StartConnect(payload);

                await connectTcs.Task;
            }
            finally
            {
                connectTcs = null;
            }
        }

        internal void OnDatagram(Datagram datagram)
        {
            if (datagram.ConnectionKey != this.EndPoint.ConnectionKey)
            {
                logger.Warn($"Got wrong {datagram}: connection id mismatch");
                datagram.Dispose();
                return;
            }

            Statistics.PacketIn();
            Statistics.BytesIn(datagram.GetTotalSize());

            switch (datagram.Type)
            {
                case MessageType.ConnectReq:
                    OnConnectReq(datagram); break;
                case MessageType.ConnectResp:
                    OnConnectResp(datagram); break;
                case MessageType.DisconnectReq:
                    OnDisconnectReq(datagram); break;
                case MessageType.DisconnectResp:
                    OnDisconnectResp(datagram); break;
                case MessageType.Ping:
                    OnPing(datagram); break;
                case MessageType.Pong:
                    OnPong(datagram); break;
                case MessageType.ExpandMTURequest:
                    OnMtuExpand(datagram); break;
                case MessageType.ExpandMTUSuccess:
                    OnMtuSuccess(datagram); break;
                case MessageType.DeliveryAck:
                    OnAckReceived(datagram); break;
                default:
                    OnDataReceived(datagram); break;
            }   
        }

        void OnDisconnectReq(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;

            SendDatagramAsync(CreateSpecialDatagram(MessageType.DisconnectResp));
            CloseInternal(DisconnectReason.ClosedByOtherPeer, datagram.ConvertToMessage());
            datagram.Dispose();
        }

        void OnDisconnectResp(Datagram datagram)
        {
            CloseInternal(DisconnectReason.ClosedByThisPeer, null);
            datagram.Dispose();
        }

        void SendDisconnectResp()
        {
            SendDatagramAsync(CreateSpecialDatagram(MessageType.DisconnectResp));
        }

        void OnConnectReq(Datagram datagram)
        {
            switch(Status)
            {
                case UdpConnectionStatus.Connected:
                    logger.Debug($"Got late {datagram.Type} resend connect resp");
                    SendDatagramAsync(CreateSpecialDatagram(MessageType.ConnectResp));
                    break;
                case UdpConnectionStatus.Waiting:
                    var connectionAccepted = peer.OnAcceptConnection(new OnAcceptConnectionEventArgs(this.EndPoint.EndPoint, datagram.ConvertToMessage()));
                    if (connectionAccepted)
                    {
                        logger.Debug($"Accepting connection {this}");
                        SendDatagramAsync(CreateSpecialDatagram(MessageType.ConnectResp));
                        ChangeStatus(UdpConnectionStatus.Connected);
                    }
                    else
                    {
                        logger.Debug($"Rejecting connection {this}");
                        SendDatagramAsync(CreateSpecialDatagram(MessageType.DisconnectResp));
                        ChangeStatus(UdpConnectionStatus.Disconnected);
                    }
                    break;
                case UdpConnectionStatus.Disconnected:
                    logger.Debug($"Got late {datagram.Type}, but connection is closed. Resend disconnect resp");
                    SendDatagramAsync(CreateSpecialDatagram(MessageType.DisconnectResp));
                    break;
                default:
                    logger.Debug($"Got {datagram.Type} in wrong connection status: {Status}. Dropping...");
                    break;
            }
            
            datagram.Dispose();
        }

        void OnConnectResp(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connecting))
                return;

            ChangeStatus(UdpConnectionStatus.Connected);
            connectReq.Dispose();
            connectReq = null;
            datagram.Dispose();
        }

        void SendConnectResp()
        {
            SendDatagramAsync(CreateSpecialDatagram(MessageType.ConnectResp));
        }

        void OnAckReceived(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;
            GetOrAddChannel(datagram.GetChannelDescriptor()).OnAckReceived(datagram);
        }

        void OnDataReceived(Datagram datagram)
        {
            if (!CheckStatus(datagram, UdpConnectionStatus.Connected))
                return;
            GetOrAddChannel(datagram.GetChannelDescriptor()).OnDatagram(datagram);
        }

        bool CheckStatus(params UdpConnectionStatus[] status)
        {
            bool ok = false;
            foreach (var s in status)
                ok |= s == this.Status;

            return ok;
        }

        bool CheckStatus(Datagram datagram, params UdpConnectionStatus[] status)
        {
            if (!CheckStatus(status))
            {
                string expected = string.Join(",", status.Select(s => s.ToString()));
                logger.Debug($"Got {datagram.Type} in wrong connection status: {Status}, expected: {expected}. Dropping...");
                datagram.Dispose();
                return false;
            }
            return true;
        }

        void IChannelConnection.ReleaseDatagram(Datagram datagram)
        {
            if (datagram.IsFragmented)
                ManageFragment(datagram);
            else
            {
                ReleaseMessage(datagram.ConvertToMessage(), datagram.DeliveryType, datagram.Channel);
                datagram.Dispose();
            }
        }

        void ReleaseMessage(UdpRawMessage message, DeliveryType deliveryType, int channel)
        {
            message.Position = 0;
            peer.Configuration.SynchronizeSafe(() =>
            {
                try
                {
                    OnRawMessage(new MessageInfo(message, deliveryType, channel));
                }
                catch(Exception ex)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnRawMessage)}: {ex}");
                }
            }, logger);
        }

        IChannel GetOrAddChannel(ChannelDescriptor descriptor)
        {
            IChannel result = channels.GetOrAdd(descriptor, (desc) => {
                switch (desc.DeliveryType)
                {
                    case DeliveryType.ReliableOrdered:
                        return new ReliableChannel(peer.Configuration.MemoryStreamPool, peer.Configuration.LogManager, desc, this, true);
                    case DeliveryType.ReliableUnordered:
                        return new ReliableChannel(peer.Configuration.MemoryStreamPool, peer.Configuration.LogManager, desc, this, false);
                    case DeliveryType.Unreliable:
                        return new UnreliableChannel(peer.Configuration.MemoryStreamPool, peer.Configuration.LogManager, desc, this);
                    case DeliveryType.UnreliableSequenced:
                        return new UnreliableSequencedChannel(peer.Configuration.MemoryStreamPool, peer.Configuration.LogManager, desc, this);
                    default:
                        throw new ArgumentException("Got datagram with unknown delivery type");
                }
            });

            return result;
        }

        internal void PollEvents()
        {
            Statistics.PollEvents();

            if (Status == UdpConnectionStatus.Disconnected)
                return;

            if (Status == UdpConnectionStatus.Connecting && DoesNeedResend() && connectReq != null)
            {
                SendDatagramAsync(connectReq);
                lastStatusPacketSent = DateTime.UtcNow;
            }

            if (Status == UdpConnectionStatus.Disconnecting && DoesNeedResend() && disconnectReq != null)
            {
                SendDatagramAsync(disconnectReq);
                lastStatusPacketSent = DateTime.UtcNow;
            }

            if (Status == UdpConnectionStatus.Connected)
            {
                if ((DateTime.UtcNow - lastPingSent).TotalMilliseconds > 1000)
                    SendPing();
                MtuCheck();
            }

            foreach (var channel in channels.Values)
                channel.PollEvents();


            if (Status != UdpConnectionStatus.Disconnected &&
                DateTime.UtcNow >= this.connectionTimeoutDeadline)
            {
                CloseInternal(DisconnectReason.Timeout, null);
            }
        }

        bool DoesNeedResend()
        {
            if ((DateTime.UtcNow - this.lastStatusPacketSent).TotalMilliseconds > this.GetInitialResendDelay())
                return true;
            return false;
        }

        public void CloseImmidiately()
        {
            this.CloseInternal(DisconnectReason.ClosedByThisPeer, null);
        }

        internal void CloseImmidiately(DisconnectReason reason)
        {
            this.CloseInternal(reason, null);
        }

        void CloseInternal(DisconnectReason reason, UdpRawMessage payload)
        {
            if (Status == UdpConnectionStatus.Disconnected)
                return;
            ChangeStatus(UdpConnectionStatus.Disconnected);

            logger.Trace($"{this} closing with ({reason})");
            var disconnectReq_ = disconnectReq;
            if (disconnectReq_ != null)
            {
                disconnectReq_.Dispose();
                disconnectReq = null;
            }
            var args = new ConnectionClosedEventArgs(this, reason, payload);
            logger.Trace($"{this} closing pre event syncronization");
            peer.Configuration.SynchronizeSafe(() =>
            {
                logger.Trace($"{this} firing event connection OnConnectionClosed");

                try
                {
                    OnConnectionClosed(args);
                }
                catch(Exception ex)
                {
                    logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnConnectionClosed)}: {ex}");
                }

                logger.Trace($"{this} firing event peer OnConnectionClosed");
                peer.OnConnectionClosedInternalSynchronized(args);
                logger.Trace($"{this} events fired");
            }, logger);

            logger.Trace($"{this} closing post event syncronization");

            var connectTcs_ = this.connectTcs;
            if (connectTcs_ != null)
                connectTcs_.TrySetException(new ConnectionFailed(reason));
            var disconnectTcs_ = this.disconnectTcs;
            if (disconnectTcs_ != null)
                disconnectTcs_.TrySetResult(null);

            logger.Info($"{this} closed ({reason})");
        }

        public async Task CloseAsync()
        {
            try
            {
                disconnectTcs = new TaskCompletionSource<object>( TaskCreationOptions.RunContinuationsAsynchronously);

                Close();

                await disconnectTcs.Task;
            }
            finally
            {
                disconnectTcs = null;
            }
        }

        public virtual async Task CloseAsync(UdpRawMessage payload)
        {
            try
            {
                disconnectTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                Close(payload);

                await disconnectTcs.Task;
            }
            finally
            {
                disconnectTcs = null;
            }
        }

        public void Close()
        {
            Close(null);
        }

        public virtual void Close(UdpRawMessage payload)
        {
            if (Status >= UdpConnectionStatus.Disconnecting)
                return;
            ChangeStatus(UdpConnectionStatus.Disconnecting);
            Datagram disconnectReq;
            if (payload == null)
                disconnectReq = CreateSpecialDatagram(MessageType.DisconnectReq);
            else
                disconnectReq = CreateSpecialDatagram(MessageType.DisconnectReq, payload);
            disconnectReq.DontDisposeOnSend = true;
            this.disconnectReq = disconnectReq;
            lastStatusPacketSent = DateTime.UtcNow;
            SendDatagramAsync(disconnectReq);
            payload?.Dispose();
        }

        int IChannelConnection.GetInitialResendDelay()
        {
            return GetInitialResendDelay();
        }

        int GetInitialResendDelay()
        {
            if (latency.HasValue)
            {
                int doubleLatency = (int)(avgLatency.Value * 3);
                if (doubleLatency < 100)
                    doubleLatency = 100;
                return doubleLatency;
            }
            else
            {
                return 100;
            }
        }

        Datagram CreateSpecialDatagram(MessageType messageType)
        {
            var datagram = Datagram.CreateEmpty(peer.Configuration.MemoryStreamPool);
            datagram.Type = messageType;
            return datagram;
        }

        Datagram CreateSpecialDatagram(MessageType messageType, int payloadSize)
        {
            var datagram = Datagram.CreateNew(peer.Configuration.MemoryStreamPool, payloadSize);
            datagram.Type = messageType;
            return datagram;
        }

        Datagram CreateSpecialDatagram(MessageType messageType, UdpRawMessage payload)
        {
            if (!CheckCanBeSendUnfragmented(payload))
                throw new ArgumentException($"Payload can be datagram only smaller than current MTU ({Mtu})");
            var datagram = Datagram.CreateNew(peer.Configuration.MemoryStreamPool, (int)payload.BaseStream.Length);
            datagram.Type = messageType;
            payload.BaseStream.Position = 0;
            payload.BaseStream.CopyTo(datagram.BaseStream);
            return datagram;
        }

        Task<SocketError> SendDatagramAsync(Datagram datagram)
        {
            datagram.ConnectionKey = this.EndPoint.ConnectionKey;
            Statistics.PacketOut();
            Statistics.BytesOut(datagram.GetTotalSize());
            return peer.SendDatagramAsync(this, datagram);
        }

        Task<SocketError> IChannelConnection.SendDatagramAsync(Datagram datagram)
        {
            return SendDatagramAsync(datagram);
        }

        public UdpSendStatus SendMessage(MessageInfo messageInfo)
        {
            if (!CheckStatus(UdpConnectionStatus.Connected))
            {
                messageInfo.Message.Dispose();
                return UdpSendStatus.Failed;
            }

            var descriptor = new ChannelDescriptor(messageInfo.Channel, messageInfo.DeliveryType);
            IChannel channel_ = GetOrAddChannel(descriptor);

            if (!CheckCanBeSendUnfragmented(messageInfo.Message))
            { //need split
                if (messageInfo.DeliveryType == DeliveryType.Unreliable || messageInfo.DeliveryType == DeliveryType.UnreliableSequenced)
                {
                    messageInfo.Message.Dispose();

                    if (peer.Configuration.TooLargeUnreliableMessageBehaviour == UdpPeerConfiguration.TooLargeMessageBehaviour.RaiseException)
                        throw new ArgumentException("You couldn't send fragmented message throught unreliable channel. You message below MTU limit or change delivery type");
                    else
                        return UdpSendStatus.Failed;

                }

                return SendFragmentedMessage(messageInfo.Message, channel_);
            }

            Datagram datagram = messageInfo.Message.ConvertToDatagram();
            datagram.Type = MessageType.UserData;
            datagram.Channel = messageInfo.Channel;
            datagram.ConnectionKey = this.EndPoint.ConnectionKey;
            datagram.DeliveryType = messageInfo.DeliveryType;
            var status = channel_.SendDatagram(datagram);
            messageInfo.Message.Dispose();

            return status;
        }

        public override string ToString()
        {
            return $"{nameof(UdpConnection)}[endpoint={EndPoint}]";
        }
    }
}
