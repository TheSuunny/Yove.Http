namespace Yove.Http.Proxy;

public enum ConnectionResult
{
    OK = 0,
    HostUnreachable = 4,
    ConnectionRefused = 5,
    UnknownError,
    AuthenticationError,
    ConnectionReset,
    ConnectionError,
    InvalidProxyResponse
}
