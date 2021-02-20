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
    }
}