using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using Yove.Http.Exceptions;

namespace Yove.Http.Proxy;

public class HttpProxy : ProxyClient
{
    public string Username { get; set; }
    public string Password { get; set; }

    public HttpProxy() { }
    public HttpProxy(string host, int port) : this($"{host}:{port}") { }
    public HttpProxy(string proxy) : base(proxy, ProxyType.Http) { }

    private protected override async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
    {
        if (destinationPort == 80)
            return ConnectionResult.OK;

        string authorizationHeader = GenerateAuthorizationHeader();

        byte[] requestBytes = Encoding.ASCII.GetBytes($"CONNECT {destinationHost}:{destinationPort} HTTP/1.1{authorizationHeader}\r\n\r\n");

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

    private string GenerateAuthorizationHeader()
    {
        if (!string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password))
        {
            string data = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));

            return $"\r\nProxy-Authorization: Basic {data}";
        }

        return string.Empty;
    }
}
