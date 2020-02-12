using System;
using ByteSizeLib;

namespace Yove.Http.Events
{
    public class DownloadEvent : EventArgs
    {
        public long Received { get; private set; }

        public long Total { get; private set; }

        public ByteSize Speed
        {
            get
            {
                return ByteSize.FromBytes(SpeedBytes);
            }
        }

        public int ProgressPercentage
        {
            get
            {
                return (int)(((double)Received / (double)Total) * 100.0);
            }
        }

        private long SpeedBytes { get; set; }

        public DownloadEvent(long Speed, long Received, long Total)
        {
            this.SpeedBytes = Speed;
            this.Received = Received;
            this.Total = Total;
        }
    }
}