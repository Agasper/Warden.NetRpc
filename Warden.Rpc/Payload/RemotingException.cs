using System;
using System.Text;
using Warden.Networking.IO;

namespace Warden.Rpc.Payload
{
    public class RemotingException : Exception
    {
        public override string Message => base.Message;
        public override string StackTrace
        {
            get
            {
                if (string.IsNullOrEmpty(remoteStackTrace))
                    return base.StackTrace;
                else
                    return remoteStackTrace;
            }
        }
        public string InnerExceptionType => remoteType;

        string remoteStackTrace;
        string remoteType;

        public RemotingException(string message) : base(message)
        {
            this.remoteType = "";
            this.remoteStackTrace = "";
        }

        public RemotingException(string message, Exception exception) : base(message, exception)
        {
            this.remoteStackTrace = exception.StackTrace;
            this.remoteType = exception.GetType().Name;
        }

        public RemotingException(Exception exception) : base(exception.Message, exception)
        {
            this.remoteStackTrace = exception.StackTrace;
            this.remoteType = exception.GetType().FullName;
        }

        public RemotingException(string message, string exceptionType, string stackTrace) : base(message)
        {
            this.remoteStackTrace = stackTrace;
            this.remoteType = exceptionType;
        }

        internal RemotingException(IReader reader) : base(reader.ReadString())
        {
            remoteStackTrace = reader.ReadString();
            remoteType = reader.ReadString();
        }

        internal void WriteTo(IWriter writer)
        {
            writer.Write(this.Message);
            writer.Write(this.StackTrace == null ? "StackTrace not available" : this.StackTrace);
            writer.Write(this.remoteType);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            if (string.IsNullOrEmpty(remoteType) || remoteType == nameof(RemotingException))
                sb.Append(this.GetType().Name);
            else
                sb.AppendFormat("{0}({1})", this.GetType().Name, remoteType);

            if (!string.IsNullOrEmpty(Message))
                sb.Append(": " + Message);

            if (!string.IsNullOrEmpty(StackTrace))
            {
                sb.Append(Environment.NewLine + StackTrace);
            }

            return sb.ToString();
        }
    }
}
