using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

using Fody;

using Yove.Http.Exceptions;
using Yove.Http.Models;

namespace Yove.Http;

[ConfigureAwait(false)]
public class HttpResponse
{
    private HttpClient _request { get; }
    private Receiver _content { get; }

    public NameValueCollection Headers = [];
    public NameValueCollection Cookies = [];
    public List<RedirectItem> RedirectHistory = [];

    public string ContentType { get; }
    public string ContentEncoding { get; }
    public string ProtocolVersion { get; }
    public string Location { get; }

    public long? ContentLength { get; private set; }
    public long HeaderLength { get; }

    internal long ResponseLength { get; }

    public int KeepAliveTimeout { get; }
    public int KeepAliveMax { get; } = 100;

    public bool IsEmpytyBody { get; private set; }

    public HttpStatusCode StatusCode { get; }
    public HttpMethod Method { get; }
    public Encoding CharacterSet { get; } = Encoding.Default;
    public Uri Address { get; }
    public Content Content { get; }
    public TimeSpan TimeResponse { get; internal set; }

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
            return Headers["Connection"]?.Contains("close", StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public Uri RedirectAddress
    {
        get
        {
            return Location != null ? new UriBuilder(Location).Uri : null;
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
                throw new NullReferenceException("Key is null or empty.");

            return Headers[key];
        }
    }

    internal HttpResponse(HttpClient httpClient)
    {
        _request = httpClient;

        Method = httpClient.Method;
        Address = httpClient.Address;

        if (_request.RedirectHistory.Count > 0)
            RedirectHistory.AddRange(_request.RedirectHistory);

        _content = new Receiver(_request.Connection.ReceiveBufferSize, _request.CommonStream);

        string headerSource = _content.Get(false).Replace("\r", null);

        if (string.IsNullOrEmpty(headerSource))
        {
            IsEmpytyBody = true;
            return;
        }

        ProtocolVersion = HttpUtils.Parser("HTTP/", headerSource, " ")?.Trim();

        try
        {
            StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($"HTTP/{ProtocolVersion} ", headerSource, " ")?.Trim());
        }
        catch
        {
            StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($"HTTP/{ProtocolVersion} ", headerSource, "\n")?.Trim());
        }

        ContentType = HttpUtils.Parser("\ncontent-type: ", headerSource, "\n")?.Trim();
        ContentEncoding = HttpUtils.Parser("\ncontent-encoding: ", headerSource, "\n")?.Trim();
        Location = HttpUtils.Parser("\nLocation: ", headerSource, "\n")?.Trim();

        if (Location?.StartsWith("//") == true)
            Location = $"{Address.Scheme}://{Location.TrimStart('/')}";
        else if (Location?.StartsWith("/") == true)
            Location = $"{Address.Scheme}://{Address.Authority}/{Location.TrimStart('/')}";

        if (headerSource.Contains("content-length:", StringComparison.OrdinalIgnoreCase))
            ContentLength = Convert.ToInt64(HttpUtils.Parser("\ncontent-length: ", headerSource, "\n")?.Trim());

        if (headerSource.Contains("keep-alive", StringComparison.OrdinalIgnoreCase))
        {
            if (headerSource.Contains(", max=", StringComparison.OrdinalIgnoreCase))
            {
                KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("\nkeep-alive: timeout=", headerSource, ",")?.Trim()) * 1000;
                KeepAliveMax = Convert.ToInt32(HttpUtils.Parser($"\nkeep-alive: timeout={KeepAliveTimeout}, max=", headerSource, "\n")?.Trim());
            }
            else
            {
                KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("\nkeep-alive: timeout=", headerSource, "\n")?.Trim()) * 1000;
            }
        }

        if (ContentType != null)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            string charset = HttpUtils.Parser("\ncharset=", headerSource, "\n")?.Replace(@"\", "").Replace("\"", "");

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
            if (!header.Contains(':'))
                continue;

            string key = header.Split(':')[0]?.Trim();
            string value = header[(key.Length + 2)..]?.Trim();

            if (!string.IsNullOrEmpty(key))
            {
                if (key.Contains("set-cookie", StringComparison.OrdinalIgnoreCase))
                {
                    string cookie = value.TrimEnd(';').Split(';')[0];

                    if (!cookie.Contains('='))
                        continue;

                    string cookieName = cookie.Split('=')[0]?.Trim();
                    string cookieValue = cookie.Split('=')[1]?.Trim();

                    if (!string.IsNullOrEmpty(cookieName))
                    {
                        Cookies[cookieName] = cookieValue;

                        if (_request.EnableCookies && _request.Cookies != null)
                            _request.Cookies[cookieName] = cookieValue;
                    }
                }
                else
                {
                    Headers.Add(key, value);
                }
            }
        }

        HeaderLength = _content.Position;
        ResponseLength = _content.Position + (ContentLength ?? 0);

        Content = new Content(this);
    }

    internal async Task<MemoryStream> GetBodyContent()
    {
        _request.CancellationToken.ThrowIfCancellationRequested();

        if (ContentLength.HasValue && ContentLength > _request.MaxReciveBufferSize)
            throw new InternalBufferOverflowException($"Cannot write more bytes to the buffer than the configured maximum buffer size: {_request.MaxReciveBufferSize}");

        if (Content.Stream.Length > 0)
        {
            Content.Stream.Position = 0;

            return Content.Stream;
        }

        MemoryStream outputStream = null;

        if (Headers["Content-Encoding"] != null && Headers["Content-Encoding"] != "none")
        {
            if (Headers["Transfer-Encoding"] != null)
                outputStream = await ReceiveZipBody(true);

            if (outputStream == null && ContentLength.HasValue)
                outputStream = await ReceiveZipBody(false);

            if (outputStream == null)
            {
                using StreamWrapper streamWrapper = new(_request.CommonStream, _content);
                using Stream decopressionStream = GetZipStream(streamWrapper);

                outputStream = await ReceiveUnsizeBody(decopressionStream);
            }
        }

        if (outputStream == null && Headers["Transfer-Encoding"] != null)
            outputStream = await ReceiveStandartBody(true);

        if (outputStream == null && ContentLength.HasValue)
            outputStream = await ReceiveStandartBody(false);

        outputStream ??= await ReceiveUnsizeBody(_request.CommonStream);

        if (outputStream != null && Content.Stream.Length > 0)
            Content.Stream.Position = 0;

        if (!ContentLength.HasValue || ContentLength == 0 || Method == HttpMethod.HEAD ||
            StatusCode == HttpStatusCode.Continue || StatusCode == HttpStatusCode.NoContent ||
            StatusCode == HttpStatusCode.NotModified)
        {
            IsEmpytyBody = true;
        }

        return outputStream;
    }

    private Stream GetZipStream(Stream inputStream)
    {
        return Headers["Content-Encoding"].ToLower() switch
        {
            "gzip" => new GZipStream(inputStream, CompressionMode.Decompress, true),
            "br" => new BrotliStream(inputStream, CompressionMode.Decompress, true),
            "deflate" => new DeflateStream(inputStream, CompressionMode.Decompress, true),
            _ => throw new HttpResponseException("Unsupported compression format."),
        };
    }

    private async Task<MemoryStream> ReceiveZipBody(bool chunked)
    {
        using (StreamWrapper streamWrapper = new(_request.CommonStream, _content))
        {
            using Stream decopressionStream = GetZipStream(streamWrapper);

            byte[] buffer = new byte[_request.Connection.ReceiveBufferSize];

            if (chunked)
            {
                while (true)
                {
                    string getLine = _content.Get(true);

                    if (getLine == "\r\n")
                        continue;

                    getLine = getLine.Trim(' ', '\r', '\n');

                    if (getLine?.Length == 0)
                        break;

                    int blockLength = Convert.ToInt32(getLine, 16);

                    if (blockLength == 0)
                        break;

                    streamWrapper.TotalBytesRead = 0;
                    streamWrapper.LimitBytesRead = blockLength;

                    while (true)
                    {
                        int readBytes = decopressionStream.Read(buffer, 0, buffer.Length);

                        if (readBytes == 0)
                        {
                            if (streamWrapper.TotalBytesRead == blockLength)
                                break;

                            await WaitStream();
                            continue;
                        }

                        await Content.Stream.WriteAsync(buffer.AsMemory(0, readBytes));

                        if (Content.Stream.Length > _request.MaxReciveBufferSize)
                            throw new InternalBufferOverflowException($"Cannot write more bytes to the buffer than the configured maximum buffer size: {_request.MaxReciveBufferSize}");
                    }
                }

                if (ContentLength == null || ContentLength.Value == 0)
                    ContentLength = Content.Stream.Length;
            }
            else
            {
                while (true)
                {
                    int readBytes = await decopressionStream.ReadAsync(buffer);

                    if (readBytes == 0)
                    {
                        if (streamWrapper.TotalBytesRead == ContentLength)
                            break;

                        await WaitStream();
                        continue;
                    }

                    await Content.Stream.WriteAsync(buffer.AsMemory(0, readBytes));
                }
            }
        }

        return Content.Stream;
    }

    private async Task<MemoryStream> ReceiveStandartBody(bool chunked)
    {
        byte[] buffer = new byte[_request.Connection.ReceiveBufferSize];

        if (chunked)
        {
            while (true)
            {
                string getLine = _content.Get(true);

                if (getLine == "\r\n")
                    continue;

                getLine = getLine.Trim(' ', '\r', '\n');

                if (getLine?.Length == 0)
                    break;

                long totalBytesRead = 0;

                int blockLength = Convert.ToInt32(getLine, 16);

                if (blockLength == 0)
                    break;

                while (totalBytesRead != blockLength)
                {
                    long length = blockLength - totalBytesRead;

                    if (length > buffer.Length)
                        length = buffer.Length;

                    int readBytes;

                    if (_content.HasData)
                        readBytes = _content.Read(buffer, 0, (int)length);
                    else
                        readBytes = await _request.CommonStream.ReadAsync(buffer.AsMemory(0, (int)length));

                    if (readBytes == 0)
                    {
                        await WaitStream();
                        continue;
                    }

                    totalBytesRead += readBytes;

                    await Content.Stream.WriteAsync(buffer.AsMemory(0, readBytes));

                    if (Content.Stream.Length > _request.MaxReciveBufferSize)
                        throw new InternalBufferOverflowException($"Cannot write more bytes to the buffer than the configured maximum buffer size: {_request.MaxReciveBufferSize}");
                }
            }

            if (ContentLength == null || ContentLength.Value == 0)
                ContentLength = Content.Stream.Length;
        }
        else
        {
            long totalBytesRead = 0;

            while (totalBytesRead != ContentLength)
            {
                int readBytes;

                if (_content.HasData)
                    readBytes = _content.Read(buffer, 0, buffer.Length);
                else
                    readBytes = await _request.CommonStream.ReadAsync(buffer);

                if (readBytes == 0)
                {
                    await WaitStream();
                    continue;
                }

                await Content.Stream.WriteAsync(buffer.AsMemory(0, readBytes));

                totalBytesRead += readBytes;
            }
        }

        return Content.Stream;
    }

    private async Task<MemoryStream> ReceiveUnsizeBody(Stream inputStream)
    {
        int beginBytesRead = 0;

        byte[] buffer = new byte[_request.Connection.ReceiveBufferSize];

        if (inputStream is GZipStream || inputStream is DeflateStream || inputStream is BrotliStream)
        {
            beginBytesRead = await inputStream.ReadAsync(buffer);
        }
        else
        {
            if (_content.HasData)
                beginBytesRead = _content.Read(buffer, 0, buffer.Length);

            if (beginBytesRead < buffer.Length)
                beginBytesRead += await inputStream.ReadAsync(buffer.AsMemory(beginBytesRead, buffer.Length - beginBytesRead));
        }

        await Content.Stream.WriteAsync(buffer.AsMemory(0, beginBytesRead));

        string html = Encoding.ASCII.GetString(buffer);

        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase) && html.Contains("</html>", StringComparison.OrdinalIgnoreCase))
            return Content.Stream;

        while (true)
        {
            int readBytes = await inputStream.ReadAsync(buffer);

            if (html.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                if (readBytes == 0)
                {
                    await WaitStream();
                    continue;
                }

                html = Encoding.ASCII.GetString(buffer);

                if (html.Contains("</html>", StringComparison.OrdinalIgnoreCase))
                {
                    await Content.Stream.WriteAsync(buffer.AsMemory(0, beginBytesRead));
                    break;
                }
            }
            else if (readBytes == 0)
            {
                break;
            }

            await Content.Stream.WriteAsync(buffer.AsMemory(0, readBytes));

            if (Content.Stream.Length > _request.MaxReciveBufferSize)
                throw new InternalBufferOverflowException($"Cannot write more bytes to the buffer than the configured maximum buffer size: {_request.MaxReciveBufferSize}");
        }

        if (ContentLength == null || ContentLength.Value == 0)
            ContentLength = Content.Stream.Length;

        return Content.Stream;
    }

    private async Task WaitStream()
    {
        int currentSleep = 0;
        int delay = (_request.Connection.ReceiveTimeout < 10) ? 10 : _request.Connection.ReceiveTimeout;

        while (!_request.NetworkStream.DataAvailable)
        {
            if (currentSleep < delay)
            {
                currentSleep += 10;

                await Task.Delay(10);
                continue;
            }

            throw new HttpResponseException($"Timeout waiting for data from Address: {_request.Address.AbsoluteUri}");
        }
    }

    public async Task<string> ToFile(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            throw new NullReferenceException("Filename is null or empty.");

        return await ToFile(AppDomain.CurrentDomain.BaseDirectory, filename);
    }

    public async Task<string> ToFile(string localPath, string filename = null)
    {
        if (IsEmpytyBody)
            throw new NullReferenceException("Content not found.");

        if (string.IsNullOrEmpty(localPath))
            throw new NullReferenceException("Path is null or empty.");

        if (filename == null)
        {
            if (Headers["Content-Disposition"] != null)
            {
                filename = $"{localPath.TrimEnd('/')}/{HttpUtils.Parser("filename=\"", Headers["Content-Disposition"], "\"")}";
            }
            else
            {
                filename = Path.GetFileName(new Uri(Address.AbsoluteUri).LocalPath);

                if (string.IsNullOrEmpty(filename))
                    throw new NullReferenceException("Could not find filename.");
            }
        }

        string outputPath = $"{localPath.TrimEnd('/')}/{filename}";

        await File.WriteAllBytesAsync(outputPath, await Content.ReadAsBytes());

        return outputPath;
    }
}
