using System;
using System.Threading.Tasks;

namespace Warden.Rpc
{
    public interface IRpcSession
    {
        object Tag { get; set; }

        bool Close(Exception exception = null);

        Task ExecuteAsync(int methodIdentity);
        Task ExecuteAsync(string methodIdentity);
        Task ExecuteAsync(int methodIdentity, ExecutionOptions options);
        Task ExecuteAsync(string methodIdentity, ExecutionOptions options);
        Task ExecuteAsync<A>(int methodIdentity, A arg);
        Task ExecuteAsync<A>(string methodIdentity, A arg);
        Task ExecuteAsync<A>(int methodIdentity, A arg, ExecutionOptions options);
        Task ExecuteAsync<A>(string methodIdentity, A arg, ExecutionOptions options);
        Task<R> ExecuteAsync<R>(int methodIdentity);
        Task<R> ExecuteAsync<R>(string methodIdentity);
        Task<R> ExecuteAsync<R>(int methodIdentity, ExecutionOptions options);
        Task<R> ExecuteAsync<R>(string methodIdentity, ExecutionOptions options);
        Task<R> ExecuteAsync<R, A>(int methodIdentity, A arg);
        Task<R> ExecuteAsync<R, A>(string methodIdentity, A arg);
        Task<R> ExecuteAsync<R, A>(int methodIdentity, A arg, ExecutionOptions options);
        Task<R> ExecuteAsync<R, A>(string methodIdentity, A arg, ExecutionOptions options);
        void Send(int methodIdentity);
        void Send(string methodIdentity);
        void Send<T>(int methodIdentity, T arg);
        void Send<T>(string methodIdentity, T arg);
    }
}