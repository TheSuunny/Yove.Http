using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Yove.Http
{
    public abstract class HttpContent
    {
        public string ContentType { get; set; }

        public abstract long ContentLength { get; }

        public abstract Task WriteAsync(Stream CommonStream);

        public abstract void Dispose();
    }
}