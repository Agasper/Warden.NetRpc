namespace Warden.Rpc
{
    public struct SendingOptions
    {
        public object State { get; set; }
        public bool ThrowIfFailedToSend { get; set; }
        public bool NoAck { get; set; }

        public SendingOptions(bool throwIfFailedToSend, bool noAck, object state)
        {
            this.State = state;
            this.NoAck = noAck;
            this.ThrowIfFailedToSend = throwIfFailedToSend;
        }

        public SendingOptions WithNoAck(bool noAck)
        {
            this.NoAck = noAck;
            return this;
        }
        
        public SendingOptions WithThrow(bool throwIfFailed)
        {
            this.ThrowIfFailedToSend = throwIfFailed;
            return this;
        }

        public static SendingOptions Default => new SendingOptions(true, false, null);

        public override string ToString()
        {
            return $"SendingOptions[State={State},throwIfFailedToSend={ThrowIfFailedToSend}]";
        }
    }
}
