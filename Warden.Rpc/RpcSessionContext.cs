﻿using System.Threading.Tasks;
using Warden.Logging;
using Warden.Rpc.Serialization;

namespace Warden.Rpc
{
    public class RpcSessionContext
    {
        public int DefaultExecutionTimeout { get; set; }
        public bool OrderedExecution { get; set; }
        public int OrderedExecutionMaxQueue { get; set; }
        public RpcSerializer Serializer { get; set; }
        public TaskScheduler TaskScheduler { get; set; }
        public ILogManager LogManager { get; set; }
        public IRpcConnection Connection { get; set; }
        public RemotingObjectConfiguration RemotingObjectConfiguration { get; set; }

        public RpcSessionContext()
        {
            DefaultExecutionTimeout = 10000;
            OrderedExecution = false;
            OrderedExecutionMaxQueue = 32;
            TaskScheduler = TaskScheduler.Default;
            RemotingObjectConfiguration = RemotingObjectConfiguration.Default;
        }
    }
}
