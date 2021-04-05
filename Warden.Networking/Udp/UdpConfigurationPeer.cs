using System;
using System.Threading;
using Warden.Logging;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp
{
    public class UdpConfigurationPeer
    {
        public enum TooLargeMessageBehaviour
        {
            Drop,
            RaiseException
        }

        public MemoryStreamPool MemoryStreamPool { get => memoryStreamPool; set { CheckLocked(); CheckNull(value); memoryStreamPool = value; } }
        public ConnectionSimulation ConnectionSimulation { get => connectionSimulation; set { CheckLocked(); connectionSimulation = value; } }
        public bool ReuseAddress { get => reuseAddress; set { CheckLocked(); reuseAddress = value; } }
        public int ConnectionTimeout { get => connectionTimeout; set { CheckLocked(); connectionTimeout = value; } }
        public int NetworkReceiveThreads { get => networkReceiveThreads; set { CheckLocked(); networkReceiveThreads = value; } }
        public ILogManager LogManager { get => logManager; set { CheckLocked(); CheckNull(value); logManager = value; } }
        public bool AutoMtuExpand { get => autoMtuExpand; set { CheckLocked(); autoMtuExpand = value; } }
        public int MtuExpandMaxFailAttempts { get => mtuExpandMaxFailAttempts; set { CheckLocked(); mtuExpandMaxFailAttempts = value; } }
        public int MtuExpandFrequency { get => mtuExpandFrequency; set { CheckLocked(); mtuExpandFrequency = value; } }
        public int LimitMtu { get => limitMtu; set { CheckLocked(); limitMtu = value; } }
        public int PingInterval { get => pingInterval; set { CheckLocked(); pingInterval = value; } }
        public int ConnectionLingerTimeout { get => connectionLingerTimeout; set { CheckLocked(); connectionLingerTimeout = value; } }
        public TooLargeMessageBehaviour TooLargeUnreliableMessageBehaviour { get => tooLargeMessageBehaviour; set { CheckLocked(); tooLargeMessageBehaviour = value; } }
        public ContextSynchronizationMode ContextSynchronizationMode { get => contextSynchronizationMode; set { CheckLocked(); contextSynchronizationMode = value; } }

        //internal SynchronizationContext SyncronizationContext => syncronizationContext;

        bool reuseAddress;
        MemoryStreamPool memoryStreamPool;
        ILogManager logManager;
        int connectionTimeout;
        int connectionLingerTimeout;
        int networkReceiveThreads;
        int mtuExpandMaxFailAttempts;
        int mtuExpandFrequency;
        int limitMtu;
        int pingInterval;
        ConnectionSimulation connectionSimulation;
        TooLargeMessageBehaviour tooLargeMessageBehaviour;
        private protected bool autoMtuExpand;

        SynchronizationContext syncronizationContext;
        ContextSynchronizationMode contextSynchronizationMode;

        private protected bool locked;

        internal void Lock()
        {
            if (locked)
                throw new InvalidOperationException($"{nameof(UdpConfigurationPeer)} already locked");
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
                throw new NullReferenceException("Synchronization context is null");
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

        public UdpConfigurationPeer()
        {
            syncronizationContext = new SynchronizationContext();
            tooLargeMessageBehaviour = TooLargeMessageBehaviour.RaiseException;
            contextSynchronizationMode = ContextSynchronizationMode.Send;
            networkReceiveThreads = Environment.ProcessorCount;
            memoryStreamPool = MemoryStreamPool.Shared;
            logManager = Logging.LogManager.Dummy;
            connectionTimeout = 5000;
            mtuExpandMaxFailAttempts = 5;
            mtuExpandFrequency = 2000;
            pingInterval = 1000;
            connectionLingerTimeout = 20000;
            limitMtu = int.MaxValue;
        }
    }
}
