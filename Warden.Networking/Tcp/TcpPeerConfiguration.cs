using System;
using System.Net.Sockets;
using System.Threading;
using Warden.Logging;
using Warden.Util.Buffers;

namespace Warden.Networking.Tcp
{
    public abstract class TcpPeerConfiguration
    {
        public ConnectionSimulation ConnectionSimulation { get => connectionSimulation; set { CheckLocked(); connectionSimulation = value; } }
        public LingerOption LingerOption { get => lingerOption; set { CheckLocked(); CheckNull(value); lingerOption = value; } }
        public bool NoDelay { get => noDelay; set { CheckLocked(); noDelay = value; } }
        public bool ReuseAddress { get => reuseAddress; set { CheckLocked(); reuseAddress = value; } }
        public MemoryStreamPool MemoryStreamPool { get => memoryStreamPool; set { CheckLocked(); CheckNull(value); memoryStreamPool = value; } }
        public int BufferSize { get => bufferSize; set { CheckLocked(); bufferSize = value; } }
        public ILogManager LogManager { get => logManager; set { CheckLocked(); CheckNull(value); logManager = value; } }
        public ContextSynchronizationMode ContextSynchronizationMode { get => contextSynchronizationMode; set { CheckLocked(); contextSynchronizationMode = value; } }

        public bool KeepAliveEnabled { get => keepAliveEnabled; set { CheckLocked(); keepAliveEnabled = value; } }
        public int KeepAliveInterval { get => keepAliveInterval; set { CheckLocked(); keepAliveInterval = value; } }
        public int KeepAliveTimeout { get => keepAliveTimeout; set { CheckLocked(); keepAliveTimeout = value; } }

        //internal SynchronizationContext SyncronizationContext => syncronizationContext;

        protected int bufferSize;
        protected bool noDelay;
        protected MemoryStreamPool memoryStreamPool;
        protected LingerOption lingerOption;
        protected ILogManager logManager;
        protected bool keepAliveEnabled;
        protected int keepAliveInterval;
        protected int keepAliveTimeout;
        protected bool reuseAddress;

        protected ConnectionSimulation connectionSimulation;
        protected SynchronizationContext syncronizationContext;
        protected ContextSynchronizationMode contextSynchronizationMode;

        protected bool locked;

        internal void Lock()
        {
            locked = true;
        }

        protected void CheckLocked()
        {
            if (locked)
                throw new InvalidOperationException("Configuration is locked in read only mode");
        }

        protected void CheckNull(object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
        }

        public virtual void CaptureSynchronizationContext()
        {
            CheckLocked();
            if (SynchronizationContext.Current == null)
                throw new ArgumentNullException("Synchronization context is null");
            this.syncronizationContext = SynchronizationContext.Current;
        }

        public virtual void SetSynchronizationContext(SynchronizationContext context)
        {
            CheckLocked();
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            this.syncronizationContext = context;
        }

        internal void SynchronizeSafe(Action callback, ILogger logger)
        {
            ContextSynchronizationHelper.SynchronizeSafe(this.syncronizationContext, this.contextSynchronizationMode,
                callback, logger);
        }

        public TcpPeerConfiguration()
        {
            syncronizationContext = new SynchronizationContext();

            memoryStreamPool = MemoryStreamPool.Shared;
            bufferSize = ushort.MaxValue;
            noDelay = true;
            logManager = Logging.LogManager.Dummy;
            lingerOption = new LingerOption(true, 15);

            keepAliveEnabled = true;
            keepAliveInterval = 1000;
            keepAliveTimeout = Timeout.Infinite;
        }
    }
}
