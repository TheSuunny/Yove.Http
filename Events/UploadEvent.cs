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
                return ByteSize.FromBytes(_speedBytes);
            }
        }

        public int ProgressPercentage
        {
            get
            {
                return (int)(((double)Sent / (double)Total) * 100.0);
            }
        }

        private long _speedBytes { get; set; }

        public UploadEvent(long speed, long sent, long total)
        {
            _speedBytes = speed;

            Sent = sent;
            Total = total;
        }
    }
}