namespace Warden.Rpc
{
    public struct ExecutionOptions
    {
        public int Timeout { get; set; }
        public object State { get; set; }

        public ExecutionOptions(int timeout, object state)
        {
            this.Timeout = timeout;
            this.State = state;
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

        public static ExecutionOptions Default => new ExecutionOptions(System.Threading.Timeout.Infinite, null);
    }
}
