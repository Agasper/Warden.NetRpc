namespace Warden.Rpc
{
    public class SendingOptions
    {
        public object State { get; set; }
        public bool ThrowIfFailedToSend { get; set; }

        public SendingOptions()
        {
            ThrowIfFailedToSend = true;
        }

        public static SendingOptions Default => new SendingOptions();

        public override string ToString()
        {
            return $"SendingOptions[State={State},throwIfFailedToSend={ThrowIfFailedToSend}]";
        }
    }
}
