using System;
namespace Warden.Rpc
{
    public class SendingOptions
    {
        public object State { get; set; }

        public SendingOptions()
        {
        }

        public static SendingOptions Default => new SendingOptions();

        public override string ToString()
        {
            return $"SendingOptions[State={State}]";
        }
    }
}
