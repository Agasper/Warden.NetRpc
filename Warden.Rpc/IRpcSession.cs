using System;
using System.Threading.Tasks;
using Warden.Networking.Udp.Messages;

namespace Warden.Rpc
{
    public interface IRpcSession
    {
        object Tag { get; set; }

        bool Close();

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
        void Send(int methodIdentity, SendingOptions sendingOptions);
        void Send(string methodIdentity, SendingOptions sendingOptions);
        void Send<T>(int methodIdentity, T arg);
        void Send<T>(string methodIdentity, T arg);
        void Send<T>(int methodIdentity, T arg, SendingOptions sendingOptions);
        void Send<T>(string methodIdentity, T arg, SendingOptions sendingOptions);
    }

    public interface IRpcSessionAdvancedDelivery : IRpcSession
    {
        void Send(int methodIdentity, DeliveryType deliveryType);
        void Send(string methodIdentity, DeliveryType deliveryType);
        void Send(int methodIdentity, DeliveryType deliveryType, int channel);
        void Send(string methodIdentity, DeliveryType deliveryType, int channel);
        void Send(int methodIdentity, DeliveryType deliveryType, int channel, SendingOptions sendingOptions);
        void Send(string methodIdentity, DeliveryType deliveryType, int channel, SendingOptions sendingOptions);
        void Send<T>(int methodIdentity, T arg, DeliveryType deliveryType);
        void Send<T>(string methodIdentity, T arg, DeliveryType deliveryType);
        void Send<T>(int methodIdentity, T arg, DeliveryType deliveryType, int channel);
        void Send<T>(string methodIdentity, T arg, DeliveryType deliveryType, int channel);
        void Send<T>(int methodIdentity, T arg, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions);
        void Send<T>(string methodIdentity, T arg, DeliveryType deliveryType, int channel,
            SendingOptions sendingOptions);
    }
}