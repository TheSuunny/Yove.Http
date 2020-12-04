using System;
using System.Linq;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Fody;
using BrotliSharpLib;

namespace Yove.Http
{
    [ConfigureAwait(false)]
    public class HttpResponse
    {
        private HttpClient _request { get; set; }
        private Receiver _content { get; set; }

        public NameValueCollection Headers = new NameValueCollection();
        public NameValueCollection Cookies = new NameValueCollection();

        public string ContentType { get; private set; }
        public string ContentEncoding { get; private set; }
        public string ProtocolVersion { get; private set; }
        public string Location { get; private set; }

        public long? ContentLength { get; private set; }
        internal long ResponseLength { get; private set; }

        public int KeepAliveTimeout { get; private set; }
        public int KeepAliveMax { get; private set; } = 100;

        public bool IsEmpytyBody { get; private set; }

        public HttpStatusCode StatusCode { get; private set; }
        public HttpMethod Method { get; private set; }
        public Encoding CharacterSet { get; private set; }
        public Uri Address { get; private set; }

        public TimeSpan TimeResponse { get; internal set; }

        public string Body { get; private set; }

        public JToken Json
        {
            get
            {
                if (IsEmpytyBody)
                    throw new NullReferenceException("Content not found.");

                return JToken.Parse(Body);
            }
        }

        public bool IsOK
        {
            get
            {
                return StatusCode == HttpStatusCode.OK;
            }
        }

        public bool HasRedirect
        {
            get
            {
                if ((int)StatusCode >= 300 && (int)StatusCode < 400)
                    return true;

                return Location != null;
            }
        }

        public bool ConnectionClose
        {
            get
            {
                if (Headers["Connection"] != null && Headers["Connection"].Contains("close"))
                    return true;

                return false;
            }
        }

        public Uri RedirectAddress
        {
            get
            {
                if (Location != null)
                    return new UriBuilder(Location).Uri;

                return null;
            }
        }

        public string this[string Key]
        {
            get
            {
                if (string.IsNullOrEmpty(Key))
                    throw new ArgumentNullException("Key is null or empty.");

                return Headers[Key];
            }
        }

        internal HttpResponse(HttpClient httpClient)
        {
            _request = httpClient;

            Method = httpClient.Method;
            Address = httpClient.Address;

            _content = new Receiver(_request.Connection.ReceiveBufferSize, _request.CommonStream);

            string headerSource = _content.Get(false).Replace("\r", null);

            if (string.IsNullOrEmpty(headerSource))
            {
                IsEmpytyBody = true;
                return;
            }

            ProtocolVersion = HttpUtils.Parser("HTTP/", headerSource, " ")?.Trim();
            StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($"HTTP/{ProtocolVersion} ", headerSource, " ")?.Trim());
            ContentType = HttpUtils.Parser("Content-Type: ", headerSource, "\n")?.Trim();
            ContentEncoding = HttpUtils.Parser("Content-Encoding: ", headerSource, "\n")?.Trim();
            Location = HttpUtils.Parser("Location: ", headerSource, "\n")?.Trim();

            if (Location != null && Location.StartsWith("/"))
                Location = $"{Address.Scheme}://{Address.Authority}/{Location.TrimStart('/')}";

            if (headerSource.Contains("Content-Length:"))
                ContentLength = Convert.ToInt64(HttpUtils.Parser("Content-Length: ", headerSource, "\n")?.Trim());

            if (headerSource.Contains("Keep-Alive"))
            {
                if (headerSource.Contains(", max="))
                {
                    KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("Keep-Alive: timeout=", headerSource, ",")?.Trim()) * 1000;
                    KeepAliveMax = Convert.ToInt32(HttpUtils.Parser($"Keep-Alive: timeout={KeepAliveTimeout}, max=", headerSource, "\n")?.Trim());
                }
                else
                {
                    KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("Keep-Alive: timeout=", headerSource, "\n")?.Trim()) * 1000;
                }
            }

            if (ContentType != null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                string charset = HttpUtils.Parser("charset=", headerSource, "\n")?.Replace(@"\", "").Replace("\"", "");

                if (charset != null)
                {
                    try
                    {
                        CharacterSet = Encoding.GetEncoding(charset);
                    }
                    catch
                    {
                        CharacterSet = Encoding.UTF8;
                    }
                }
                else
                {
                    CharacterSet = _request.CharacterSet ?? Encoding.Default;
                }
            }
            else
            {
                CharacterSet = _request.CharacterSet ?? Encoding.Default;
            }

            foreach (string header in headerSource.Split('\n'))
            {
                if (!header.Contains(":"))
                    continue;

                string key = header.Split(':')[0]?.Trim();
                string value = header.Substring(key.Count() + 2)?.Trim();

                if (!string.IsNullOrEmpty(key))
                {
                    if (key.Contains("Set-Cookie"))
                    {
                        string cookie = value.TrimEnd(';').Split(';')[0];

                        if (!cookie.Contains("="))
                            continue;

                        string cookieName = cookie.Split('=')[0]?.Trim();
                        string cookieValue = cookie.Split('=')[1]?.Trim();

                        if (!string.IsNullOrEmpty(cookieName))
                            Cookies[cookieName] = cookieValue;
                    }
                    else
                    {
                        Headers.Add(key, value);
                    }
                }
            }

            if (ContentLength == 0 || Method == HttpMethod.HEAD || StatusCode == HttpStatusCode.Continue ||
                StatusCode == HttpStatusCode.NoContent || StatusCode == HttpStatusCode.NotModified)
            {
                IsEmpytyBody = true;
            }

            ResponseLength = _content.Position + ((ContentLength.HasValue) ? ContentLength.Value : 0);
        }

        internal async Task<MemoryStream> LoadBody()
        {
            MemoryStream stream = null;

            if (Headers["Content-Encoding"] != null)
            {
                if (Headers["Transfer-Encoding"] != null)
                    stream = await ReceiveZipBody(true);

                if (stream == null && ContentLength.HasValue)
                    stream = await ReceiveZipBody(false);

                if (stream == null)
                {
                    using (StreamWrapper streamWrapper = new StreamWrapper(_request.CommonStream, _content))
                    {
                        using (Stream gZipStream = GetZipStream(streamWrapper))
                            stream = await ReceiveUnsizeBody(gZipStream);
                    }
                }
            }

            if (stream == null && Headers["Transfer-Encoding"] != null)
                stream = await ReceiveStandartBody(true);

            if (stream == null && ContentLength.HasValue)
                stream = await ReceiveStandartBody(false);

            if (stream == null)
                stream = await ReceiveUnsizeBody(_request.CommonStream);

            if (stream != null)
            {
                stream.Position = 0;

                if (Body == null)
                    Body = CharacterSet.GetString(stream.ToArray(), 0, (int)stream.Length);
            }

            return stream;
        }

        private Stream GetZipStream(Stream inputStream)
        {
            switch (Headers["Content-Encoding"].ToLower())
            {
                case "gzip":
                    return new GZipStream(inputStream, CompressionMode.Decompress, true);
                case "br":
                    return new BrotliStream(inputStream, CompressionMode.Decompress, true);
                case "deflate":
                    return new DeflateStream(inputStream, CompressionMode.Decompress, true);
                default:
                    throw new Exception("Unsupported compression format.");
            }
        }

        private async Task<MemoryStream> ReceiveZipBody(bool chunked)
        {
            MemoryStream outputStream = new MemoryStream();

            using (StreamWrapper streamWrapper = new StreamWrapper(_request.CommonStream, _content))
            {
                using (Stream gZipStream = GetZipStream(streamWrapper))
                {
                    byte[] buffer = new byte[_request.Connection.ReceiveBufferSize];

                    while (streamWrapper.TotalBytesRead != ContentLength)
                    {
                        if (!chunked)
                        {
                            int readBytes = await gZipStream.ReadAsync(buffer, 0, buffer.Length);

                            if (readBytes == 0)
                            {
                                if (streamWrapper.TotalBytesRead != ContentLength)
                                {
                                    await WaitStream();
                                    continue;
                                }

                                break;
                            }

                            await outputStream.WriteAsync(buffer, 0, readBytes);
                        }
                        else
                        {
                            string getLine = _content.Get(true);

                            if (getLine == "\r\n")
                                continue;

                            getLine = getLine.Trim(' ', '\r', '\n');

                            if (getLine == string.Empty)
                                break;

                            int blockLength = Convert.ToInt32(getLine, 16);

                            if (blockLength == 0)
                                break;

                            streamWrapper.TotalBytesRead = 0;
                            streamWrapper.LimitBytesRead = blockLength;

                            while (true)
                            {
                                int readBytes = gZipStream.Read(buffer, 0, buffer.Length);

                                if (readBytes == 0)
                                {
                                    if (streamWrapper.TotalBytesRead != blockLength)
                                    {
                                        await WaitStream();
                                        continue;
                                    }

                                    break;
                                }

                                await outputStream.WriteAsync(buffer, 0, readBytes);
                            }
                        }
                    }
                }
            }

            return outputStream;
        }

        private async Task<MemoryStream> ReceiveStandartBody(bool chunked)
        {
            MemoryStream outputStream = new MemoryStream();

            byte[] buffer = new byte[_request.Connection.ReceiveBufferSize];

            if (!chunked)
            {
                long totalBytesRead = 0;

                while (totalBytesRead != ContentLength)
                {
                    int readBytes = 0;

                    if (_content.HasData)
                        readBytes = _content.Read(buffer, 0, buffer.Length);
                    else
                        readBytes = await _request.CommonStream.ReadAsync(buffer, 0, buffer.Length);

                    if (readBytes != 0)
                    {
                        totalBytesRead += readBytes;

                        await outputStream.WriteAsync(buffer, 0, readBytes);
                    }
                    else
                    {
                        await WaitStream();
                    }
                }
            }
            else
            {
                while (true)
                {
                    string getLine = _content.Get(true);

                    if (getLine == "\r\n")
                        continue;

                    getLine = getLine.Trim(' ', '\r', '\n');

                    if (getLine == string.Empty)
                        break;

                    int blockLength = 0;
                    long totalBytesRead = 0;

                    blockLength = Convert.ToInt32(getLine, 16);

                    if (blockLength == 0)
                        break;

                    while (totalBytesRead != blockLength)
                    {
                        long length = blockLength - totalBytesRead;

                        if (length > buffer.Length)
                            length = buffer.Length;

                        int readBytes = 0;

                        if (_content.HasData)
                            readBytes = _content.Read(buffer, 0, (int)length);
                        else
                            readBytes = await _request.CommonStream.ReadAsync(buffer, 0, (int)length);

                        if (readBytes != 0)
                        {
                            totalBytesRead += readBytes;

                            await outputStream.WriteAsync(buffer, 0, readBytes);
                        }
                        else
                        {
                            await WaitStream();
                        }
                    }
                }
            }

            return outputStream;
        }

        private async Task<MemoryStream> ReceiveUnsizeBody(Stream inputStream)
        {
            MemoryStream outputStream = new MemoryStream();

            int beginBytesRead = 0;

            byte[] buffer = new byte[_request.Connection.ReceiveBufferSize];

            if (inputStream is GZipStream || inputStream is DeflateStream || inputStream is BrotliStream)
            {
                beginBytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length);
            }
            else
            {
                if (_content.HasData)
                    beginBytesRead = _content.Read(buffer, 0, buffer.Length);

                if (beginBytesRead < buffer.Length)
                    beginBytesRead += await inputStream.ReadAsync(buffer, beginBytesRead, buffer.Length - beginBytesRead);
            }

            await outputStream.WriteAsync(buffer, 0, beginBytesRead);

            string html = Encoding.ASCII.GetString(buffer);

            if (html.Contains("<html") && html.Contains("</html>"))
                return outputStream;

            while (true)
            {
                int readBytes = await inputStream.ReadAsync(buffer, 0, buffer.Length);

                if (html.Contains("<html"))
                {
                    if (readBytes == 0)
                    {
                        await WaitStream();
                        continue;
                    }

                    html = Encoding.ASCII.GetString(buffer);

                    if (html.Contains("</html>"))
                    {
                        await outputStream.WriteAsync(buffer, 0, beginBytesRead);
                        break;
                    }
                }
                else if (readBytes == 0)
                {
                    break;
                }

                await outputStream.WriteAsync(buffer, 0, beginBytesRead);
            }

            return outputStream;
        }

        private async Task WaitStream()
        {
            int sleep = 0;
            int delay = (_request.Connection.ReceiveTimeout < 10) ? 10 : _request.Connection.ReceiveTimeout;

            while (!_request.NetworkStream.DataAvailable)
            {
                if (sleep < delay)
                {
                    sleep += 10;

                    await Task.Delay(10);

                    continue;
                }

                throw new Exception($"Timeout waiting for data - {_request.Address.AbsoluteUri}");
            }
        }

        public string Parser(string start, string end)
        {
            if (string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end))
                throw new ArgumentNullException("Start or End is null or empty.");

            return HttpUtils.Parser(start, Body, end);
        }

        public async Task<string> ToFile(string localPath, string filename = null)
        {
            if (IsEmpytyBody)
                throw new NullReferenceException("Content not found.");

            if (string.IsNullOrEmpty(localPath))
                throw new ArgumentNullException("Path is null or empty.");

            string fullPath = string.Empty;

            if (filename == null)
            {
                if (Headers["Content-Disposition"] != null)
                {
                    fullPath = $"{localPath.TrimEnd('/')}/{HttpUtils.Parser("filename=\"", Headers["Content-Disposition"], "\"")}";
                }
                else
                {
                    filename = Path.GetFileName(new Uri(Address.AbsoluteUri).LocalPath);

                    if (string.IsNullOrEmpty(filename))
                        throw new ArgumentNullException("Could not find filename.");
                }
            }

            fullPath = $"{localPath.TrimEnd('/')}/{filename}";

            using (FileStream fileStream = new FileStream(fullPath, FileMode.Append))
            {
                using (MemoryStream bodyStream = await LoadBody())
                    await bodyStream.CopyToAsync(fileStream);
            }

            return fullPath;
        }

        public async Task<byte[]> ToBytes()
        {
            if (IsEmpytyBody)
                throw new NullReferenceException("Content not found.");

            using (MemoryStream bodyStream = await LoadBody())
                return bodyStream.ToArray();
        }

        public async Task<MemoryStream> ToMemoryStream()
        {
            if (IsEmpytyBody)
                throw new NullReferenceException("Content not found.");

            return await LoadBody();
        }
    }
}