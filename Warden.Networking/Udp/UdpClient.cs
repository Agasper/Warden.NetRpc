using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Udp.Events;
using Warden.Networking.Udp.Exceptions;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp
{
    public class UdpClient : UdpPeer
    {
        public UdpConnectionStatistics Statistics => Connection?.Statistics;
        public new UdpConfigurationClient Configuration => configuration;
        public UdpConnectionStatus Status
        {
            get
            {
                var conn = this.Connection;
                if (conn == null)
                    return UdpConnectionStatus.Waiting;
                return conn.Status;
            }
        }
        public UdpConnection Connection { get; private set; }
        readonly UdpConfigurationClient configuration;

        private protected override ILogger Logger => logger;

        ILogger logger;

        public UdpClient(UdpConfigurationClient configuration) : base(configuration)
        {
            this.configuration = configuration;
            this.logger = configuration.LogManager.GetLogger(nameof(UdpClient));
            this.logger.Meta["kind"] = this.GetType().Name;
        }
        
        internal override void OnConnectionClosedInternalSynchronized(ConnectionClosedEventArgs args)
        {
            base.OnConnectionClosedInternalSynchronized(args);

            args.Connection.Dispose();
            this.Connection = null;
        }

        private protected override void PollEventsInternal()
        {
            base.PollEventsInternal();
            Connection?.PollEvents();
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

        public Task ConnectAsync(IPEndPoint endpoint)
        {
            if (this.Connection != null)
                throw new ConnectionFailed(DisconnectReason.Error, "Already connected");
            DestroySocket();
            Bind(new IPEndPoint(IPAddress.Any, 0));
            var connection = CreateConnection();
            Random rnd = new Random();
            connection.Init(new UdpNetEndpoint(endpoint, (ushort)rnd.Next(0, ushort.MaxValue)), true);
            Connection = connection;
            return connection.Connect();
        }

        public void Disconnect()
        {
            this.Connection?.Close();
        }

        private protected override void OnDatagram(Datagram datagram, UdpNetEndpoint remoteEndpoint)
        {
            Connection?.OnDatagram(datagram);
        }
    }
}
