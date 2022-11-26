using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using Yove.Http.Exceptions;

namespace Yove.Http.Proxy;

public class HttpProxy : ProxyClient
{
    public HttpProxy() { }
    public HttpProxy(string host, int port, ProxyType type) : this($"{host}:{port}", type) { }
    public HttpProxy(string proxy, ProxyType type) : base(proxy, type) { }

    private protected override async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
    {
        if (destinationPort == 80)
            return ConnectionResult.OK;

        byte[] requestBytes = Encoding.ASCII.GetBytes($"CONNECT {destinationHost}:{destinationPort} HTTP/1.1\r\n\r\n");

        networkStream.Write(requestBytes, 0, requestBytes.Length);

        await WaitStream(networkStream);

        StringBuilder responseBuilder = new();

        byte[] buffer = new byte[100];

        while (networkStream.DataAvailable)
        {
            int readBytes = networkStream.Read(buffer, 0, 100);

            responseBuilder.Append(Encoding.ASCII.GetString(buffer, 0, readBytes));
        }

        if (responseBuilder.Length == 0)
            throw new HttpProxyException("Received empty response.");

        HttpStatusCode statusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser(" ", responseBuilder.ToString(), " ")?.Trim());

        if (statusCode != HttpStatusCode.OK)
            return ConnectionResult.InvalidProxyResponse;

        return ConnectionResult.OK;
    }
}
