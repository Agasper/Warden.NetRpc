using System.Text;

namespace Warden.Util.Buffers
{
    public class MemoryStreamPoolStatistics
    {
        public long SmallPoolInUseBytes { get; internal set; }
        public long SmallPoolFreeBytes { get; internal set; }
        public long LargePoolInUseBytes { get; internal set; }
        public long LargePoolFreeBytes { get; internal set; }
        public long StreamsFinalized { get; internal set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("-----------------------------");
            sb.AppendFormat("SmallPoolInUseBytes: {0}\n", SmallPoolInUseBytes);
            sb.AppendFormat("SmallPoolFreeBytes: {0}\n", SmallPoolFreeBytes);
            sb.AppendFormat("LargePoolInUseBytes: {0}\n", LargePoolInUseBytes);
            sb.AppendFormat("LargePoolFreeBytes: {0}\n", LargePoolFreeBytes);
            sb.AppendFormat("StreamsFinalized: {0}\n", StreamsFinalized);
            sb.AppendLine("-----------------------------");
            return sb.ToString();
        }
    }
}
