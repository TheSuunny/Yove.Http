using System.IO;

namespace Yove.Http
{
    public class StreamWrapper : Stream
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

        public StreamWrapper(Stream Stream, Receiver Content)
        {
            this.Stream = Stream;
            this.Content = Content;
        }

        public override void Flush() => Stream.Flush();

        public override void SetLength(long value) => Stream.SetLength(value);

        public override long Seek(long offset, SeekOrigin origin)
        {
            return Stream.Seek(offset, origin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (LimitBytesRead != 0)
            {
                int Length = LimitBytesRead - TotalBytesRead;

                if (Length == 0)
                    return 0;

                if (Length > buffer.Length)
                    Length = buffer.Length;

                if (Content.HasData)
                    BytesRead = Content.Read(buffer, offset, Length);
                else
                    BytesRead = Stream.Read(buffer, offset, Length);
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

        public override async void Write(byte[] buffer, int offset, int count)
        {
            await Stream.WriteAsync(buffer, offset, count).ConfigureAwait(false);
        }
    }
}