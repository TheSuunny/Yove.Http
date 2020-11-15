using System;
using System.IO;

namespace Yove.Http
{
    public class ByteContent : HttpContent
    {
        internal byte[] Content { get; set; }

        internal int Offset { get; set; }
        internal int Count { get; set; }

        public override long ContentLength
        {
            get
            {
                return Content.LongLength;
            }
        }

        public ByteContent() { }

        public ByteContent(byte[] content) : this(content, 0, content.Length) { }

        public ByteContent(byte[] content, int offset, int count)
        {
            if (content == null || offset < 0 || count < 0 || offset > content.Length || count > (content.Length - offset))
                throw new ArgumentNullException("Parameters is empty or invalid value.");

            this.Content = content;
            this.Offset = offset;
            this.Count = count;
        }

        public override void Write(Stream commonStream)
        {
            if (commonStream == null)
                throw new ArgumentNullException("Stream is empty.");

            commonStream.Write(Content, Offset, Count);
        }

        public override void Dispose() { }
    }
}