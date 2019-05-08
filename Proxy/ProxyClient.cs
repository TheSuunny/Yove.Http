using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Yove.Http.Proxy
{
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
                throw new ArgumentNullException("Host is null or empty");

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535");

            this.Host = Host;
            this.Port = Port;
            this.Type = Type;
        }

        public ProxyClient(string Proxy, ProxyType Type)
        {
            if (string.IsNullOrEmpty(Proxy) || !Proxy.Contains(":"))
                throw new ArgumentNullException("Proxy is null or empty or invalid type");

            string Host = Proxy.Split(':')[0];
            int Port = Convert.ToInt32(Proxy.Split(':')[1]);

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535");

            this.Host = Host;
            this.Port = Port;
            this.Type = Type;
        }

        internal async Task<TcpClient> CreateConnection(string DestinationHost, int DestinationPort)
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentNullException("Host is null or empty");

            if (Port < 0 || Port > 65535)
                throw new ArgumentNullException("Port goes beyond < 0 or > 65535");

            TcpClient TcpClient = new TcpClient();

            Exception ConnectionEx = null;

            ManualResetEventSlim ConnectionEvent = new ManualResetEventSlim();

            TcpClient.BeginConnect(Host, Port, new AsyncCallback((ar) =>
            {
                try
                {
                    TcpClient.EndConnect(ar);
                }
                catch (Exception ex)
                {
                    ConnectionEx = ex;
                }

                ConnectionEvent.Set();

            }), TcpClient);

            if (!ConnectionEvent.Wait(TimeOut) || ConnectionEx != null || !TcpClient.Connected)
            {
                TcpClient.Close();

                throw new ProxyException($"Failed Connection to proxy - {Host}:{Port}");
            }

            TcpClient.ReceiveTimeout = TcpClient.SendTimeout = ReadWriteTimeOut;

            ConnectionResult Connection = await SendCommand(TcpClient.GetStream(), DestinationHost, DestinationPort).ConfigureAwait(false);

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
                    return await SendHttp(Stream, DestinationHost, DestinationPort).ConfigureAwait(false);
                case ProxyType.Socks4:
                    return await SendSocks4(Stream, DestinationHost, DestinationPort).ConfigureAwait(false);
                case ProxyType.Socks5:
                    return await SendSocks5(Stream, DestinationHost, DestinationPort).ConfigureAwait(false);
                default:
                    throw new ProxyException("Unsupported proxy type");
            }
        }

        private async Task<ConnectionResult> SendHttp(NetworkStream Stream, string DestinationHost, int DestinationPort)
        {
            if (DestinationPort == 80)
                return ConnectionResult.OK;

            byte[] RequestBuffer = Encoding.ASCII.GetBytes($"CONNECT {DestinationHost}:{DestinationPort} HTTP/1.1\r\n\r\n");

            await Stream.WriteAsync(RequestBuffer, 0, RequestBuffer.Length).ConfigureAwait(false);

            await WaitStream(Stream).ConfigureAwait(false);

            byte[] ResponseBuffer = new byte[100];

            StringBuilder Response = new StringBuilder();

            while (Stream.DataAvailable)
            {
                int Bytes = await Stream.ReadAsync(ResponseBuffer, 0, 100).ConfigureAwait(false);

                Response.Append(Encoding.ASCII.GetString(ResponseBuffer, 0, Bytes));
            }

            if (Response.Length == 0)
                new Exception("Received empty response");

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

            await Stream.WriteAsync(Request, 0, Request.Length).ConfigureAwait(false);

            await WaitStream(Stream).ConfigureAwait(false);

            await Stream.ReadAsync(Response, 0, Response.Length).ConfigureAwait(false);

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

            await Stream.WriteAsync(Auth, 0, Auth.Length).ConfigureAwait(false);

            await WaitStream(Stream).ConfigureAwait(false);

            await Stream.ReadAsync(Response, 0, Response.Length).ConfigureAwait(false);

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

            await Stream.WriteAsync(Request, 0, Request.Length).ConfigureAwait(false);

            await WaitStream(Stream).ConfigureAwait(false);

            await Stream.ReadAsync(Response, 0, Response.Length).ConfigureAwait(false);

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
                    await Task.Delay(10).ConfigureAwait(false);

                    continue;
                }

                throw new ProxyException($"Timeout waiting for data - {Host}:{Port}");
            }
        }

        private IPAddress GetHost(string Host)
        {
            if (IPAddress.TryParse(Host, out IPAddress Ip)) return Ip;

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