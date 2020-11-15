using System.IO;

namespace Yove.Http
{
    public abstract class HttpContent
    {
        public string ContentType { get; set; }

        public abstract long ContentLength { get; }

        public abstract void Write(Stream commonStream);

        public abstract void Dispose();
    }
}