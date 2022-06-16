using System;

namespace Yove.Http.Events
{
    public class UploadEvent : EventArgs
    {
        public long Sent { get; }
        public long Total { get; }

        public int ProgressPercentage
        {
            get
            {
                return (int)(Sent / (double)Total * 100.0);
            }
        }

        public UploadEvent(long sent, long total)
        {
            Sent = sent;
            Total = total;
        }
    }
}
