using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Yove.Http.Proxy;

public class Socks4Proxy : ProxyClient
{
    public string UserId { get; set; }

    public Socks4Proxy() { }
    public Socks4Proxy(string host, int port) : this($"{host}:{port}") { }
    public Socks4Proxy(string proxy) : base(proxy, ProxyType.Socks4) { }

    private protected override async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
    {
        byte addressType = GetAddressType(destinationHost);

        if (addressType == ADDRESS_TYPE_DOMAIN_NAME)
            destinationHost = GetHost(destinationHost).ToString();

        byte[] address = GetIPAddressBytes(destinationHost);
        byte[] port = GetPortBytes(destinationPort);
        byte[] userId = string.IsNullOrEmpty(UserId) ? [] : Encoding.ASCII.GetBytes(UserId);

        byte[] request = new byte[9 + userId.Length];
        byte[] response = new byte[8];

        request[0] = 4;
        request[1] = 0x01;
        address.CopyTo(request, 4);
        port.CopyTo(request, 2);
        userId.CopyTo(request, 8);
        request[8 + userId.Length] = 0x00;

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
