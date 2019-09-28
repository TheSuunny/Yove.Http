using System;
using System.IO;
using System.Collections.Specialized;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yove.Http.Proxy;

namespace Yove.Http
{
    public class HttpClient : IDisposable, ICloneable
    {
        public ProxyClient Proxy
        {
            get
            {
                return proxy;
            }
            set
            {
                proxy = value;

                HasConnection = false;
            }
        }

        private ProxyClient proxy { get; set; }

        public NameValueCollection Headers = new NameValueCollection();
        public NameValueCollection Cookies { get; set; }

        public string Username { get; set; }
        public string Password { get; set; }
        public string Language { get; set; } = "en-US,en;q=0.9";
        public string Referer { get; set; }
        public string UserAgent { get; set; }
        public string Authorization { get; set; }
        public string Accept { get; set; } = "*/*";

        public Encoding CharacterSet { get; set; }

        public bool KeepAlive { get; set; } = true;
        public int KeepAliveTimeOut { get; set; } = 60000;
        public int KeepAliveMaxRequest { get; set; } = 100;

        public bool EnableEncodingContent { get; set; } = true;
        public bool EnableAutoRedirect { get; set; } = true;
        public bool EnableProtocolError { get; set; } = true;
        public bool EnableCookies { get; set; } = true;
        public bool EnableReconnect { get; set; } = true;
        public bool HasConnection { get; set; }

        public int ReconnectLimit { get; set; } = 3;
        public int ReconnectDelay { get; set; } = 1000;
        public int TimeOut { get; set; } = 60000;
        public int ReadWriteTimeOut { get; set; } = 60000;

        public Uri Address { get; private set; }

        private HttpResponse Response { get; set; }

        private int ReconnectCount { get; set; }
        private int KeepAliveRequestCount { get; set; }

        private DateTime WhenConnectionIdle { get; set; }

        public string this[string Key]
        {
            get
            {
                if (string.IsNullOrEmpty(Key))
                    throw new ArgumentNullException("Key is null or empty.");

                return Headers[Key];
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                    Headers[Key] = value;
            }
        }

        private bool CanReconnect
        {
            get
            {
                return EnableReconnect && ReconnectCount < ReconnectLimit;
            }
        }

        internal TcpClient Connection { get; set; }
        internal NetworkStream NetworkStream { get; set; }
        internal Stream CommonStream { get; set; }
        internal HttpMethod Method { get; set; }
        internal HttpContent Content { get; set; }
        internal RemoteCertificateValidationCallback AcceptAllCertificationsCallback = new RemoteCertificateValidationCallback(AcceptAllCertifications);

        public async Task<HttpResponse> Post(string URL)
        {
            return await Raw(HttpMethod.POST, URL).ConfigureAwait(false);
        }

        public async Task<HttpResponse> Post(string URL, string Content, string ContentType = "application/json")
        {
            return await Raw(HttpMethod.POST, URL, new StringContent(Content)
            {
                ContentType = ContentType
            }).ConfigureAwait(false);
        }

        public async Task<HttpResponse> Post(string URL, byte[] Content, string ContentType = "application/octet-stream")
        {
            return await Raw(HttpMethod.POST, URL, new ByteContent(Content)
            {
                ContentType = ContentType
            }).ConfigureAwait(false);
        }

        public async Task<HttpResponse> Post(string URL, Stream Content, string ContentType = "application/octet-stream")
        {
            return await Raw(HttpMethod.POST, URL, new StreamContent(Content)
            {
                ContentType = ContentType
            }).ConfigureAwait(false);
        }

        public async Task<HttpResponse> Post(string URL, HttpContent Content)
        {
            return await Raw(HttpMethod.POST, URL, Content).ConfigureAwait(false);
        }

        public async Task<HttpResponse> Get(string URL)
        {
            return await Raw(HttpMethod.GET, URL).ConfigureAwait(false);
        }

        public async Task<string> GetString(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL).ConfigureAwait(false);

            return Response.Body;
        }

        public async Task<byte[]> GetBytes(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL).ConfigureAwait(false);

            return await Response.ToBytes().ConfigureAwait(false);
        }

        public async Task<MemoryStream> GetStream(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL).ConfigureAwait(false);

            return await Response.ToMemoryStream().ConfigureAwait(false);
        }

        public async Task<string> GetToFile(string URL, string LocalPath, string Filename = null)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL).ConfigureAwait(false);

            return await Response.ToFile(LocalPath, Filename).ConfigureAwait(false);
        }

        public async Task<HttpResponse> Raw(HttpMethod Method, string URL, HttpContent Content = null)
        {
            if (string.IsNullOrEmpty(URL))
                throw new ArgumentNullException("URL is null or empty.");

            this.Method = Method;
            this.Content = Content;

            if (EnableCookies && Cookies == null)
                Cookies = new NameValueCollection();

            if (CheckKeepAlive() || Address.Host != new UriBuilder(URL).Host)
            {
                Dispose();

                this.Address = new UriBuilder(URL).Uri;

                try
                {
                    Connection = await CreateConnection(Address.Host, Address.Port).ConfigureAwait(false);

                    NetworkStream = Connection.GetStream();

                    if (Address.Scheme.StartsWith("https"))
                    {
                        try
                        {
                            SslStream SSL = new SslStream(NetworkStream, false, AcceptAllCertificationsCallback);

                            await SSL.AuthenticateAsClientAsync(Address.Host, null, SslProtocols.Tls12, false).ConfigureAwait(false);

                            CommonStream = SSL;
                        }
                        catch (Exception ex)
                        {
                            throw ex;
                        }
                    }
                    else
                    {
                        CommonStream = NetworkStream;
                    }

                    HasConnection = true;
                }
                catch (Exception ex)
                {
                    if (CanReconnect)
                        return await ReconnectFail().ConfigureAwait(false);

                    throw ex;
                }
            }
            else
            {
                this.Address = new UriBuilder(URL).Uri;
            }

            try
            {
                long ContentLength = 0L;
                string ContentType = null;

                if (Method != HttpMethod.GET && Content != null)
                {
                    ContentType = Content.ContentType;
                    ContentLength = Content.ContentLength;
                }

                byte[] StartingLineBytes = Encoding.ASCII.GetBytes($"{Method} {Address.PathAndQuery} HTTP/1.1\r\n");
                byte[] HeadersBytes = Encoding.ASCII.GetBytes(GenerateHeaders(Method, ContentLength, ContentType));

                await CommonStream.WriteAsync(StartingLineBytes, 0, StartingLineBytes.Length).ConfigureAwait(false);
                await CommonStream.WriteAsync(HeadersBytes, 0, HeadersBytes.Length).ConfigureAwait(false);

                if (Content != null && ContentLength != 0)
                    await Content.WriteAsync(CommonStream).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (CanReconnect)
                    return await ReconnectFail().ConfigureAwait(false);

                throw new Exception($"Failed send data to - {Address.AbsoluteUri}", ex);
            }

            try
            {
                Response = new HttpResponse(this);
            }
            catch (Exception ex)
            {
                if (CanReconnect || KeepAlive)
                    return await ReconnectFail().ConfigureAwait(false);

                throw new Exception($"Failed receive data from - {Address.AbsoluteUri}", ex);
            }

            ReconnectCount = 0;
            WhenConnectionIdle = DateTime.Now;

            if (EnableProtocolError)
            {
                if ((int)Response.StatusCode >= 400 && (int)Response.StatusCode < 500)
                    throw new Exception($"[Client] | Status Code - {Response.StatusCode}\r\n{Response.Body}");

                if ((int)Response.StatusCode >= 500)
                    throw new Exception($"[Server] | Status Code - {Response.StatusCode}\r\n{Response.Body}");
            }

            if (EnableAutoRedirect && Response.Location != null)
                return await Raw(Method, Response.Location, Content).ConfigureAwait(false);

            return Response;
        }

        private bool CheckKeepAlive()
        {
            int MaxRequest = (Response != null && Response.KeepAliveMax != 0) ? Response.KeepAliveMax : KeepAliveMaxRequest;
            int Timeout = (Response != null && Response.KeepAliveTimeout != 0) ? Response.KeepAliveTimeout : KeepAliveTimeOut;

            if (KeepAliveRequestCount == 0 || KeepAliveRequestCount == MaxRequest || (Response != null && Response.ConnectionClose) || !HasConnection)
                return true;

            if (WhenConnectionIdle.AddMilliseconds(TimeOut) < DateTime.Now)
                return true;

            return false;
        }

        private async Task<TcpClient> CreateConnection(string Host, int Port)
        {
            if (Proxy == null)
            {
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

                    throw new Exception($"Failed Connection - {Address.AbsoluteUri}");
                }

                TcpClient.ReceiveTimeout = TcpClient.SendTimeout = ReadWriteTimeOut;

                return TcpClient;
            }
            else
            {
                return await Proxy.CreateConnection(Host, Port).ConfigureAwait(false);
            }
        }

        private string GenerateHeaders(HttpMethod Method, long ContentLength = 0, string ContentType = null)
        {
            if (Address.IsDefaultPort)
                Headers["Host"] = Address.Host;
            else
                Headers["Host"] = $"{Address.Host}:{Address.Port}";

            if (!string.IsNullOrEmpty(UserAgent))
                Headers["User-Agent"] = UserAgent;

            Headers["Accept"] = Accept;
            Headers["Accept-Language"] = Language;

            if (EnableEncodingContent)
                Headers["Accept-Encoding"] = "gzip,deflate";

            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                string Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));

                Headers["Authorization"] = $"Basic {Auth}";
            }

            if (!string.IsNullOrEmpty(Referer))
                Headers["Referer"] = Referer;

            if (!string.IsNullOrEmpty(Authorization))
                Headers["Authorization"] = Authorization;

            if (CharacterSet != null)
            {
                if (CharacterSet != Encoding.UTF8)
                    Headers["Accept-Charset"] = $"{CharacterSet.WebName},utf-8";
                else
                    Headers["Accept-Charset"] = "utf-8";
            }

            if (Method != HttpMethod.GET)
            {
                if (ContentLength > 0)
                    Headers["Content-Type"] = ContentType;

                Headers["Content-Length"] = ContentLength.ToString();
            }

            if (Proxy != null && Proxy.Type == ProxyType.Http)
            {
                if (KeepAlive)
                {
                    Headers["Proxy-Connection"] = "keep-alive";

                    KeepAliveRequestCount++;
                }
                else
                {
                    Headers["Proxy-Connection"] = "close";
                }
            }
            else
            {
                if (KeepAlive)
                {
                    Headers["Connection"] = "keep-alive";

                    KeepAliveRequestCount++;
                }
                else
                {
                    Headers["Connection"] = "close";
                }
            }

            if (Cookies != null && Cookies.Count > 0)
            {
                string CookieBuilder = string.Empty;

                foreach (var Cookie in Cookies)
                    CookieBuilder += $"{Cookie}={Cookies[(string)Cookie]}; ";

                Headers["Cookie"] = CookieBuilder.TrimEnd();
            }

            StringBuilder Builder = new StringBuilder();

            foreach (var Header in Headers)
                Builder.AppendFormat($"{Header}: {Headers[(string)Header]}\r\n");

            Headers.Clear();

            return $"{Builder}\r\n";
        }

        private async Task<HttpResponse> ReconnectFail()
        {
            Dispose();

            ReconnectCount++;

            await Task.Delay(ReconnectDelay).ConfigureAwait(false);

            return await Raw(Method, Address.AbsoluteUri, Content).ConfigureAwait(false);
        }

        private static bool AcceptAllCertifications(object sender, X509Certificate certification, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public void Dispose()
        {
            if (Connection != null)
            {
                Connection.Close();
                Connection.Dispose();

                NetworkStream.Dispose();
                CommonStream.Dispose();

                KeepAliveRequestCount = 0;
                HasConnection = false;
            }
        }
    }
}