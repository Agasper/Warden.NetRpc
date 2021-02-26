using System;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Cryptography;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConfiguration
    {
        internal TaskScheduler TaskScheduler { get => taskScheduler; set { CheckLocked(); taskScheduler = value; } }
        
        public int DefaultExecutionTimeout { get => defaultExecutionTimeout; set { CheckLocked(); CheckNull(value); defaultExecutionTimeout = value; } }
        public bool OrderedExecution { get => orderedExecution; set { CheckLocked(); CheckNull(value); orderedExecution = value; } }
        public int OrderedExecutionMaxQueue { get => orderedExecutionMaxQueue; set { CheckLocked(); CheckNull(value); orderedExecutionMaxQueue = value; } }
        public RpcSerializer Serializer { get => serializer; set { CheckLocked(); CheckNull(value); serializer = value; } }
        public ILogManager LogManager { get => logManager; set { CheckLocked(); CheckNull(value); logManager = value; } }
        public ISessionFactory SessionFactory { get => sessionFactory; set { CheckLocked(); CheckNull(value); sessionFactory = value; } }

        internal ICipher Cipher => cipher;

        ICipher cipher;
        ISessionFactory sessionFactory;
        ILogManager logManager;
        int defaultExecutionTimeout;
        bool orderedExecution;
        int orderedExecutionMaxQueue;
        RpcSerializer serializer;
        TaskScheduler taskScheduler;
        
        protected bool locked;

        internal virtual void Lock()
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
                throw new NullReferenceException("Synchronization context is null");
            this.TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        public void SetCipher<T>() where T : ICipher, new()
        {
            this.cipher = new T();
        }

        public RpcTcpConfiguration()
        {
            logManager = Logging.LogManager.Dummy;
            defaultExecutionTimeout = 10000;
            orderedExecution = false;
            orderedExecutionMaxQueue = 32;
            taskScheduler = TaskScheduler.Default;
        }
    }
}