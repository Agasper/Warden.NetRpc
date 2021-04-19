using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Cryptography;
using Warden.Rpc.Cryptography;

namespace Warden.Rpc.Net
{
    public class RpcConfiguration
    {
        internal TaskScheduler TaskScheduler { get => taskScheduler; set { CheckLocked(); taskScheduler = value; } }
        
        public int DefaultExecutionTimeout { get => defaultExecutionTimeout; set { CheckLocked(); CheckNull(value); defaultExecutionTimeout = value; } }
        public bool OrderedExecution { get => orderedExecution; set { CheckLocked(); CheckNull(value); orderedExecution = value; } }
        public int OrderedExecutionMaxQueue { get => orderedExecutionMaxQueue; set { CheckLocked(); CheckNull(value); orderedExecutionMaxQueue = value; } }
        public RpcSerializer Serializer { get => serializer; set { CheckLocked(); CheckNull(value); serializer = value; } }
        public ILogManager LogManager { get => logManager; set { CheckLocked(); CheckNull(value); logManager = value; } }
        public ISessionFactory SessionFactory { get => sessionFactory; set { CheckLocked(); CheckNull(value); sessionFactory = value; } }
        public RemotingObjectConfiguration RemotingConfiguration { get => remotingConfiguration; set { CheckLocked(); CheckNull(value); remotingConfiguration = value; } }
        public int CompressionThreshold { get => compressionThreshold; set { CheckLocked(); compressionThreshold = value; } }

        ICipherFactory cipherFactory;
        ISessionFactory sessionFactory;
        ILogManager logManager;
        int defaultExecutionTimeout;
        bool orderedExecution;
        int orderedExecutionMaxQueue;
        int compressionThreshold;
        RpcSerializer serializer;
        TaskScheduler taskScheduler;
        RemotingObjectConfiguration remotingConfiguration;
        
        protected bool locked;

        internal virtual void Lock()
        {
            if (locked)
                throw new InvalidOperationException($"{nameof(RpcConfiguration)} already locked");
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
            this.TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        internal bool IsCipherSet => this.cipherFactory != null;

        internal ICipher CreateNewCipher()
        {
            if (this.cipherFactory == null)
                throw new NullReferenceException("Cipher not set");
            return this.cipherFactory.CreateNewCipher();
        }

        public void SetCipher<T>() where T : ICipher, new()
        {
            CheckLocked();
            this.cipherFactory = new CipherFactory<T>();
        }

        public RpcConfiguration()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly != null)
                serializer = new RpcSerializer(assembly);
            else
                serializer = new RpcSerializer();
            remotingConfiguration = RemotingObjectConfiguration.Default;
            logManager = Logging.LogManager.Dummy;
            defaultExecutionTimeout = 10000;
            orderedExecution = false;
            orderedExecutionMaxQueue = 32;
            compressionThreshold = 1024;
            taskScheduler = TaskScheduler.Default;
        }
    }
}