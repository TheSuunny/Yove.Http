using System;
using System.Runtime.Serialization;

namespace Yove.Http
{
    public class ProxyException : Exception
    {
        public ProxyException() { }

        public ProxyException(string message) : base(message) { }

        public ProxyException(string message, Exception inner) : base(message, inner) { }

        protected ProxyException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}