using System;

namespace Yove.HttpClient.Exceptions;

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
