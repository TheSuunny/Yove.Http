using Fody;
using System;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;

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
            if (string.IsNullOrEmpty(proxy) || !proxy.Contains(":"))
                throw new ArgumentNullException("Proxy is null or empty or invalid type.");

            string host = proxy.Split(':')[0];
            int port = Convert.ToInt32(proxy.Split(':')[1]);

            if (port < 0 || port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535.");

            this.Host = host;
            this.Port = port;
            this.Type = type;
        }

        internal async Task<TcpClient> CreateConnection(string destinationHost, int destinationPort)
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentNullException("Host is null or empty.");

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535.");

            TcpClient client = new TcpClient
            {
                ReceiveTimeout = ReadWriteTimeOut,
                SendTimeout = ReadWriteTimeOut
            };

            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();

            using (CancellationTokenSource cancellationToken = new CancellationTokenSource(TimeSpan.FromMilliseconds(ReadWriteTimeOut)))
            {
                Task connectionTask = client.ConnectAsync(Host, Port);

                using (cancellationToken.Token.Register(() => completionSource.TrySetResult(true)))
                {
                    if (connectionTask != await Task.WhenAny(connectionTask, completionSource.Task))
                        throw new ProxyException($"Failed Connection to proxy - {Host}:{Port}");
                }
            }

            if (!client.Connected)
                throw new ProxyException($"Failed Connection to proxy - {Host}:{Port}");

            NetworkStream networkStream = client.GetStream();

            ConnectionResult connection = await SendCommand(networkStream, destinationHost, destinationPort);

            if (connection != ConnectionResult.OK)
            {
                client.Close();

                throw new ProxyException($"Could not connect to proxy server | Response - {connection}");
            }

            return client;
        }

        private async Task<ConnectionResult> SendCommand(NetworkStream networkStream, string destinationHost, int destinationPort)
        {
            switch (Type)
            {
                case ProxyType.Http:
                    return await SendHttp(networkStream, destinationHost, destinationPort);
                case ProxyType.Socks4:
                    return await SendSocks4(networkStream, destinationHost, destinationPort);
                case ProxyType.Socks5:
                    return await SendSocks5(networkStream, destinationHost, destinationPort);
                default:
                    throw new ProxyException("Unsupported proxy type.");
            }
        }

        private async Task<ConnectionResult> SendHttp(NetworkStream networkStream, string destinationHost, int destinationPort)
        {
            if (destinationPort == 80)
                return ConnectionResult.OK;

            byte[] requestBytes = Encoding.ASCII.GetBytes($"CONNECT {destinationHost}:{destinationPort} HTTP/1.1\r\n\r\n");

            networkStream.Write(requestBytes, 0, requestBytes.Length);

            await WaitStream(networkStream);

            StringBuilder responseBuilder = new StringBuilder();

            byte[] buffer = new byte[100];

            while (networkStream.DataAvailable)
            {
                int readBytes = networkStream.Read(buffer, 0, 100);

                responseBuilder.Append(Encoding.ASCII.GetString(buffer, 0, readBytes));
            }

            if (responseBuilder.Length == 0)
                throw new Exception("Received empty response.");

            HttpStatusCode statusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($" ", responseBuilder.ToString(), " ")?.Trim());

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
            byte[] userId = new byte[0];

            byte[] request = new byte[9];
            byte[] response = new byte[8];

            request[0] = (byte)4;
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

            auth[0] = (byte)5;
            auth[1] = (byte)1;
            auth[2] = (byte)0;

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

            request[0] = (byte)5;
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

                throw new ProxyException($"Timeout waiting for data - {Host}:{Port}");
            }
        }

        private IPAddress GetHost(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress Ip))
                return Ip;

            return Dns.GetHostAddresses(Host)[0];
        }

        private byte[] GetAddressBytes(byte addressType, string host)
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

        private byte GetAddressType(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ADDRESS_TYPE_IPV4;

                return ADDRESS_TYPE_IPV6;
            }

            return ADDRESS_TYPE_DOMAIN_NAME;
        }

        private byte[] GetIPAddressBytes(string destinationHost)
        {
            if (!IPAddress.TryParse(destinationHost, out IPAddress address))
            {
                IPAddress[] IPs = Dns.GetHostAddresses(destinationHost);

                if (IPs.Length > 0)
                    address = IPs[0];
            }

            return address.GetAddressBytes();
        }

        private byte[] GetPortBytes(int port)
        {
            byte[] bytes = new byte[2];

            bytes[0] = (byte)(port / 256);
            bytes[1] = (byte)(port % 256);

            return bytes;
        }
    }
}