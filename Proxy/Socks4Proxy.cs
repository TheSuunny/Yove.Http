using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Yove.Http;
using Yove.Http.Proxy;

namespace Yove.Http.Proxy;

public class Socks4Proxy : ProxyClient
{
    public Socks4Proxy() { }
    public Socks4Proxy(string host, int port, ProxyType type) : this($"{host}:{port}", type) { }
    public Socks4Proxy(string proxy, ProxyType type) : base(proxy, type) { }

    private protected override async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
    {
        byte addressType = GetAddressType(destinationHost);

        if (addressType == ADDRESS_TYPE_DOMAIN_NAME)
            destinationHost = GetHost(destinationHost).ToString();

        byte[] address = GetIPAddressBytes(destinationHost);
        byte[] port = GetPortBytes(destinationPort);
        byte[] userId = Array.Empty<byte>();

        byte[] request = new byte[9];
        byte[] response = new byte[8];

        request[0] = 4;
        request[1] = 0x01;
        address.CopyTo(request, 4);
        port.CopyTo(request, 2);
        userId.CopyTo(request, 8);
        request[8] = 0x00;

        networkStream.Write(request, 0, request.Length);

        await WaitStream(networkStream);

        networkStream.Read(response, 0, response.Length);

        if (response[1] != 0x5a)
            return ConnectionResult.InvalidProxyResponse;

        return ConnectionResult.OK;
    }

    private static byte[] GetIPAddressBytes(string destinationHost)
    {
        if (!IPAddress.TryParse(destinationHost, out IPAddress address))
        {
            IPAddress[] ipAddresses = Dns.GetHostAddresses(destinationHost);

            if (ipAddresses.Length > 0)
                address = ipAddresses[0];
        }

        return address?.GetAddressBytes();
    }
}
