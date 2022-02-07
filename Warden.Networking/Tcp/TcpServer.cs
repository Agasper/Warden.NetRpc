using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Warden.Logging;
using Warden.Util;

namespace Warden.Networking.Tcp
{
    public class TcpServer : TcpPeer
    {
        public IReadOnlyDictionary<long, TcpConnection> Connections
        {
            get
            {
                return connections;
            }
        }

        private protected override ILogger Logger => logger;

        ILogger logger;
        ConcurrentDictionary<long, TcpConnection> connections;
#if NET50
        IResettableCachedEnumerable<KeyValuePair<long, TcpConnection>> connectionsEnumerator;
#endif 
        Socket serverSocket;
        new TcpConfigurationServer configuration;
        long connectionId = 0;
        bool listening;

        public TcpServer(TcpConfigurationServer configuration) : base(configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.AcceptThreads < 1 || configuration.AcceptThreads > 10)
                throw new ArgumentOutOfRangeException($"{nameof(configuration.AcceptThreads)} should be in range 1-10");
            this.connections = new ConcurrentDictionary<long, TcpConnection>(
                configuration.AcceptThreads / 2 + 1,
                Math.Min(configuration.MaximumConnections, 101));
#if NET50
            this.connectionsEnumerator =
                new ResettableCachedEnumerator<KeyValuePair<long, TcpConnection>>(this.connections);
#endif
            this.configuration = configuration;
            this.logger = configuration.LogManager.GetLogger(nameof(TcpServer));
            this.logger.Meta.Add("kind", this.GetType().Name);
        }

        public override void Shutdown()
        {
            if (serverSocket == null)
                return;

            logger.Info($"{nameof(TcpServer)} shutdown");

            listening = false;

            serverSocket.Close();
            serverSocket.Dispose();

            ClearConnections();

            base.Shutdown();
        }

        void ClearConnections()
        {
            foreach (var pair in connections)
            {
                pair.Value.Close();
            }
            connections.Clear();
        }

        private protected override void PollEventsInternal()
        {
#if NET50
            connectionsEnumerator.Reset();
            foreach(var pair in connectionsEnumerator)
                if (pair.Value.Connected)
                    pair.Value.PollEventsInternal();
#else
            foreach(var pair in connections)
                if (pair.Value.Connected)
                    pair.Value.PollEventsInternal();
#endif 
        }

        internal override void OnConnectionClosedInternal(TcpConnection tcpConnection)
        {
            if (!connections.TryRemove(tcpConnection.Id, out TcpConnection outVar))
            {
                logger.Error($"Couldn't remove connection from server {tcpConnection}. It doesn't exists");
            }

            base.OnConnectionClosedInternal(tcpConnection);
        }

        public void Listen(int port)
        {
            Listen(null, port);
        }

        public void Listen(string host, int port)
        {
            IPEndPoint myEndpoint;
            if (string.IsNullOrEmpty(host))
                myEndpoint = new IPEndPoint(IPAddress.Any, port);
            else
                myEndpoint = new IPEndPoint(IPAddress.Parse(host), port);

            Listen(myEndpoint);
        }

        public void Listen(IPEndPoint endPoint)
        {
            CheckStarted();
            if (serverSocket != null)
                throw new InvalidOperationException("Server already listening");

            serverSocket = GetNewSocket();
            //https://stackoverflow.com/questions/14388706/how-do-so-reuseaddr-and-so-reuseport-differ
            serverSocket.Bind(endPoint);
            serverSocket.Listen(configuration.ListenBacklog);

            for (int i = 0; i < configuration.AcceptThreads; i++)
            {
                StartAccept();
            }

            listening = true;

            logger.Debug($"Listening on {endPoint}");
        }

        void StartAccept()
        {
            if (serverSocket == null)
                return;

            serverSocket.BeginAccept(AcceptCallback, null);
        }

        protected virtual bool AcceptConnection(Socket socket)
        {
            return true;
        }

        void AcceptCallback(IAsyncResult asyncResult)
        {
            try
            {
                if (listening)
                    StartAccept();

                logger.Debug("Accepting new connection...");
                Socket connSocket = serverSocket.EndAccept(asyncResult);

                if (!listening || !AcceptConnection(connSocket))
                {
                    connSocket.Close();
                    return;
                }

                if (connections.Count >= configuration.MaximumConnections)
                {
                    connSocket.Close();
                    logger.Warn("New connection rejected due to max connection limit");
                    return;
                }

                // Finish Accept
                SetSocketOptions(connSocket);
                long newId = Interlocked.Increment(ref connectionId);
                TcpConnection connection = this.CreateConnection();
                connection.CheckParent(this);
                connection.Init(newId, connSocket, false);

                if (!connections.TryAdd(newId, connection))
                {
                    logger.Error("New connection rejected. Blocking collection reject addition.");
                    connection.Dispose();
                    return;
                }

                connection.StartReceive();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                logger.Error($"{nameof(TcpServer)} encountered exception on accepting thread: {ex}");
            }
        }
    }
}
