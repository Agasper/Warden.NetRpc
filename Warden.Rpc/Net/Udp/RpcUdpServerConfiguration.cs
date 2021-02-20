using System;
using System.Threading;
using System.Threading.Tasks;
using Warden.Logging;
using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpServerConfiguration : UdpServerConfiguration
    {
        public int DefaultExecutionTimeout { get; set; }
        public bool OrderedExecution { get; set; }
        public int OrderedExecutionMaxQueue { get; set; }
        public RpcSerializer Serializer { get; set; }
        public TaskScheduler TaskScheduler { get; set; }
        
        public ISessionFactory SessionFactory { get => sessionFactory; set { CheckLocked(); CheckNull(value); sessionFactory = value; } }

        ISessionFactory sessionFactory;

        public RpcUdpServerConfiguration()
        {
            DefaultExecutionTimeout = 10000;
            OrderedExecution = false;
            OrderedExecutionMaxQueue = 32;
        }

        public override void CaptureSynchronizationContext()
        { 
            CheckLocked();
            if (SynchronizationContext.Current == null)
                throw new NullReferenceException("Synchronization context is null");
            this.TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        public override void SetSynchronizationContext(SynchronizationContext context)
        {
            throw new NotSupportedException();
        }
    }
}
