using System;
using System.IO;

namespace Yove.Http;

public class StreamContent : HttpContent
{
    internal Stream Content { get; set; }

    internal int BufferSize { get; set; }

    public override long ContentLength
    {
        get
        {
            return Content == null ? throw new ObjectDisposedException("Content disposed or empty.") : Content.Length;
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
        if (Content != null)
        {
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
        else
        {
            throw new ObjectDisposedException("Content disposed or empty.");
        }
    }

    public override void Dispose()
    {
        Content?.Dispose();
    }
}
