using System;
namespace Warden.Rpc
{
    public struct ExecutionResult
    {
        public bool HasResult { get; set; }
        public object Result { get; set; }

        public ExecutionResult(object result)
        {
            HasResult = true;
            Result = result;
        }
    }
}
