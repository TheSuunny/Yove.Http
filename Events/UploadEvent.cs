using System;

namespace Yove.Http.Events;

public class UploadEvent(long sent, long total) : EventArgs
{
    public long Sent { get; } = sent;
    public long Total { get; } = total;

    public int ProgressPercentage
    {
        get
        {
            return (int)(Sent / (double)Total * 100.0);
        }
    }
}
