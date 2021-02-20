using System;
using System.Threading.Tasks;
using Warden.Logging;

namespace Warden.Rpc
{
    public class RpcSessionContext
    {
        public int DefaultExecutionTimeout { get; set; }
        public bool OrderedExecution { get; set; }
        public int OrderedExecutionMaxQueue { get; set; }
        public RpcSerializer Serializer { get; set; }
        public ILogManager LogManager { get; set; }
        public TaskScheduler TaskScheduler { get; set; }
        public IRpcConnectionTcp Connection { get; set; }

        public bool AllowAsync { get; set; } = true;
        public bool AllowNonVoid { get; set; } = true;
        public bool DontUseLambdaExpressions { get; set; } = false;
        public bool OnlyPublicMethods { get; set; } = true;

        public RpcSessionContext()
        {
            DefaultExecutionTimeout = 10000;
            OrderedExecution = false;
            OrderedExecutionMaxQueue = 32;
            TaskScheduler = TaskScheduler.Default;
        }
    }
}
