using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Fody;

using Yove.Http.Exceptions;

namespace Yove.Http.Proxy;

[ConfigureAwait(false)]
public abstract class ProxyClient
{
    public string Host { get; set; }
    public int Port { get; set; }
    public ProxyType Type { get; set; }

    public int TimeOut { get; set; } = 60000;
    public int ReadWriteTimeOut { get; set; } = 60000;

    private protected const byte ADDRESS_TYPE_IPV4 = 0x01;
    private protected const byte ADDRESS_TYPE_IPV6 = 0x04;
    private protected const byte ADDRESS_TYPE_DOMAIN_NAME = 0x03;

    protected ProxyClient() { }

    protected ProxyClient(string host, int port, ProxyType type) : this($"{host}:{port}", type) { }

    protected ProxyClient(string proxy, ProxyType type)
    {
        if (string.IsNullOrEmpty(proxy) || !proxy.Contains(':'))
            throw new NullReferenceException("Proxy is null or empty or invalid type.");

        string host = proxy.Split(':')[0];
        int port = Convert.ToInt32(proxy.Split(':')[1]);

        if (port < 0 || port > 65535)
            throw new NullReferenceException("Port goes beyond < 0 or > 65535.");

        Host = host;
        Port = port;
        Type = type;
    }

    internal async Task<TcpClient> CreateConnection(string destinationHost, int destinationPort)
    {
        if (string.IsNullOrEmpty(Host))
            throw new NullReferenceException("Host is null or empty.");

        if (Port < 0 || Port > 65535)
            throw new NullReferenceException("Port goes beyond < 0 or > 65535.");

        TcpClient tcpClient = new()
        {
            ReceiveTimeout = ReadWriteTimeOut,
            SendTimeout = ReadWriteTimeOut
        };

        using CancellationTokenSource cancellationToken = new(TimeSpan.FromMilliseconds(TimeOut));

        try
        {
#if NETSTANDARD2_1 || NETCOREAPP3_1
                tcpClient.ConnectAsync(Host, Port).Wait(cancellationToken.Token);
#elif NET5_0_OR_GREATER
            await tcpClient.ConnectAsync(Host, Port, cancellationToken.Token);
#endif

            if (!tcpClient.Connected)
                throw new();
        }
        catch
        {
            tcpClient.Dispose();

            throw new HttpRequestException($"Failed Connection to proxy: {Host}:{Port}");
        }

        NetworkStream networkStream = tcpClient.GetStream();

        ConnectionResult connection = await SendCommand(networkStream, destinationHost, destinationPort);

        if (connection != ConnectionResult.OK)
        {
            tcpClient.Close();

            throw new HttpProxyException($"Could not connect to proxy server | Response - {connection}");
        }

        return tcpClient;
    }

    private protected abstract Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort);

    private protected async Task WaitStream(NetworkStream networkStream)
    {
        int sleep = 0;
        int delay = (networkStream.ReadTimeout < 10) ? 10 : networkStream.ReadTimeout;

        while (!networkStream.DataAvailable)
        {
            if (sleep < delay)
            {
                sleep += 10;
                await Task.Delay(10);

                continue;
            }

            throw new HttpProxyException($"Timeout waiting for data from Address: {Host}:{Port}");
        }
    }

    private protected static IPAddress GetHost(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress ip))
            return ip;

        return Dns.GetHostAddresses(host)[0];
    }

    private protected static byte GetAddressType(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ADDRESS_TYPE_IPV4;

            return ADDRESS_TYPE_IPV6;
        }

        return ADDRESS_TYPE_DOMAIN_NAME;
    }

    private protected static byte[] GetPortBytes(int port)
    {
        byte[] bytes = new byte[2];

        bytes[0] = (byte)(port / 256);
        bytes[1] = (byte)(port % 256);

        return bytes;
    }
}
