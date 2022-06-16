using System;

namespace Yove.Http.Events
{
    public class DownloadEvent : EventArgs
    {
        public long Received { get; }
        public long? Total { get; }

        public int ProgressPercentage
        {
            get
            {
                return (int)(Received / (double)Total * 100.0);
            }
        }

        public DownloadEvent(long received, long? total)
        {
            Received = received;
            Total = total;
        }
    }
}
