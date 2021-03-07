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
using System.Threading;

namespace Yove.Http
{
    [ConfigureAwait(false)]
    public class HttpClient : IDisposable, ICloneable
    {
        public NameValueCollection Headers = new NameValueCollection();
        public NameValueCollection TempHeaders = new NameValueCollection();

        public NameValueCollection Cookies { get; set; }

        public string BaseUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Language { get; set; } = "en-US,en;q=0.9";
        public string Referer { get; set; }
        public string UserAgent { get; set; }
        public string Authorization { get; set; }
        public string Accept { get; set; } = "*/*";

        public Encoding CharacterSet { get; set; }

        public int KeepAliveTimeOut { get; set; } = 60000;
        public int KeepAliveMaxRequest { get; set; } = 100;

        public bool KeepAlive { get; set; } = true;
        public bool EnableEncodingContent { get; set; } = true;
        public bool EnableAutoRedirect { get; set; } = true;
        public bool EnableProtocolError { get; set; } = true;
        public bool EnableCookies { get; set; } = true;
        public bool EnableReconnect { get; set; } = true;
        public bool HasConnection { get; set; }
        public bool IsDisposed { get; set; }

        public int ReconnectLimit { get; set; } = 3;
        public int ReconnectDelay { get; set; } = 1000;
        public int TimeOut { get; set; } = 60000;
        public int ReadWriteTimeOut { get; set; } = 60000;

        public Uri Address { get; private set; }

        public CancellationToken CancellationToken { get; set; }

        internal TcpClient Connection { get; set; }
        internal NetworkStream NetworkStream { get; set; }
        internal Stream CommonStream { get; set; }
        internal HttpMethod Method { get; set; }
        internal HttpContent Content { get; set; }

        internal RemoteCertificateValidationCallback AcceptAllCertificationsCallback = new RemoteCertificateValidationCallback(AcceptAllCertifications);

        private HttpResponse _response { get; set; }

        private ProxyClient _proxy { get; set; }

        private int _reconnectCount { get; set; }
        private int _keepAliveRequestCount { get; set; }

        private DateTime _whenConnectionIdle { get; set; }

        private long _sentBytes { get; set; }
        private long _totalSentBytes { get; set; }
        private long _receivedBytes { get; set; }
        private long _totalReceivedBytes { get; set; }

        private bool _isReceivedHeader { get; set; }

        public EventHandler<UploadEvent> UploadProgressChanged { get; set; }
        public EventHandler<DownloadEvent> DownloadProgressChanged { get; set; }

        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentNullException("Key is null or empty.");

                return Headers[key];
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                    Headers[key] = value;
            }
        }

        public ProxyClient Proxy
        {
            get
            {
                return _proxy;
            }
            set
            {
                _proxy = value;

                HasConnection = false;
            }
        }

        private bool CanReconnect
        {
            get
            {
                return EnableReconnect && _reconnectCount < ReconnectLimit;
            }
        }

        public HttpClient()
        {
            if (EnableCookies && Cookies == null)
                Cookies = new NameValueCollection();
        }

        public HttpClient(CancellationToken token) : this()
        {
            if (token == null)
                throw new ArgumentNullException("Token cannot be null");

            CancellationToken = token;
        }

        public HttpClient(string baseUrl)
        {
            BaseUrl = baseUrl;

            if (EnableCookies && Cookies == null)
                Cookies = new NameValueCollection();
        }

        public HttpClient(string baseUrl, CancellationToken token) : this(baseUrl)
        {
            if (token == null)
                throw new ArgumentNullException("Token cannot be null");

            CancellationToken = token;
        }

        public async Task<HttpResponse> Post(string url)
        {
            return await Raw(HttpMethod.POST, url);
        }

        public async Task<HttpResponse> Post(string url, string body, string contentType = "application/json")
        {
            return await Raw(HttpMethod.POST, url, new StringContent(body)
            {
                ContentType = contentType
            });
        }

        public async Task<HttpResponse> Post(string url, byte[] body, string contentType = "application/octet-stream")
        {
            return await Raw(HttpMethod.POST, url, new ByteContent(body)
            {
                ContentType = contentType
            });
        }

        public async Task<HttpResponse> Post(string url, Stream body, string contentType = "application/octet-stream")
        {
            return await Raw(HttpMethod.POST, url, new StreamContent(body)
            {
                ContentType = contentType
            });
        }

        public async Task<HttpResponse> Post(string url, HttpContent body)
        {
            return await Raw(HttpMethod.POST, url, body);
        }

        public async Task<HttpResponse> Get(string url)
        {
            return await Raw(HttpMethod.GET, url);
        }

        public async Task<string> GetString(string url)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, url);

            return Response.Body;
        }

        public async Task<JToken> GetJson(string url)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, url);

            return JToken.Parse(Response.Body);
        }

        public async Task<byte[]> GetBytes(string url)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, url);

            return await Response.ToBytes();
        }

        public async Task<MemoryStream> GetStream(string url)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, url);

            return await Response.ToMemoryStream();
        }

        public async Task<string> GetToFile(string url, string path, string filename = null)
        {
            HttpResponse Response = await Raw(HttpMethod.GET, url);

            return await Response.ToFile(path, filename);
        }

        public async Task<HttpResponse> Raw(HttpMethod method, string url, HttpContent body = null)
        {
            if (IsDisposed)
                throw new ObjectDisposedException("Object disposed.");

            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException("URL is null or empty.");

            if (CancellationToken != null && !CancellationToken.IsCancellationRequested)
            {
                CancellationToken.Register(() =>
                {
                    _reconnectCount = ReconnectLimit;

                    Close();
                });
            }

            if ((!url.StartsWith("https://") && !url.StartsWith("http://")) && !string.IsNullOrEmpty(BaseUrl))
                url = $"{BaseUrl.TrimEnd('/')}/{url}";

            if (!EnableCookies && Cookies != null)
                Cookies = null;

            Method = method;
            Content = body;

            _receivedBytes = 0;
            _sentBytes = 0;

            TimeSpan timeResponseStart = DateTime.Now.TimeOfDay;

            if (CheckKeepAlive() || Address.Host != new UriBuilder(url).Host)
            {
                Close();

                Address = new UriBuilder(url).Uri;

                try
                {
                    Connection = await CreateConnection(Address.Host, Address.Port);

                    NetworkStream = Connection.GetStream();

                    if (Address.Scheme.StartsWith("https"))
                    {
                        SslStream sslStream = new SslStream(NetworkStream, false, AcceptAllCertificationsCallback);

                        await sslStream.AuthenticateAsClientAsync(Address.Host, null, SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls |
                            SslProtocols.Ssl3 | SslProtocols.Ssl2 | SslProtocols.Tls, false);

                        CommonStream = sslStream;
                    }
                    else
                    {
                        CommonStream = NetworkStream;
                    }

                    HasConnection = true;

                    if (DownloadProgressChanged != null || UploadProgressChanged != null)
                    {
                        EventStreamWrapper eventStream = new EventStreamWrapper(CommonStream, Connection.SendBufferSize);

                        if (UploadProgressChanged != null)
                        {
                            long speed = 0;

                            eventStream.WriteBytesCallback = (e) =>
                            {
                                speed += e;
                                _sentBytes += e;
                            };

                            new Task(async () =>
                            {
                                while ((int)(((double)_sentBytes / (double)_totalSentBytes) * 100.0) != 100 && HasConnection)
                                {
                                    await Task.Delay(1000);

                                    if (UploadProgressChanged != null)
                                        UploadProgressChanged(this, new UploadEvent(speed, _sentBytes, _totalSentBytes));

                                    speed = 0;
                                }
                            }).Start();
                        }

                        if (DownloadProgressChanged != null)
                        {
                            long speed = 0;

                            eventStream.ReadBytesCallback = (e) =>
                            {
                                speed += e;
                                _receivedBytes += e;
                            };

                            new Task(async () =>
                            {
                                while ((int)(((double)_receivedBytes / (double)_totalReceivedBytes) * 100.0) != 100 && HasConnection)
                                {
                                    await Task.Delay(1000);

                                    if (_isReceivedHeader)
                                    {
                                        if (DownloadProgressChanged != null)
                                            DownloadProgressChanged(this, new DownloadEvent(speed, _receivedBytes, _totalReceivedBytes));
                                    }

                                    speed = 0;
                                }
                            }).Start();
                        }

                        CommonStream = eventStream;
                    }
                }
                catch (Exception ex)
                {
                    if (CanReconnect)
                        return await Reconnect(method, url, body);

                    throw new Exception($"Failed connected to - {Address.AbsoluteUri}", ex);
                }
            }
            else
            {
                Address = new UriBuilder(url).Uri;
            }

            try
            {
                long contentLength = 0L;
                string contentType = null;

                if (Method != HttpMethod.GET && Content != null)
                {
                    contentType = Content.ContentType;
                    contentLength = Content.ContentLength;
                }

                string stringHeader = GenerateHeaders(Method, contentLength, contentType);

                byte[] startingLineBytes = Encoding.ASCII.GetBytes($"{Method} {Address.PathAndQuery} HTTP/1.1\r\n");
                byte[] headersBytes = Encoding.ASCII.GetBytes(stringHeader);

                _sentBytes = 0;
                _totalSentBytes = startingLineBytes.Length + headersBytes.Length + contentLength;

                CommonStream.Write(startingLineBytes, 0, startingLineBytes.Length);
                CommonStream.Write(headersBytes, 0, headersBytes.Length);

                if (Content != null && contentLength != 0)
                    Content.Write(CommonStream);
            }
            catch (Exception ex)
            {
                if (CanReconnect)
                    return await Reconnect(method, url, body);

                throw new Exception($"Failed send data to - {Address.AbsoluteUri}", ex);
            }

            try
            {
                _isReceivedHeader = false;

                _response = new HttpResponse(this);

                await _response.LoadBody();

                _totalReceivedBytes = _response.ResponseLength;
                _isReceivedHeader = true;
            }
            catch (Exception ex)
            {
                if (CanReconnect)
                    return await Reconnect(method, url, body);

                throw new Exception($"Failed receive data from - {Address.AbsoluteUri}", ex);
            }

            _reconnectCount = 0;
            _whenConnectionIdle = DateTime.Now;

            _response.TimeResponse = (DateTime.Now - timeResponseStart).TimeOfDay;

            if (EnableProtocolError)
            {
                if ((int)_response.StatusCode >= 400 && (int)_response.StatusCode < 500)
                    throw new Exception($"[Client] | Status Code - {_response.StatusCode}\r\n{_response.Body}");

                if ((int)_response.StatusCode >= 500)
                    throw new Exception($"[Server] | Status Code - {_response.StatusCode}\r\n{_response.Body}");
            }

            if (EnableAutoRedirect && _response.Location != null)
                return await Raw(Method, _response.Location, Content);

            return _response;
        }

        private bool CheckKeepAlive()
        {
            int maxRequest = (_response != null && _response.KeepAliveMax != 0) ? _response.KeepAliveMax : KeepAliveMaxRequest;

            if (_keepAliveRequestCount == 0 || _keepAliveRequestCount == maxRequest ||
                (_response != null && _response.ConnectionClose) || !HasConnection ||
                _whenConnectionIdle.AddMilliseconds(TimeOut) < DateTime.Now)
            {
                return true;
            }

            return false;
        }

        private async Task<TcpClient> CreateConnection(string host, int port)
        {
            if (Proxy == null)
            {
                TcpClient tcpClient = new TcpClient
                {
                    ReceiveTimeout = ReadWriteTimeOut,
                    SendTimeout = ReadWriteTimeOut
                };

                TaskCompletionSource<bool> taskCompletion = new TaskCompletionSource<bool>();

                using (CancellationTokenSource cancellationToken = new CancellationTokenSource(TimeSpan.FromMilliseconds(ReadWriteTimeOut)))
                {
                    Task connectionTask = tcpClient.ConnectAsync(host, port);

                    using (cancellationToken.Token.Register(() => taskCompletion.TrySetResult(true)))
                    {
                        if (connectionTask != await Task.WhenAny(connectionTask, taskCompletion.Task))
                            throw new Exception($"Failed Connection - {Address.AbsoluteUri}");
                    }
                }

                if (!tcpClient.Connected)
                    throw new Exception($"Failed Connection - {Address.AbsoluteUri}");

                return tcpClient;
            }
            else
            {
                return await Proxy.CreateConnection(host, port);
            }
        }

        private string GenerateHeaders(HttpMethod method, long contentLength = 0, string contentType = null)
        {
            NameValueCollection rawHeaders = new NameValueCollection();

            if (Address.IsDefaultPort)
                rawHeaders["Host"] = Address.Host;
            else
                rawHeaders["Host"] = $"{Address.Host}:{Address.Port}";

            if (!string.IsNullOrEmpty(UserAgent))
                rawHeaders["User-Agent"] = UserAgent;

            rawHeaders["Accept"] = Accept;
            rawHeaders["Accept-Language"] = Language;

            if (EnableEncodingContent)
                rawHeaders["Accept-Encoding"] = "gzip, deflate, br";

            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
                rawHeaders["Authorization"] = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"))}";

            if (!string.IsNullOrEmpty(Referer))
                rawHeaders["Referer"] = Referer;

            if (!string.IsNullOrEmpty(Authorization))
                rawHeaders["Authorization"] = Authorization;

            if (CharacterSet != null)
            {
                if (CharacterSet != Encoding.UTF8)
                    rawHeaders["Accept-Charset"] = $"{CharacterSet.WebName},utf-8";
                else
                    rawHeaders["Accept-Charset"] = "utf-8";
            }

            if (method != HttpMethod.GET)
            {
                if (contentLength > 0)
                    rawHeaders["Content-Type"] = contentType;

                rawHeaders["Content-Length"] = contentLength.ToString();
            }

            if (Proxy != null && Proxy.Type == ProxyType.Http)
            {
                if (KeepAlive)
                {
                    rawHeaders["Proxy-Connection"] = "keep-alive";

                    _keepAliveRequestCount++;
                }
                else
                {
                    rawHeaders["Proxy-Connection"] = "close";
                }
            }
            else
            {
                if (KeepAlive)
                {
                    rawHeaders["Connection"] = "keep-alive";

                    _keepAliveRequestCount++;
                }
                else
                {
                    rawHeaders["Connection"] = "close";
                }
            }

            if (Cookies != null && Cookies.Count > 0)
            {
                string cookiesBuilder = string.Empty;

                foreach (string Cookie in Cookies)
                    cookiesBuilder += $"{Cookie}={Cookies[Cookie]}; ";

                rawHeaders["Cookie"] = cookiesBuilder.TrimEnd();
            }

            StringBuilder headerBuuilder = new StringBuilder();

            foreach (string header in rawHeaders)
                headerBuuilder.AppendFormat($"{header}: {rawHeaders[header]}\r\n");

            foreach (string header in Headers)
                headerBuuilder.AppendFormat($"{header}: {Headers[header]}\r\n");

            foreach (string header in TempHeaders)
                headerBuuilder.AppendFormat($"{header}: {TempHeaders[header]}\r\n");

            TempHeaders.Clear();

            return $"{headerBuuilder}\r\n";
        }

        public void AddTempHeader(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException("Key is null or empty.");

            if (string.IsNullOrEmpty(value))
                throw new ArgumentNullException("Value is null or empty.");

            TempHeaders.Add(key, value);
        }

        public void AddRawCookies(string source)
        {
            if (string.IsNullOrEmpty(source))
                throw new ArgumentNullException("Value is null or empty.");

            if (!EnableCookies)
                throw new Exception("Cookies is disabled.");

            if (source.Contains("Cookie:"))
                source = source.Replace("Cookie:", "").Trim();

            foreach (string cookie in source.Split(';'))
            {
                string key = cookie.Split('=')[0]?.Trim();
                string value = cookie.Split('=')[1]?.Trim();

                Cookies[key] = value;
            }
        }

        private async Task<HttpResponse> Reconnect(HttpMethod method, string url, HttpContent body = null)
        {
            Close();

            _reconnectCount++;

            await Task.Delay(ReconnectDelay);

            return await Raw(method, url, body);
        }

        private static bool AcceptAllCertifications(object sender, X509Certificate certification, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public void Close()
        {
            Connection?.Close();
            Connection?.Dispose();

            NetworkStream?.Close();
            NetworkStream?.Dispose();

            CommonStream?.Close();
            CommonStream?.Dispose();

            _response = null;
            _keepAliveRequestCount = 0;

            HasConnection = false;
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;

                Close();

                Content?.Dispose();

                Content = null;
                Proxy = null;
                Headers = null;
                TempHeaders = null;
                Cookies = null;
                Connection = null;
                NetworkStream = null;
                CommonStream = null;
            }
        }

        ~HttpClient()
        {
            Dispose();

            GC.SuppressFinalize(this);
        }
    }
}