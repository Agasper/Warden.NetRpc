using System;
using System.Threading;
using System.Threading.Tasks;
using Warden.Networking.Tcp;
using Warden.Rpc;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpServerConfiguration : TcpServerConfiguration
    {
        public ISessionFactory SessionFactory { get => sessionFactory; set { CheckLocked(); CheckNull(value); sessionFactory = value; } }
        public RpcSerializer Serializer { get => serializer; set { CheckLocked(); CheckNull(value); serializer = value; } }
        //TODO FIX remove this class or refactor
        public RemotingObjectConfiguration RemotingConfiguration { get => remotingConfiguration; set { CheckLocked(); remotingConfiguration = value; } }
        public int DefaultExecutionTimeout { get => defaultExecutionTimeout; set { CheckLocked(); defaultExecutionTimeout = value; } }
        public bool OrderedExecution { get => orderedExecution; set { CheckLocked(); orderedExecution = value; } }
        public int OrderedExecutionMaxQueue { get => orderedExecutionMaxQueue; set { CheckLocked(); orderedExecutionMaxQueue = value; } }

        internal TaskScheduler TaskScheduler { get => taskScheduler; }

        RpcSerializer serializer;
        int defaultExecutionTimeout;
        RemotingObjectConfiguration remotingConfiguration;
        bool orderedExecution;
        int orderedExecutionMaxQueue;
        ISessionFactory sessionFactory;
        TaskScheduler taskScheduler;

        public override void CaptureSynchronizationContext()
        {
            CheckLocked();
            if (SynchronizationContext.Current == null)
                throw new ArgumentNullException("Synchronization context is null");
            this.syncronizationContext = SynchronizationContext.Current;
            this.taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        public override void SetSynchronizationContext(SynchronizationContext context)
        {
            throw new NotSupportedException();
        }

        public RpcTcpServerConfiguration()
        {
            defaultExecutionTimeout = 10000;
            remotingConfiguration = new RemotingObjectConfiguration(true, true, true);
            orderedExecution = true;
            orderedExecutionMaxQueue = 32;
            taskScheduler = TaskScheduler.Default;
        }
    }
}
