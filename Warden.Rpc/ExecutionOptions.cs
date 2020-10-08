using System;

namespace Warden.Rpc
{
    public class ExecutionOptions
    {
        public int Timeout { get; set; }
        public object State { get; set; }

        public ExecutionOptions()
        {
            Timeout = System.Threading.Timeout.Infinite;
        }

        public ExecutionOptions WithTimeout(int timeout)
        {
            this.Timeout = timeout;
            return this;
        }

        public override string ToString()
        {
            return $"{nameof(ExecutionOptions)}[timeout={Timeout}]";
        }

        public static ExecutionOptions Default => new ExecutionOptions();
    }
}
