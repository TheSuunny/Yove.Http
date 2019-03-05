using System;
using System.IO;

namespace Yove.Http
{
    public class StreamContent : HttpContent
    {
        internal Stream Content { get; set; }

        internal int BufferSize { get; set; }

        public override long ContentLength
        {
            get
            {
                if (Content == null)
                    throw new ObjectDisposedException("Content disposed or empty");

                return Content.Length;
            }
        }

        public StreamContent() { }

        public StreamContent(Stream Content, int BufferSize = 32768)
        {
            if (Content == null || !Content.CanRead || !Content.CanSeek)
                throw new ArgumentNullException("Parameters is empty or invalid value");

            this.Content = Content;
            this.BufferSize = BufferSize;
        }

        public override void Write(Stream CommonStream)
        {
            if (Content == null)
                throw new ObjectDisposedException("Content disposed or empty");

            if (CommonStream == null)
                throw new ArgumentNullException("Stream is empty");

            Content.Position = 0;

            byte[] Buffer = new byte[BufferSize];

            while (true)
            {
                int Bytes = Content.Read(Buffer, 0, Buffer.Length);

                if (Bytes == 0)
                    break;

                CommonStream.Write(Buffer, 0, Bytes);
            }
        }

        public override void Dispose()
        {
            if (Content != null)
                Content.Dispose();
        }
    }
}