using System;
using ByteSizeLib;

namespace Yove.Http.Events
{
    public class UploadEvent : EventArgs
    {
        public long Sent { get; private set; }

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
                return (int)(((double)Sent / (double)Total) * 100.0);
            }
        }

        private long SpeedBytes { get; set; }

        public UploadEvent(long Speed, long Sent, long Total)
        {
            this.SpeedBytes = Speed;
            this.Sent = Sent;
            this.Total = Total;
        }
    }
}