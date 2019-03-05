using System;
using System.IO;
using System.Text;

namespace Yove.Http
{
    public abstract class HttpContent
    {
        public string ContentType { get; set; }

        public abstract long ContentLength { get; }

        public abstract void Write(Stream CommonStream);

        public abstract void Dispose();
    }
}