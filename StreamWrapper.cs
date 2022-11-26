using System;
using System.IO;

namespace Yove.Http;

internal class StreamWrapper : Stream
{
    public Stream Stream { get; set; }
    public Receiver Content { get; set; }

    public int BytesRead { get; private set; }
    public int TotalBytesRead { get; set; }
    public int LimitBytesRead { get; set; }

    public override bool CanRead
    {
        get
        {
            return Stream.CanRead;
        }
    }

    public override bool CanSeek
    {
        get
        {
            return Stream.CanSeek;
        }
    }

    public override bool CanTimeout
    {
        get
        {
            return Stream.CanTimeout;
        }
    }

    public override bool CanWrite
    {
        get
        {
            return Stream.CanWrite;
        }
    }

    public override long Length
    {
        get
        {
            return Stream.Length;
        }
    }

    public override long Position
    {
        get
        {
            return Stream.Position;
        }
        set
        {
            Stream.Position = value;
        }
    }

    public StreamWrapper(Stream stream, Receiver content)
    {
        Stream = stream;
        Content = content;
    }

    public override void Flush()
    {
        Stream.Flush();
    }

    public override void SetLength(long value)
    {
        Stream.SetLength(value);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return Stream.Seek(offset, origin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (LimitBytesRead != 0)
        {
            int length = LimitBytesRead - TotalBytesRead;

            if (length == 0)
                return 0;

            if (length > buffer.Length)
                length = buffer.Length;

            if (Content.HasData)
                BytesRead = Content.Read(buffer, offset, length);
            else
                BytesRead = Stream.Read(buffer, offset, length);
        }
        else
        {
            if (Content.HasData)
                BytesRead = Content.Read(buffer, offset, count);
            else
                BytesRead = Stream.Read(buffer, offset, count);
        }

        TotalBytesRead += BytesRead;

        return BytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Stream.Write(buffer, offset, count);
    }
}

internal class EventStreamWrapper : Stream
{
    private Stream _stream { get; }
    private int _bufferSize { get; }

    public Action<int> ReadBytesCallback { get; set; }
    public Action<int> WriteBytesCallback { get; set; }

    public override bool CanRead
    {
        get
        {
            return _stream.CanRead;
        }
    }

    public override bool CanSeek
    {
        get
        {
            return _stream.CanSeek;
        }
    }

    public override bool CanTimeout
    {
        get
        {
            return _stream.CanTimeout;
        }
    }

    public override bool CanWrite
    {
        get
        {
            return _stream.CanWrite;
        }
    }

    public override long Length
    {
        get
        {
            return _stream.Length;
        }
    }

    public override long Position
    {
        get
        {
            return _stream.Position;
        }
        set
        {
            _stream.Position = value;
        }
    }

    public EventStreamWrapper(Stream stream, int bufferSize)
    {
        _stream = stream;
        _bufferSize = bufferSize;
    }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override void SetLength(long value)
    {
        _stream.SetLength(value);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _stream.Read(buffer, offset, count);

        ReadBytesCallback?.Invoke(bytesRead);

        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (WriteBytesCallback != null)
        {
            int index = 0;

            do
            {
                int writeBytes;

                if (count >= _bufferSize)
                {
                    writeBytes = _bufferSize;
                    _stream.Write(buffer, index, writeBytes);

                    index += _bufferSize;
                    count -= _bufferSize;
                }
                else
                {
                    writeBytes = count;
                    _stream.Write(buffer, index, writeBytes);

                    count = 0;
                }

                WriteBytesCallback(writeBytes);
            } while (count > 0);
        }
        else
        {
            _stream.Write(buffer, offset, count);
        }
    }
}
