using System;
using System.IO;

namespace Yove.HttpClient;

public class StreamContent : HttpContent
{
    internal Stream Content { get; set; }

    internal int BufferSize { get; set; }

    public override long ContentLength
    {
        get
        {
            if (Content == null)
                throw new ObjectDisposedException("Content disposed or empty.");

            return Content.Length;
        }
    }

    public StreamContent() { }

    public StreamContent(Stream content, int bufferSize = 32768)
    {
        if (content?.CanRead != true || !content.CanSeek)
            throw new NullReferenceException("Parameters is empty or invalid value.");

        Content = content;
        BufferSize = bufferSize;
    }

    public override void Write(Stream commonStream)
    {
        if (Content == null)
            throw new ObjectDisposedException("Content disposed or empty.");

        if (commonStream == null)
            throw new NullReferenceException("Stream is empty.");

        Content.Position = 0;

        byte[] buffer = new byte[BufferSize];

        while (true)
        {
            int readBytes = Content.Read(buffer, 0, buffer.Length);

            if (readBytes == 0)
                break;

            commonStream.Write(buffer, 0, readBytes);
        }
    }

    public override void Dispose()
    {
        Content?.Dispose();
    }
}
