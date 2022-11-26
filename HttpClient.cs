using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Fody;

using Newtonsoft.Json.Linq;

using Yove.Http.Events;
using Yove.Http.Exceptions;
using Yove.Http.Models;
using Yove.Http.Proxy;

namespace Yove.Http;

[ConfigureAwait(false)]
public class HttpClient : IDisposable
{
    #region Public
    public NameValueCollection Headers = new();
    public NameValueCollection TempHeaders = new();

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

    public bool KeepAlive { get; set; }
    public bool EnableEncodingContent { get; set; } = true;
    public bool EnableAutoRedirect { get; set; } = true;
    public bool RedirectOnlyIfOtherDomain { get; set; }
    public bool EnableProtocolError { get; set; } = true;
    public bool EnableCookies { get; set; } = true;
    public bool EnableReconnect { get; set; } = true;
    public bool HasConnection { get; set; }
    public bool IsDisposed { get; set; }

    public int KeepAliveTimeOut { get; set; } = 60000;
    public int KeepAliveMaxRequest { get; set; } = 100;
    public int RedirectLimit { get; set; } = 3;
    public int ReconnectLimit { get; set; } = 3;
    public int ReconnectDelay { get; set; } = 1000;
    public int TimeOut { get; set; } = 60000;
    public int ReadWriteTimeOut { get; set; } = 60000;
    public int MaxReciveBufferSize { get; set; } = 2147483647;

    public Uri Address { get; private set; }

    public CancellationToken CancellationToken { get; set; }

    public SslProtocols DefaultSslProtocols { get; set; } = SslProtocols.None;

    public EventHandler<UploadEvent> UploadProgressChanged { get; set; }
    public EventHandler<DownloadEvent> DownloadProgressChanged { get; set; }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
                throw new NullReferenceException("Key is null or empty.");

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
            return _proxyClient;
        }
        set
        {
            if (_proxyClient != null && HasConnection)
                Close();

            _proxyClient = value;
        }
    }

    #endregion

    #region Private & Internal

    internal TcpClient Connection { get; set; }
    internal NetworkStream NetworkStream { get; set; }
    internal Stream CommonStream { get; set; }
    internal HttpMethod Method { get; set; }
    internal HttpContent Content { get; set; }

    internal List<RedirectItem> RedirectHistory = new();

    internal RemoteCertificateValidationCallback AcceptAllCertificationsCallback = new(AcceptAllCertifications);

    private HttpResponse _response { get; set; }
    private ProxyClient _proxyClient { get; set; }

    private int _reconnectCount { get; set; }
    private int _keepAliveRequestCount { get; set; }
    private int _redirectCount { get; set; }

    private DateTime _whenConnectionIdle { get; set; }

    private long _sentBytes { get; set; }
    private long _receivedBytes { get; set; }

    private bool _isReceivedHeader { get; set; }
    private long _headerLength { get; set; }

    private bool _canReconnect
    {
        get
        {
            return EnableReconnect && _reconnectCount < ReconnectLimit;
        }
    }

    #endregion

    public HttpClient()
    {
        if (EnableCookies && Cookies == null)
            Cookies = new NameValueCollection();
    }

    public HttpClient(CancellationToken token) : this()
    {
        if (token == default)
            throw new NullReferenceException("Token cannot be null");

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
        if (token == default)
            throw new NullReferenceException("Token cannot be null");

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
        HttpResponse response = await Raw(HttpMethod.GET, url);

        return await response.Content.ReadAsString();
    }

    public async Task<JToken> GetJson(string url)
    {
        HttpResponse response = await Raw(HttpMethod.GET, url);

        return await response.Content.ReadAsJson();
    }

    public async Task<byte[]> GetBytes(string url)
    {
        HttpResponse response = await Raw(HttpMethod.GET, url);

        return await response.Content.ReadAsBytes();
    }

    public async Task<MemoryStream> GetStream(string url)
    {
        HttpResponse response = await Raw(HttpMethod.GET, url);

        return await response.Content.ReadAsStream();
    }

    public async Task<string> GetToFile(string url, string path, string filename = null)
    {
        HttpResponse response = await Raw(HttpMethod.GET, url);

        return await response.ToFile(path, filename);
    }

    public async Task<HttpResponse> Raw(HttpMethod method, string url, HttpContent body = null)
    {
        if (IsDisposed)
            throw new ObjectDisposedException("Object disposed.");

        if (string.IsNullOrEmpty(url))
            throw new NullReferenceException("URL is null or empty.");

        CancellationTokenRegistration ctr = default;

        if (CancellationToken != default)
        {
            CancellationToken.ThrowIfCancellationRequested();

            ctr = CancellationToken.Register(() =>
            {
                _reconnectCount = ReconnectLimit;

                Close();
            });
        }

        try
        {
            if (!url.StartsWith("https://") && !url.StartsWith("http://") && !string.IsNullOrEmpty(BaseUrl))
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
                        SslStream sslStream = new(NetworkStream, false, AcceptAllCertificationsCallback);

                        await sslStream.AuthenticateAsClientAsync(Address.Host, null, DefaultSslProtocols, false);

                        CommonStream = sslStream;
                    }
                    else
                    {
                        CommonStream = NetworkStream;
                    }

                    HasConnection = true;

                    if (DownloadProgressChanged != null || UploadProgressChanged != null)
                    {
                        EventStreamWrapper eventStream = new(CommonStream, Connection.SendBufferSize);

                        if (UploadProgressChanged != null)
                        {
                            eventStream.WriteBytesCallback = (e) =>
                            {
                                _sentBytes += e;

                                UploadProgressChanged?.Invoke(this, new UploadEvent(_sentBytes - _headerLength, Content.ContentLength));
                            };
                        }

                        if (DownloadProgressChanged != null)
                        {
                            eventStream.ReadBytesCallback = (e) =>
                            {
                                _receivedBytes += e;

                                if (_isReceivedHeader)
                                    DownloadProgressChanged?.Invoke(this, new DownloadEvent(_receivedBytes - _response.HeaderLength, _response.ContentLength));
                            };
                        }

                        CommonStream = eventStream;
                    }
                }
                catch (Exception ex)
                {
                    if (ex?.InnerException?.Message == "bad protocol version")
                        DefaultSslProtocols = SslProtocols.Tls11;

                    if (_canReconnect)
                        return await Reconnect(method, url, body);

                    throw new HttpRequestException($"Failed Connection to Address: {Address.AbsoluteUri}", ex);
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

                string stringHeader = $"{Method} {Address.PathAndQuery} HTTP/1.1\r\n{GenerateHeaders(Method, contentLength, contentType)}";
                byte[] headersBytes = Encoding.ASCII.GetBytes(stringHeader);

                _headerLength = headersBytes.Length;

                CommonStream.Write(headersBytes, 0, headersBytes.Length);

                if (Content != null && contentLength != 0)
                    Content.Write(CommonStream);
            }
            catch (Exception ex)
            {
                if (_canReconnect)
                    return await Reconnect(method, url, body);

                throw new HttpRequestException($"Failed send data to Address: {Address.AbsoluteUri}", ex);
            }

            try
            {
                _isReceivedHeader = false;

                _response = new HttpResponse(this);

                _isReceivedHeader = true;
            }
            catch (Exception ex)
            {
                if (_canReconnect)
                    return await Reconnect(method, url, body);

                throw new HttpResponseException($"Failed receive data from Address: {Address.AbsoluteUri}", ex);
            }

            _reconnectCount = 0;
            _whenConnectionIdle = DateTime.Now;

            _response.TimeResponse = (DateTime.Now - timeResponseStart).TimeOfDay;

            if (EnableProtocolError)
            {
                if ((int)_response.StatusCode >= 400 && (int)_response.StatusCode < 500)
                    throw new HttpResponseException($"[Client] | Status Code - {_response.StatusCode}");
                else if ((int)_response.StatusCode >= 500)
                    throw new HttpResponseException($"[Server] | Status Code - {_response.StatusCode}");
            }

            if (EnableAutoRedirect && _response.Location != null && _redirectCount < RedirectLimit &&
                ((_response.RedirectAddress.Host == Address.Host && _response.RedirectAddress.Scheme != Address.Scheme) ||
                !RedirectOnlyIfOtherDomain || (RedirectOnlyIfOtherDomain && _response.RedirectAddress.Host != Address.Host)))
            {
                _redirectCount++;

                string location = _response.Location;

                RedirectHistory.Add(new RedirectItem
                {
                    From = url,
                    To = location,
                    StatusCode = (int)_response.StatusCode,
                    Length = _response.ContentLength,
                    ContentType = _response.ContentType
                });

                Close();

                return await Raw(HttpMethod.GET, location, null);
            }

            _redirectCount = 0;
            RedirectHistory.Clear();
        }
        finally
        {
            ctr.Dispose();
        }

        return _response;
    }

    private bool CheckKeepAlive()
    {
        int maxRequest = (_response != null && _response.KeepAliveMax != 0) ? _response.KeepAliveMax : KeepAliveMaxRequest;

        return _keepAliveRequestCount == 0 || _keepAliveRequestCount == maxRequest ||
            _response?.ConnectionClose == true || !HasConnection ||
            _whenConnectionIdle.AddMilliseconds(TimeOut) < DateTime.Now;
    }

    private async Task<TcpClient> CreateConnection(string host, int port)
    {
        if (Proxy == null)
        {
            TcpClient tcpClient = new()
            {
                ReceiveTimeout = ReadWriteTimeOut,
                SendTimeout = ReadWriteTimeOut
            };

            using CancellationTokenSource cancellationToken = new(TimeSpan.FromMilliseconds(TimeOut));

            try
            {
#if NETSTANDARD2_1 || NETCOREAPP3_1
                tcpClient.ConnectAsync(host, port).Wait(cancellationToken.Token);
#elif NET5_0_OR_GREATER
                await tcpClient.ConnectAsync(host, port, cancellationToken.Token);
#endif

                if (!tcpClient.Connected)
                    throw new();
            }
            catch
            {
                tcpClient.Dispose();

                throw new HttpRequestException($"Failed Connection to Address: {Address.AbsoluteUri}");
            }

            return tcpClient;
        }
        else
        {
            return await Proxy.CreateConnection(host, port);
        }
    }

    private string GenerateHeaders(HttpMethod method, long contentLength = 0, string contentType = null)
    {
        NameValueCollection rawHeaders = new();

        if (Address.IsDefaultPort)
            rawHeaders["Host"] = Address.Host;
        else
            rawHeaders["Host"] = $"{Address.Host}:{Address.Port}";

        if (!string.IsNullOrEmpty(UserAgent))
            rawHeaders["User-Agent"] = UserAgent;

        if (!Headers.AllKeys.Contains("Accept"))
            rawHeaders["Accept"] = Accept;

        if (!Headers.AllKeys.Contains("Accept-Language"))
            rawHeaders["Accept-Language"] = Language;

        if (EnableEncodingContent)
            rawHeaders["Accept-Encoding"] = "deflate, gzip, br";

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

        if (Proxy?.Type == ProxyType.Http)
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
        else if (KeepAlive)
        {
            rawHeaders["Connection"] = "keep-alive";

            _keepAliveRequestCount++;
        }
        else
        {
            rawHeaders["Connection"] = "close";
        }

        if (Cookies?.Count > 0)
        {
            string cookieBuilder = string.Empty;

            foreach (string cookie in Cookies)
                cookieBuilder += $"{cookie}={Cookies[cookie]}; ";

            rawHeaders["Cookie"] = cookieBuilder.TrimEnd();
        }

        StringBuilder headerBuilder = new();

        foreach (string header in rawHeaders)
            headerBuilder.AppendFormat($"{header}: {rawHeaders[header]}\r\n");

        foreach (string header in Headers)
            headerBuilder.AppendFormat($"{header}: {Headers[header]}\r\n");

        foreach (string header in TempHeaders)
            headerBuilder.AppendFormat($"{header}: {TempHeaders[header]}\r\n");

        TempHeaders.Clear();

        return $"{headerBuilder}\r\n";
    }

    public void AddTempHeader(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            throw new NullReferenceException("Key is null or empty.");

        if (string.IsNullOrEmpty(value))
            throw new NullReferenceException("Value is null or empty.");

        TempHeaders.Add(key, value);
    }

    public void AddRawCookie(string source)
    {
        if (string.IsNullOrEmpty(source))
            throw new NullReferenceException("Value is null or empty.");

        if (!EnableCookies)
            throw new HttpRequestException("Cookies is disabled.");

        if (source.Contains("Cookie:", StringComparison.OrdinalIgnoreCase))
            source = source.Replace("Cookie:", "", StringComparison.OrdinalIgnoreCase).Trim();

        foreach (string cookie in source.Split(';'))
        {
            string key = cookie.Split('=')[0]?.Trim();
            string value = cookie.Split('=')[1]?.Trim();

            if (key != null && value != null)
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

    public void Close()
    {
        if (!HasConnection)
            return;

        Connection?.Close();
        Connection?.Dispose();

        NetworkStream?.Close();
        NetworkStream?.Dispose();

        CommonStream?.Close();
        CommonStream?.Dispose();

        _keepAliveRequestCount = 0;

        HasConnection = false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
            return;

        IsDisposed = true;

        Close();

        Content?.Dispose();

        _response?.Content?.Dispose();

        Connection = null;
        NetworkStream = null;
        CommonStream = null;
    }

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }

    ~HttpClient()
    {
        Dispose();
    }
}
