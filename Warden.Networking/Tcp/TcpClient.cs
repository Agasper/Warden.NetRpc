using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Tcp.Events;
using Warden.Util;

namespace Warden.Networking.Tcp
{
    public delegate void DOnstatusChanged(ClientStatusChangedEventArgs args);

    public class TcpClient : TcpPeer
    {
        public event DOnstatusChanged OnStatusChangedEvent;
        public TcpConnectionStatus Status => status;
        public virtual bool Connected => this.status == TcpConnectionStatus.Connected;
        public TcpConnection Connection { get; private set; }

        public enum TcpConnectionStatus
        {
            Disconnected = 0,
            Connecting = 1,
            Connected = 2
        }

        private protected override ILogger Logger => logger;

        new TcpClientConfiguration configuration;
        TcpConnectionStatus status;
        long connectionId;
        Socket clientSocket;
        ILogger logger;
        bool canReconnect;

        DateTime reconnectTimerStartFrom;
        IPEndPoint lastEndpoint;

        public TcpClient(TcpClientConfiguration configuration) : base(configuration)
        {
            this.configuration = configuration;
            this.logger = configuration.LogManager.GetLogger(nameof(TcpClient));
            this.logger.Meta.Add("kind", this.GetType().Name);
            status = TcpConnectionStatus.Disconnected;
        }

        void ChangeStatus(TcpConnectionStatus newStatus)
        {
            if (status == newStatus)
                return;


            if (newStatus == TcpConnectionStatus.Disconnected)
                reconnectTimerStartFrom = DateTime.UtcNow;

            status = newStatus;

            logger.Info($"{nameof(TcpClient)} status changed to {newStatus}");

            configuration.SynchronizeSafe(() => {
                ClientStatusChangedEventArgs args = new ClientStatusChangedEventArgs(newStatus);
                try
                {
                    OnStatusChanged(args);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnStatusChanged)}: {ex}");
                }
                try
                {
                    OnStatusChangedEvent?.Invoke(args);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unhandled exception on {this.GetType().Name}.{nameof(OnStatusChangedEvent)}: {ex}");
                }

            }, logger);
        }

        protected override void PollEventsInternal()
        {
            Connection?.PollEventsInternal();

            if (status == TcpConnectionStatus.Disconnected &&
                IsStarted &&
                canReconnect &&
                configuration.AutoReconnect &&
                (DateTime.UtcNow - reconnectTimerStartFrom).TotalMilliseconds > configuration.AutoReconnectDelay)
            {
                if (!OnReconnecting())
                {
                    reconnectTimerStartFrom = DateTime.UtcNow;
                }
                else
                {
                    logger.Info($"Reconnecting to {lastEndpoint}...");
                    _ = ConnectAsync(lastEndpoint);
                }
            }
        }

        protected virtual void OnStatusChanged(ClientStatusChangedEventArgs args)
        {

        }

        protected virtual bool OnReconnecting()
        {
            return true;
        }

        public async Task ConnectAsync(string host, int port)
        {
            IPAddress ip = IPAddress.Any;
            if (IPAddress.TryParse(host, out IPAddress ip_))
            {
                ip = ip_;
            }
            else
            {
                logger.Debug($"Resolving {host} to ip address...");
                var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
                ip = addresses.First(addr => addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                logger.Debug($"Resolved {host} to {ip}");
            }

            IPEndPoint endpoint = new IPEndPoint(ip, port);
            await ConnectAsync(endpoint).ConfigureAwait(false);
        }

        public virtual async Task ConnectAsync(IPEndPoint endpoint)
        {
            CheckStarted();
            logger.Info($"{nameof(TcpClient)} is starting new connection to {endpoint}");
            Socket clientSocket = null;
            try
            {
                ChangeStatus(TcpConnectionStatus.Connecting);
                
                clientSocket = this.GetNewSocket();
                this.SetSocketOptions(clientSocket);
                await clientSocket.ConnectAsync(endpoint)
                    .TimeoutAfter(configuration.ConnectTimeout)
                    .ConfigureAwait(false);
                var connection = this.CreateConnection();
                connection.CheckParent(this);
                long newId = Interlocked.Increment(ref connectionId);
                connection.Init(newId, clientSocket);
                connection.StartReceive();

                if (!connection.Connected)
                    throw new InvalidOperationException("Connection reset");
                this.Connection = connection;
                ChangeStatus(TcpConnectionStatus.Connected);
                canReconnect = true;
            }
            catch
            {
                if (clientSocket != null)
                    clientSocket.Dispose();
                DisconnectInternal();

                throw;
            }
            finally
            {
                lastEndpoint = endpoint;
            }
        }

        internal override void OnConnectionClosedInternal(TcpConnection tcpConnection)
        {
            base.OnConnectionClosedInternal(tcpConnection);
            ChangeStatus(TcpConnectionStatus.Disconnected);
            Connection = null;
            tcpConnection.Dispose();
        }

        public override void Shutdown()
        {
            logger.Info($"{nameof(TcpClient)} shutdown");
            Disconnect();
            base.Shutdown();
        }

        void DisconnectInternal()
        {
            if (status == TcpConnectionStatus.Disconnected)
                return;

            var socket = clientSocket;
            if (socket != null)
            {
                socket.Dispose();
                clientSocket = null;
            }
            var connection = Connection;
            if (connection != null)
            {
                connection.Close();
                Connection = null;
            }
            ChangeStatus(TcpConnectionStatus.Disconnected);
        }

        public void Disconnect(bool stopAutoReconnecting = true)
        {
            canReconnect = !stopAutoReconnecting;
            DisconnectInternal();
        }
    }
}
