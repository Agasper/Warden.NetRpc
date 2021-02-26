namespace Warden.Rpc
{
    public struct ExecutionResult
    {
        public bool HasResult { get; private set; }
        public object Result { get; private set; }
        
        public ExecutionResult(bool hasResult, object result)
        {
            this.HasResult = hasResult;
            this.Result = result;
        }

        public static ExecutionResult FromResult(object result)
        {
            return new ExecutionResult()
            {
                HasResult = true,
                Result = result
            };
        }

        public static ExecutionResult NoResult
        {
            get
            {
                return new ExecutionResult()
                {
                    HasResult = false
                };
            }
        }
    }
}