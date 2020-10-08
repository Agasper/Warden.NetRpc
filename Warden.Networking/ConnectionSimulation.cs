using System;
namespace Warden.Networking
{
    public class ConnectionSimulation
    {
        public float PacketLoss
        {
            get
            {
                return packetLoss;
            }
            set
            {
                if (value < 0 || value > 1)
                    throw new ArgumentOutOfRangeException("PacketLoss should be in range 0.0f-1.0f, where 0.0f is no packet loss");
                packetLoss = value;
            }
        }

        public int AdditionalLatency
        {
            get
            {
                return latency;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("Latency should be more than 0");
                latency = value;
            }
        }

        public int AdditionalLatencyVariation
        {
            get
            {
                return latencyVariation;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("LatencyVariation should be more than 0");
                latencyVariation = value;
            }
        }

        float packetLoss;
        int latency;
        int latencyVariation;
        Random random;

        public ConnectionSimulation()
        {
            random = new Random();
        }

        public ConnectionSimulation(int latency, int latencyVariation) : this()
        {
            this.AdditionalLatency = latency;
            this.AdditionalLatencyVariation = latencyVariation;
        }

        public ConnectionSimulation(int latency, int latencyVariation, float packetLoss) : this()
        {
            this.AdditionalLatency = latency;
            this.AdditionalLatencyVariation = latencyVariation;
            this.PacketLoss = packetLoss;
        }

        public ConnectionSimulation(float packetLoss) : this()
        {
            this.PacketLoss = packetLoss;
        }

        public static ConnectionSimulation Normal => new ConnectionSimulation();
        public static ConnectionSimulation Poor => new ConnectionSimulation(200, 50, 0.1f);
        public static ConnectionSimulation Terrible => new ConnectionSimulation(700, 300, 0.2f);

        internal int GetHalfDelay()
        {
            return Math.Max(0, (latency + (int)(random.NextDouble() *
                    latencyVariation)) / 2);
        }
    }
}
