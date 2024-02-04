using System;

namespace Yove.Http.Events;

public class DownloadEvent(long received, long? total) : EventArgs
{
    public long Received { get; } = received;
    public long? Total { get; } = total;

    public int ProgressPercentage
    {
        get
        {
            return (int)(Received / (double)Total * 100.0);
        }
    }
}
