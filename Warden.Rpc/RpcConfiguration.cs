using System;
using System.Threading.Tasks;
using Warden.Logging;

namespace Warden.Rpc
{
    public class RpcConfiguration
    {
        public int DefaultExecutionTimeout { get; set; }
        public bool OrderedExecution { get; set; }
        public int OrderedExecutionMaxQueue { get; set; }
        public RpcSerializer Serializer { get; set; }
        public IRpcConnection Connection { get; set; }
        public ILogManager LogManager { get; set; }
        public TaskScheduler TaskScheduler { get; set; }

        public RpcConfiguration()
        {
            DefaultExecutionTimeout = 10000;
            OrderedExecution = false;
            OrderedExecutionMaxQueue = 32;
            TaskScheduler = TaskScheduler.Default;
        }
    }
}
