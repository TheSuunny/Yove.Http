using System;

namespace Yove.Http.Exceptions;

public class HttpRequestException : Exception
{
    public HttpRequestException()
    {
    }

    public HttpRequestException(string message)
        : base(message)
    {
    }

    public HttpRequestException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
