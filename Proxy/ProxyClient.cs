using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Fody;

using Yove.Http.Exceptions;

namespace Yove.Http.Proxy
{
    [ConfigureAwait(false)]
    public class ProxyClient
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public ProxyType Type { get; set; }

        public int TimeOut { get; set; } = 60000;
        public int ReadWriteTimeOut { get; set; } = 60000;

        private const byte ADDRESS_TYPE_IPV4 = 0x01;
        private const byte ADDRESS_TYPE_IPV6 = 0x04;
        private const byte ADDRESS_TYPE_DOMAIN_NAME = 0x03;

        public ProxyClient() { }

        public ProxyClient(string host, int port, ProxyType type) : this($"{host}:{port}", type) { }

        public ProxyClient(string proxy, ProxyType type)
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

            ValueTask connectionTask = tcpClient.ConnectAsync(Host, Port, cancellationToken.Token);

            try
            {
                await connectionTask;
            }
            catch
            {
                throw new HttpProxyException($"Failed Connection to proxy: {Host}:{Port}");
            }

            if (!tcpClient.Connected)
                throw new HttpProxyException($"Failed Connection to proxy: {Host}:{Port}");

            NetworkStream networkStream = tcpClient.GetStream();

            ConnectionResult connection = await SendCommand(networkStream, destinationHost, destinationPort);

            if (connection != ConnectionResult.OK)
            {
                tcpClient.Close();

                throw new HttpProxyException($"Could not connect to proxy server | Response - {connection}");
            }

            return tcpClient;
        }

        private async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
        {
            return Type switch
            {
                ProxyType.Http => await SendHttp(networkStream, destinationHost, destinationPort),
                ProxyType.Socks4 => await SendSocks4(networkStream, destinationHost, destinationPort),
                ProxyType.Socks5 => await SendSocks5(networkStream, destinationHost, destinationPort),
                _ => throw new HttpProxyException("Unsupported proxy type."),
            };
        }

        private async Task<ConnectionResult> SendHttp(NetworkStream networkStream, string destinationHost, int destinationPort)
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

        private async Task<ConnectionResult> SendSocks4(NetworkStream networkStream, string destinationHost, int destinationPort)
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

        private async Task<ConnectionResult> SendSocks5(NetworkStream networkStream, string destinationHost, int destinationPort)
        {
            byte[] response = new byte[255];
            byte[] auth = new byte[3];

            auth[0] = 5;
            auth[1] = 1;
            auth[2] = 0;

            networkStream.Write(auth, 0, auth.Length);

            await WaitStream(networkStream);

            networkStream.Read(response, 0, response.Length);

            if (response[1] != 0x00)
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

        private async Task WaitStream(NetworkStream networkStream)
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

        private static IPAddress GetHost(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress ip))
                return ip;

            return Dns.GetHostAddresses(host)[0];
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

        private static byte GetAddressType(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ADDRESS_TYPE_IPV4;

                return ADDRESS_TYPE_IPV6;
            }

            return ADDRESS_TYPE_DOMAIN_NAME;
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

        private static byte[] GetPortBytes(int port)
        {
            byte[] bytes = new byte[2];

            bytes[0] = (byte)(port / 256);
            bytes[1] = (byte)(port % 256);

            return bytes;
        }
    }
}
