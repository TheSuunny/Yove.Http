using System;

namespace Yove.Http.Exceptions;

public class HttpResponseException : Exception
{
    public HttpResponseException()
    {
    }

    public HttpResponseException(string message)
        : base(message)
    {
    }

    public HttpResponseException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
