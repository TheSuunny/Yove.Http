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
                return ByteSize.FromBytes(_speedBytes);
            }
        }

        public int ProgressPercentage
        {
            get
            {
                return (int)(((double)Received / (double)Total) * 100.0);
            }
        }

        private long _speedBytes { get; set; }

        public DownloadEvent(long speed, long received, long total)
        {
            _speedBytes = speed;

            Received = received;
            Total = total;
        }
    }
}