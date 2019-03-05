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

        public ByteContent(byte[] Content) : this(Content, 0, Content.Length) { }

        public ByteContent(byte[] Content, int Offset, int Count)
        {
            if (Content == null || Offset < 0 || Count < 0 || Offset > Content.Length || Count > (Content.Length - Offset))
                throw new ArgumentNullException("Parameters is empty or invalid value");

            this.Content = Content;
            this.Offset = Offset;
            this.Count = Count;
        }

        public override void Write(Stream CommonStream)
        {
            if (CommonStream == null)
                throw new ArgumentNullException("Stream is empty");

            CommonStream.Write(Content, Offset, Count);
        }

        public override void Dispose() { }
    }
}