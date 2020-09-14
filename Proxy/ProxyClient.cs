using System;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using Fody;

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

        private const byte AddressTypeIPV4 = 0x01;
        private const byte AddressTypeIPV6 = 0x04;
        private const byte AddressTypeDomainName = 0x03;

        public ProxyClient() { }

        public ProxyClient(string Host, int Port, ProxyType Type)
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentNullException("Host is null or empty.");

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535.");

            this.Host = Host;
            this.Port = Port;
            this.Type = Type;
        }

        public ProxyClient(string Proxy, ProxyType Type)
        {
            if (string.IsNullOrEmpty(Proxy) || !Proxy.Contains(":"))
                throw new ArgumentNullException("Proxy is null or empty or invalid type.");

            string Host = Proxy.Split(':')[0];
            int Port = Convert.ToInt32(Proxy.Split(':')[1]);

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535.");

            this.Host = Host;
            this.Port = Port;
            this.Type = Type;
        }

        internal async Task<TcpClient> CreateConnection(string DestinationHost, int DestinationPort)
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentNullException("Host is null or empty.");

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535.");

            TcpClient TcpClient = new TcpClient
            {
                ReceiveTimeout = ReadWriteTimeOut,
                SendTimeout = ReadWriteTimeOut
            };

            TcpClient.Connect(Host, Port);

            if (!TcpClient.Connected)
                throw new ProxyException($"Failed Connection to proxy - {Host}:{Port}");

            NetworkStream Stream = TcpClient.GetStream();

            ConnectionResult Connection = await SendCommand(Stream, DestinationHost, DestinationPort);

            if (Connection != ConnectionResult.OK)
            {
                TcpClient.Close();

                throw new ProxyException($"Could not connect to proxy server | Response - {Connection}");
            }

            return TcpClient;
        }

        private async Task<ConnectionResult> SendCommand(NetworkStream Stream, string DestinationHost, int DestinationPort)
        {
            switch (Type)
            {
                case ProxyType.Http:
                    return await SendHttp(Stream, DestinationHost, DestinationPort);
                case ProxyType.Socks4:
                    return await SendSocks4(Stream, DestinationHost, DestinationPort);
                case ProxyType.Socks5:
                    return await SendSocks5(Stream, DestinationHost, DestinationPort);
                default:
                    throw new ProxyException("Unsupported proxy type.");
            }
        }

        private async Task<ConnectionResult> SendHttp(NetworkStream Stream, string DestinationHost, int DestinationPort)
        {
            if (DestinationPort == 80)
                return ConnectionResult.OK;

            byte[] RequestBuffer = Encoding.ASCII.GetBytes($"CONNECT {DestinationHost}:{DestinationPort} HTTP/1.1\r\n\r\n");

            Stream.Write(RequestBuffer, 0, RequestBuffer.Length);

            await WaitStream(Stream);

            byte[] ResponseBuffer = new byte[100];

            StringBuilder Response = new StringBuilder();

            while (Stream.DataAvailable)
            {
                int Bytes = Stream.Read(ResponseBuffer, 0, 100);

                Response.Append(Encoding.ASCII.GetString(ResponseBuffer, 0, Bytes));
            }

            if (Response.Length == 0)
                new Exception("Received empty response.");

            HttpStatusCode StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($" ", Response.ToString(), " ")?.Trim());

            if (StatusCode != HttpStatusCode.OK)
                return ConnectionResult.InvalidProxyResponse;

            return ConnectionResult.OK;
        }

        private async Task<ConnectionResult> SendSocks4(NetworkStream Stream, string DestinationHost, int DestinationPort)
        {
            byte AddressType = GetAddressType(DestinationHost);

            if (AddressType == AddressTypeDomainName)
                DestinationHost = GetHost(DestinationHost).ToString();

            byte[] Address = GetIPAddressBytes(DestinationHost);
            byte[] Port = GetPortBytes(DestinationPort);
            byte[] UserId = new byte[0];

            byte[] Request = new byte[9];
            byte[] Response = new byte[8];

            Request[0] = (byte)4;
            Request[1] = 0x01;
            Address.CopyTo(Request, 4);
            Port.CopyTo(Request, 2);
            UserId.CopyTo(Request, 8);
            Request[8] = 0x00;

            Stream.Write(Request, 0, Request.Length);

            await WaitStream(Stream);

            Stream.Read(Response, 0, Response.Length);

            if (Response[1] != 0x5a)
                return ConnectionResult.InvalidProxyResponse;

            return ConnectionResult.OK;
        }

        private async Task<ConnectionResult> SendSocks5(NetworkStream Stream, string DestinationHost, int DestinationPort)
        {
            byte[] Response = new byte[255];

            byte[] Auth = new byte[3];
            Auth[0] = (byte)5;
            Auth[1] = (byte)1;
            Auth[2] = (byte)0;

            Stream.Write(Auth, 0, Auth.Length);

            await WaitStream(Stream);

            Stream.Read(Response, 0, Response.Length);

            if (Response[1] != 0x00)
                return ConnectionResult.InvalidProxyResponse;

            byte AddressType = GetAddressType(DestinationHost);

            if (AddressType == AddressTypeDomainName)
                DestinationHost = GetHost(DestinationHost).ToString();

            byte[] Address = GetAddressBytes(AddressType, DestinationHost);
            byte[] Port = GetPortBytes(DestinationPort);

            byte[] Request = new byte[4 + Address.Length + 2];

            Request[0] = (byte)5;
            Request[1] = 0x01;
            Request[2] = 0x00;
            Request[3] = AddressType;

            Address.CopyTo(Request, 4);
            Port.CopyTo(Request, 4 + Address.Length);

            Stream.Write(Request, 0, Request.Length);

            await WaitStream(Stream);

            Stream.Read(Response, 0, Response.Length);

            if (Response[1] != 0x00)
                return ConnectionResult.InvalidProxyResponse;

            return ConnectionResult.OK;
        }

        private async Task WaitStream(NetworkStream Stream)
        {
            int Sleep = 0;
            int Delay = (Stream.ReadTimeout < 10) ? 10 : Stream.ReadTimeout;

            while (!Stream.DataAvailable)
            {
                if (Sleep < Delay)
                {
                    Sleep += 10;
                    await Task.Delay(10);

                    continue;
                }

                throw new ProxyException($"Timeout waiting for data - {Host}:{Port}");
            }
        }

        private IPAddress GetHost(string Host)
        {
            if (IPAddress.TryParse(Host, out IPAddress Ip))
                return Ip;

            return Dns.GetHostAddresses(Host)[0];
        }

        private byte[] GetAddressBytes(byte AddressType, string Host)
        {
            switch (AddressType)
            {
                case AddressTypeIPV4:
                case AddressTypeIPV6:
                    return IPAddress.Parse(Host).GetAddressBytes();
                case AddressTypeDomainName:
                    byte[] Bytes = new byte[Host.Length + 1];

                    Bytes[0] = (byte)Host.Length;
                    Encoding.ASCII.GetBytes(Host).CopyTo(Bytes, 1);

                    return Bytes;
                default:
                    return null;
            }
        }

        private byte GetAddressType(string Host)
        {
            if (IPAddress.TryParse(Host, out IPAddress Ip))
            {
                if (Ip.AddressFamily == AddressFamily.InterNetwork)
                    return AddressTypeIPV4;

                return AddressTypeIPV6;
            }

            return AddressTypeDomainName;
        }

        private byte[] GetIPAddressBytes(string DestinationHost)
        {
            IPAddress Address = null;

            if (!IPAddress.TryParse(DestinationHost, out Address))
            {
                IPAddress[] IPs = Dns.GetHostAddresses(DestinationHost);

                if (IPs.Length > 0)
                    Address = IPs[0];
            }

            return Address.GetAddressBytes();
        }

        private byte[] GetPortBytes(int Port)
        {
            byte[] ArrayBytes = new byte[2];

            ArrayBytes[0] = (byte)(Port / 256);
            ArrayBytes[1] = (byte)(Port % 256);

            return ArrayBytes;
        }
    }
}