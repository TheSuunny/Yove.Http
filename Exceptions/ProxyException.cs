using System;
using System.Runtime.Serialization;

namespace Yove.Http.Exceptions;

public class HttpProxyException : Exception
{
    public HttpProxyException() { }

    public HttpProxyException(string message) : base(message) { }

    public HttpProxyException(string message, Exception inner) : base(message, inner) { }

    protected HttpProxyException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
