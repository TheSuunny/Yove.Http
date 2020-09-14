using System;
using System.IO;
using System.Collections.Specialized;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Yove.Http.Proxy;
using Yove.Http.Events;
using Newtonsoft.Json.Linq;
using Fody;

namespace Yove.Http
{
    [ConfigureAwait(false)]
    public class HttpClient : IDisposable, ICloneable
    {
        public ProxyClient Proxy
        {
            get
            {
                return ProxyBase;
            }
            set
            {
                ProxyBase = value;

                HasConnection = false;
            }
        }

        private ProxyClient ProxyBase { get; set; }

        public NameValueCollection Headers = new NameValueCollection();
        public NameValueCollection TempHeaders = new NameValueCollection();

        public NameValueCollection Cookies { get; set; }

        public string BaseURL { get; set; }
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

        private long SentBytes { get; set; }
        private long TotalSentBytes { get; set; }

        private long ReceivedBytes { get; set; }
        private long TotalReceivedBytes { get; set; }

        private bool IsReceivedHeader { get; set; }
        private bool IsDispose { get; set; }

        public EventHandler<UploadEvent> UploadProgressChanged { get; set; }
        public EventHandler<DownloadEvent> DownloadProgressChanged { get; set; }

        public HttpClient()
        {
            if (EnableCookies && Cookies == null)
                Cookies = new NameValueCollection();
        }

        public HttpClient(string BaseURL)
        {
            this.BaseURL = BaseURL;

            if (EnableCookies && Cookies == null)
                Cookies = new NameValueCollection();
        }

        public async Task<HttpResponse> Post(string URL)
        {
            return await Raw(HttpMethod.POST, URL);
        }

        public async Task<HttpResponse> Post(string URL, string Content, string ContentType = "application/json")
        {
            return await Raw(HttpMethod.POST, URL, new StringContent(Content)
            {
                ContentType = ContentType
            });
        }

        public async Task<HttpResponse> Post(string URL, byte[] Content, string ContentType = "application/octet-stream")
        {
            return await Raw(HttpMethod.POST, URL, new ByteContent(Content)
            {
                ContentType = ContentType
            });
        }

        public async Task<HttpResponse> Post(string URL, Stream Content, string ContentType = "application/octet-stream")
        {
            return await Raw(HttpMethod.POST, URL, new StreamContent(Content)
            {
                ContentType = ContentType
            });
        }

        public async Task<HttpResponse> Post(string URL, HttpContent Content)
        {
            return await Raw(HttpMethod.POST, URL, Content);
        }

        public async Task<HttpResponse> Get(string URL)
        {
            return await Raw(HttpMethod.GET, URL);
        }

        public async Task<string> GetString(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL);

            return Response.Body;
        }

        public async Task<JToken> GetJson(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL);

            return JToken.Parse(Response.Body);
        }

        public async Task<byte[]> GetBytes(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL);

            return await Response.ToBytes();
        }

        public async Task<MemoryStream> GetStream(string URL)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL);

            return await Response.ToMemoryStream();
        }

        public async Task<string> GetToFile(string URL, string LocalPath, string Filename = null)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, URL);

            return await Response.ToFile(LocalPath, Filename);
        }

        public async Task<HttpResponse> Raw(HttpMethod Method, string URL, HttpContent Content = null)
        {
            if (IsDispose)
                throw new ObjectDisposedException("Object disposed.");

            if (string.IsNullOrEmpty(URL))
                throw new ArgumentNullException("URL is null or empty.");

            if ((!URL.StartsWith("https://") && !URL.StartsWith("http://")) && !string.IsNullOrEmpty(BaseURL))
                URL = $"{BaseURL.TrimEnd('/')}/{URL}";

            if (!EnableCookies && Cookies != null)
                Cookies = null;

            this.Method = Method;
            this.Content = Content;

            ReceivedBytes = 0;
            SentBytes = 0;

            if (CheckKeepAlive() || Address.Host != new UriBuilder(URL).Host)
            {
                Close();

                this.Address = new UriBuilder(URL).Uri;

                try
                {
                    Connection = await CreateConnection(Address.Host, Address.Port);

                    NetworkStream = Connection.GetStream();

                    if (Address.Scheme.StartsWith("https"))
                    {
                        SslStream SSL = new SslStream(NetworkStream, false, AcceptAllCertificationsCallback);

                        SSL.AuthenticateAsClient(Address.Host, null, SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls |
                            SslProtocols.Ssl3 | SslProtocols.Ssl2 | SslProtocols.Tls, false);

                        CommonStream = SSL;
                    }
                    else
                    {
                        CommonStream = NetworkStream;
                    }

                    HasConnection = true;

                    if (DownloadProgressChanged != null || UploadProgressChanged != null)
                    {
                        EventWraperStream WraperStream = new EventWraperStream(CommonStream, Connection.SendBufferSize);

                        if (UploadProgressChanged != null)
                        {
                            long Speed = 0;

                            WraperStream.WriteBytesCallback = (e) =>
                            {
                                Speed += e;
                                SentBytes += e;
                            };

                            new Task(async () =>
                            {
                                while ((int)(((double)SentBytes / (double)TotalSentBytes) * 100.0) != 100 && HasConnection)
                                {
                                    await Task.Delay(1000);

                                    if (UploadProgressChanged != null)
                                        UploadProgressChanged(this, new UploadEvent(Speed, SentBytes, TotalSentBytes));

                                    Speed = 0;
                                }
                            }).Start();
                        }

                        if (DownloadProgressChanged != null)
                        {
                            long Speed = 0;

                            WraperStream.ReadBytesCallback = (e) =>
                            {
                                Speed += e;
                                ReceivedBytes += e;
                            };

                            new Task(async () =>
                            {
                                while ((int)(((double)ReceivedBytes / (double)TotalReceivedBytes) * 100.0) != 100 && HasConnection)
                                {
                                    await Task.Delay(1000);

                                    if (IsReceivedHeader)
                                    {
                                        if (DownloadProgressChanged != null)
                                            DownloadProgressChanged(this, new DownloadEvent(Speed, ReceivedBytes, TotalReceivedBytes));
                                    }

                                    Speed = 0;
                                }
                            }).Start();
                        }

                        CommonStream = WraperStream;
                    }
                }
                catch (Exception ex)
                {
                    if (CanReconnect)
                        return await ReconnectFail();

                    throw new Exception($"Failed connected to - {Address.AbsoluteUri}", ex);
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

                string StringHeader = GenerateHeaders(Method, ContentLength, ContentType);

                byte[] StartingLineBytes = Encoding.ASCII.GetBytes($"{Method} {Address.PathAndQuery} HTTP/1.1\r\n");
                byte[] HeadersBytes = Encoding.ASCII.GetBytes(StringHeader);

                SentBytes = 0;
                TotalSentBytes = StartingLineBytes.Length + HeadersBytes.Length + ContentLength;

                CommonStream.Write(StartingLineBytes, 0, StartingLineBytes.Length);
                CommonStream.Write(HeadersBytes, 0, HeadersBytes.Length);

                if (Content != null && ContentLength != 0)
                    Content.Write(CommonStream);
            }
            catch (Exception ex)
            {
                if (CanReconnect)
                    return await ReconnectFail();

                throw new Exception($"Failed send data to - {Address.AbsoluteUri}", ex);
            }

            try
            {
                IsReceivedHeader = false;

                Response = new HttpResponse(this);

                TotalReceivedBytes = Response.ResponseLength;
                IsReceivedHeader = true;
            }
            catch (Exception ex)
            {
                if (CanReconnect)
                    return await ReconnectFail();

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
                return await Raw(Method, Response.Location, Content);

            return Response;
        }

        private bool CheckKeepAlive()
        {
            int MaxRequest = (Response != null && Response.KeepAliveMax != 0) ? Response.KeepAliveMax : KeepAliveMaxRequest;
            int Timeout = (Response != null && Response.KeepAliveTimeout != 0) ? Response.KeepAliveTimeout : KeepAliveTimeOut;

            if (KeepAliveRequestCount == 0 || KeepAliveRequestCount == MaxRequest ||
                (Response != null && Response.ConnectionClose) || !HasConnection)
                return true;

            if (WhenConnectionIdle.AddMilliseconds(TimeOut) < DateTime.Now)
                return true;

            return false;
        }

        private async Task<TcpClient> CreateConnection(string Host, int Port)
        {
            if (Proxy == null)
            {
                TcpClient TcpClient = new TcpClient
                {
                    ReceiveTimeout = ReadWriteTimeOut,
                    SendTimeout = ReadWriteTimeOut
                };

                TcpClient.Connect(Host, Port);

                if (!TcpClient.Connected)
                    throw new Exception($"Failed Connection - {Address.AbsoluteUri}");

                return TcpClient;
            }
            else
            {
                return await Proxy.CreateConnection(Host, Port);
            }
        }

        private string GenerateHeaders(HttpMethod Method, long ContentLength = 0, string ContentType = null)
        {
            NameValueCollection RawHeaders = new NameValueCollection();

            if (Address.IsDefaultPort)
                RawHeaders["Host"] = Address.Host;
            else
                RawHeaders["Host"] = $"{Address.Host}:{Address.Port}";

            if (!string.IsNullOrEmpty(UserAgent))
                RawHeaders["User-Agent"] = UserAgent;

            RawHeaders["Accept"] = Accept;
            RawHeaders["Accept-Language"] = Language;

            if (EnableEncodingContent)
                RawHeaders["Accept-Encoding"] = "gzip, deflate";

            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                string Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));

                RawHeaders["Authorization"] = $"Basic {Auth}";
            }

            if (!string.IsNullOrEmpty(Referer))
                RawHeaders["Referer"] = Referer;

            if (!string.IsNullOrEmpty(Authorization))
                RawHeaders["Authorization"] = Authorization;

            if (CharacterSet != null)
            {
                if (CharacterSet != Encoding.UTF8)
                    RawHeaders["Accept-Charset"] = $"{CharacterSet.WebName},utf-8";
                else
                    RawHeaders["Accept-Charset"] = "utf-8";
            }

            if (Method != HttpMethod.GET)
            {
                if (ContentLength > 0)
                    RawHeaders["Content-Type"] = ContentType;

                RawHeaders["Content-Length"] = ContentLength.ToString();
            }

            if (Proxy != null && Proxy.Type == ProxyType.Http)
            {
                if (KeepAlive)
                {
                    RawHeaders["Proxy-Connection"] = "keep-alive";

                    KeepAliveRequestCount++;
                }
                else
                {
                    RawHeaders["Proxy-Connection"] = "close";
                }
            }
            else
            {
                if (KeepAlive)
                {
                    RawHeaders["Connection"] = "keep-alive";

                    KeepAliveRequestCount++;
                }
                else
                {
                    RawHeaders["Connection"] = "close";
                }
            }

            if (Cookies != null && Cookies.Count > 0)
            {
                string CookieBuilder = string.Empty;

                foreach (string Cookie in Cookies)
                    CookieBuilder += $"{Cookie}={Cookies[Cookie]}; ";

                RawHeaders["Cookie"] = CookieBuilder.TrimEnd();
            }

            StringBuilder Builder = new StringBuilder();

            foreach (string Header in RawHeaders)
                Builder.AppendFormat($"{Header}: {RawHeaders[Header]}\r\n");

            foreach (string Header in Headers)
                Builder.AppendFormat($"{Header}: {Headers[Header]}\r\n");

            foreach (string Header in TempHeaders)
                Builder.AppendFormat($"{Header}: {TempHeaders[Header]}\r\n");

            TempHeaders.Clear();

            return $"{Builder}\r\n";
        }

        private async Task<HttpResponse> ReconnectFail()
        {
            Close();

            ReconnectCount++;

            await Task.Delay(ReconnectDelay);

            return await Raw(Method, Address.AbsoluteUri, Content);
        }

        private static bool AcceptAllCertifications(object sender, X509Certificate certification, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public void AddTempHeader(string Key, string Value)
        {
            if (string.IsNullOrEmpty(Key))
                throw new ArgumentNullException("Key is null or empty.");

            if (string.IsNullOrEmpty(Value))
                throw new ArgumentNullException("Value is null or empty.");

            TempHeaders.Add(Key, Value);
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public void Close()
        {
            try
            {
                Connection?.Close();
                Connection?.Dispose();

                NetworkStream?.Close();
                NetworkStream?.Dispose();

                CommonStream?.Close();
                CommonStream?.Dispose();

                Content?.Dispose();

                KeepAliveRequestCount = 0;
                HasConnection = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public void Dispose()
        {
            Close();

            Proxy = null;
            Response = null;
            Headers = null;
            TempHeaders = null;
            Cookies = null;
            Connection = null;
            NetworkStream = null;
            CommonStream = null;
            Content = null;

            IsDispose = true;
        }
    }
}