using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Warden.Logging;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp
{
    public class UdpServer : UdpPeer
    {
        public new UdpConfigurationServer Configuration => configuration;
        public IReadOnlyDictionary<UdpNetEndpoint, UdpConnection> Connections
        {
            get
            {
                return connections;
            }
        }
        UdpConfigurationServer configuration;

        private protected override ILogger Logger => logger;


        ILogger logger;
        ConcurrentDictionary<UdpNetEndpoint, UdpConnection> connections;

        public UdpServer(UdpConfigurationServer configuration) : base(configuration)
        {
            this.configuration = configuration;
            this.connections = new ConcurrentDictionary<UdpNetEndpoint, UdpConnection>();
            this.logger = configuration.LogManager.GetLogger(nameof(UdpServer));
            this.logger.Meta["kind"] = this.GetType().Name;
        }
        
        public virtual void Listen(int port)
        {
            CheckStarted();
            Bind(null, port);
            this.logger.Info($"Listening on :{port}");
        }

        public virtual void Listen(string ip, int port)
        {
            CheckStarted();
            Bind(ip, port);
            this.logger.Info($"Listening on {ip}:{port}");
        }

        public virtual void Listen(IPEndPoint endPoint)
        {
            CheckStarted();
            Bind(endPoint);
            this.logger.Info($"Listening on {endPoint}");
        }

        public override void Shutdown()
        {
            base.Shutdown();
            foreach(var connection in connections.Values)
            {
                connection.CloseImmidiately(DisconnectReason.ClosedByThisPeer);
            }
            connections.Clear();
        }

        protected virtual bool AcceptConnection(Socket socket)
        {
            return true;
        }

        private protected override void OnDatagram(Datagram datagram, UdpNetEndpoint remoteEndpoint)
        {
            if (this.connections.TryGetValue(remoteEndpoint, out UdpConnection connection))
            {
                connection.OnDatagram(datagram);
                return;
            }

            UdpConnection newConnection = this.CreateConnection();
            try
            {
                newConnection.Init(remoteEndpoint, false);
                newConnection.OnDatagram(datagram);
            }
            catch(Exception ex)
            {
                logger.Warn($"Couldn't init connection {newConnection}. {ex}");
                return;
            }

            if (!connections.TryAdd(remoteEndpoint, newConnection))
            {
                newConnection.CloseImmidiately(DisconnectReason.Error);
                Logger.Error($"Couldn't add connection {newConnection}");
            }
        }

        private protected override void PollEventsInternal()
        {
            base.PollEventsInternal();

            foreach(var pair in connections)
            {
                try
                {
                    pair.Value.PollEvents();
                }
                catch(Exception ex)
                {
                    logger.Error($"{nameof(UdpConnection)}.PollEvents() got an unhandled exception: {ex}");
                    pair.Value.CloseImmidiately(DisconnectReason.Error);
                }

                if(pair.Value.IsPurgeable)
                {
                    if (!connections.TryRemove(pair.Key, out UdpConnection removed))
                        throw new InvalidOperationException($"Could not remove dead connection {pair.Value}");
                    logger.Debug($"Connection removed from server {pair.Value}");
                    pair.Value.Dispose();
                }
            }
        }
    }
}
