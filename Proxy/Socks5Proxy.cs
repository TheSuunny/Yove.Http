using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace Yove.Http.Proxy;

public class Socks5Proxy : ProxyClient
{
    public string Username { get; set; }
    public string Password { get; set; }

    public Socks5Proxy() { }
    public Socks5Proxy(string host, int port) : this($"{host}:{port}") { }
    public Socks5Proxy(string proxy) : base(proxy, ProxyType.Socks5) { }

    private protected override async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
    {
        byte[] response = new byte[255];
        byte[] auth = new byte[3];

        auth[0] = 5;
        auth[1] = 1;

        if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            auth[2] = 0x02;
        else
            auth[2] = 0;

        networkStream.Write(auth, 0, auth.Length);

        await WaitStream(networkStream);

        networkStream.Read(response, 0, response.Length);

        if (response[1] == 0x02)
            await SendAuth(networkStream);
        else if (response[1] != 0x00)
            return ConnectionResult.InvalidProxyResponse;

        byte addressType = GetAddressType(destinationHost);

        if (addressType == ADDRESS_TYPE_DOMAIN_NAME)
            destinationHost = GetHost(destinationHost).ToString();

        byte[] address = GetAddressBytes(addressType, destinationHost);
        byte[] port = GetPortBytes(destinationPort);

        byte[] request = new byte[4 + address.Length + 2];

        request[0] = 5;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = addressType;

        address.CopyTo(request, 4);
        port.CopyTo(request, 4 + address.Length);

        networkStream.Write(request, 0, request.Length);

        await WaitStream(networkStream);

        networkStream.Read(response, 0, response.Length);

        if (response[1] != 0x00)
            return ConnectionResult.InvalidProxyResponse;

        return ConnectionResult.OK;
    }

    private async Task SendAuth(NetworkStream networkStream)
    {
        byte[] username = Encoding.ASCII.GetBytes(Username);
        byte[] password = Encoding.ASCII.GetBytes(Password);

        byte[] request = new byte[username.Length + password.Length + 3];

        request[0] = 1;
        request[1] = (byte)username.Length;
        username.CopyTo(request, 2);
        request[2 + username.Length] = (byte)password.Length;
        password.CopyTo(request, 3 + username.Length);

        networkStream.Write(request, 0, request.Length);

        byte[] response = new byte[2];

        await WaitStream(networkStream);

        networkStream.Read(response, 0, response.Length);

        if (response[1] != 0x00)
            throw new AuthenticationException();
    }

    private static byte[] GetAddressBytes(byte addressType, string host)
    {
        switch (addressType)
        {
            case ADDRESS_TYPE_IPV4:
            case ADDRESS_TYPE_IPV6:
                return IPAddress.Parse(host).GetAddressBytes();
            case ADDRESS_TYPE_DOMAIN_NAME:
                byte[] bytes = new byte[host.Length + 1];

                bytes[0] = (byte)host.Length;
                Encoding.ASCII.GetBytes(host).CopyTo(bytes, 1);

                return bytes;
            default:
                return null;
        }
    }
}
