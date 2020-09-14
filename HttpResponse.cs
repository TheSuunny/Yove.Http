using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Fody;

namespace Yove.Http
{
    [ConfigureAwait(false)]
    public class HttpResponse
    {
        private HttpClient Request { get; set; }
        private Receiver Content { get; set; }

        public NameValueCollection Headers = new NameValueCollection();
        public NameValueCollection Cookies { get; set; }

        public string ContentType { get; private set; }
        public string ContentEncoding { get; private set; }
        public string ProtocolVersion { get; private set; }
        public string Location { get; private set; }

        public long? ContentLength { get; private set; }
        internal long ResponseLength { get; private set; }

        public int KeepAliveTimeout { get; private set; }
        public int KeepAliveMax { get; private set; } = 100;

        public bool NoContent { get; private set; }

        public HttpStatusCode StatusCode { get; private set; }
        public HttpMethod Method { get; private set; }
        public Encoding CharacterSet { get; private set; }
        public Uri Address { get; private set; }

        private string SourceBody { get; set; }

        public string Body
        {
            get
            {
                if (NoContent || SourceBody != null)
                    return SourceBody;

                using (MemoryStream Stream = new MemoryStream())
                {
                    foreach (BytesWraper Bytes in GetResponseBody())
                        Stream.Write(Bytes.Value, 0, Bytes.Length);

                    SourceBody = CharacterSet.GetString(Stream.GetBuffer(), 0, (int)Stream.Length);

                    return SourceBody;
                }
            }
        }

        public JToken Json
        {
            get
            {
                if (NoContent)
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

        internal HttpResponse(HttpClient Client)
        {
            Request = Client;
            Method = Client.Method;
            Address = Client.Address;

            if (Request.EnableCookies && Request.Cookies != null)
                Cookies = Request.Cookies;

            Content = new Receiver(Request.Connection.ReceiveBufferSize, Request.CommonStream);

            string HeaderSource = Content.Get(false).Replace("\r", null);

            if (string.IsNullOrEmpty(HeaderSource))
            {
                NoContent = true;
                return;
            }

            ProtocolVersion = HttpUtils.Parser("HTTP/", HeaderSource, " ")?.Trim();
            StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($"HTTP/{ProtocolVersion} ", HeaderSource, " ")?.Trim());
            ContentType = HttpUtils.Parser("Content-Type: ", HeaderSource, "\n")?.Trim();
            ContentEncoding = HttpUtils.Parser("Content-Encoding: ", HeaderSource, "\n")?.Trim();
            Location = HttpUtils.Parser("Location: ", HeaderSource, "\n")?.Trim();

            if (Location != null && Location.StartsWith("/"))
                Location = $"{Address.Scheme}://{Address.Authority}/{Location.TrimStart('/')}";

            if (HeaderSource.Contains("Content-Length:"))
                ContentLength = Convert.ToInt64(HttpUtils.Parser("Content-Length: ", HeaderSource, "\n")?.Trim());

            if (HeaderSource.Contains("Keep-Alive"))
            {
                if (HeaderSource.Contains(", max="))
                {
                    KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("Keep-Alive: timeout=", HeaderSource, ",")?.Trim()) * 1000;
                    KeepAliveMax = Convert.ToInt32(HttpUtils.Parser($"Keep-Alive: timeout={KeepAliveTimeout}, max=", HeaderSource, "\n")?.Trim());
                }
                else
                {
                    KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("Keep-Alive: timeout=", HeaderSource, "\n")?.Trim()) * 1000;
                }
            }

            if (ContentType != null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                string Charset = HttpUtils.Parser("charset=", HeaderSource, "\n")?.Replace(@"\", "").Replace("\"", "");

                if (Charset != null)
                {
                    try
                    {
                        CharacterSet = Encoding.GetEncoding(Charset);
                    }
                    catch
                    {
                        CharacterSet = Encoding.UTF8;
                    }
                }
                else
                {
                    CharacterSet = Request.CharacterSet ?? Encoding.Default;
                }
            }
            else
            {
                CharacterSet = Request.CharacterSet ?? Encoding.Default;
            }

            foreach (string Header in HeaderSource.Split('\n'))
            {
                if (!Header.Contains(":"))
                    continue;

                string Key = Header.Split(':')[0]?.Trim();
                string Value = Header.Substring(Key.Count() + 2)?.Trim();

                if (!string.IsNullOrEmpty(Key))
                {
                    if (Key.Contains("Set-Cookie"))
                    {
                        string Cookie = Value.TrimEnd(';').Split(';')[0];

                        if (!Cookie.Contains("="))
                            continue;

                        string CookieName = Cookie.Split('=')[0]?.Trim();
                        string CookieValue = Cookie.Split('=')[1]?.Trim();

                        if (!string.IsNullOrEmpty(CookieName))
                            Cookies[CookieName] = CookieValue;
                    }
                    else
                    {
                        Headers.Add(Key, Value);
                    }
                }
            }

            if (ContentLength == 0 || Method == HttpMethod.HEAD || StatusCode == HttpStatusCode.Continue ||
                StatusCode == HttpStatusCode.NoContent || StatusCode == HttpStatusCode.NotModified)
            {
                NoContent = true;
            }

            ResponseLength = Content.Position + ((ContentLength.HasValue) ? ContentLength.Value : 0);
        }

        private IEnumerable<BytesWraper> GetResponseBody()
        {
            if (Headers["Content-Encoding"] != null)
            {
                if (Headers["Transfer-Encoding"] != null)
                    return ReceiveZipBody(true);

                if (ContentLength.HasValue)
                    return ReceiveZipBody(false);

                using (StreamWrapper StreamWrapper = new StreamWrapper(Request.CommonStream, Content))
                {
                    using (Stream Stream = GetZipStream(StreamWrapper))
                        return ReceiveUnsizeBody(Stream);
                }
            }

            if (Headers["Transfer-Encoding"] != null)
                return ReceiveStandartBody(true);

            if (ContentLength.HasValue)
                return ReceiveStandartBody(false);

            return ReceiveUnsizeBody(Request.CommonStream);
        }

        private Stream GetZipStream(Stream Stream)
        {
            switch (Headers["Content-Encoding"].ToLower())
            {
                case "gzip":
                    return new GZipStream(Stream, CompressionMode.Decompress, true);
                case "deflate":
                    return new DeflateStream(Stream, CompressionMode.Decompress, true);
                default:
                    throw new Exception("Unsupported compression format.");
            }
        }

        private IEnumerable<BytesWraper> ReceiveZipBody(bool Chunked)
        {
            BytesWraper BytesWraper = new BytesWraper();

            using (StreamWrapper StreamWrapper = new StreamWrapper(Request.CommonStream, Content))
            {
                using (Stream Stream = GetZipStream(StreamWrapper))
                {
                    byte[] Buffer = new byte[Request.Connection.ReceiveBufferSize];

                    BytesWraper.Value = Buffer;

                    while (StreamWrapper.TotalBytesRead != ContentLength)
                    {
                        if (!Chunked)
                        {
                            int Bytes = Stream.ReadAsync(Buffer, 0, Buffer.Length).GetAwaiter().GetResult();

                            if (Bytes == 0)
                            {
                                if (StreamWrapper.TotalBytesRead != ContentLength)
                                {
                                    WaitStream().Wait();
                                    continue;
                                }

                                yield break;
                            }

                            BytesWraper.Length = Bytes;

                            yield return BytesWraper;
                        }
                        else
                        {
                            string GetLine = Content.Get(true);

                            if (GetLine == "\r\n")
                                continue;

                            GetLine = GetLine.Trim(' ', '\r', '\n');

                            if (GetLine == string.Empty)
                                yield break;

                            int BlockLength = Convert.ToInt32(GetLine, 16);

                            if (BlockLength == 0)
                                yield break;

                            StreamWrapper.TotalBytesRead = 0;
                            StreamWrapper.LimitBytesRead = BlockLength;

                            while (true)
                            {
                                int Bytes = Stream.ReadAsync(Buffer, 0, Buffer.Length).GetAwaiter().GetResult();

                                if (Bytes == 0)
                                {
                                    if (StreamWrapper.TotalBytesRead != BlockLength)
                                    {
                                        WaitStream().Wait();
                                        continue;
                                    }

                                    break;
                                }

                                BytesWraper.Length = Bytes;

                                yield return BytesWraper;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<BytesWraper> ReceiveStandartBody(bool Chunked)
        {
            BytesWraper BytesWraper = new BytesWraper();

            int BufferSize = Request.Connection.ReceiveBufferSize;

            byte[] Buffer = new byte[BufferSize];

            BytesWraper.Value = Buffer;

            if (!Chunked)
            {
                long TotalBytesRead = 0;

                while (TotalBytesRead != ContentLength)
                {
                    int BytesRead = 0;

                    if (Content.HasData)
                        BytesRead = Content.Read(Buffer, 0, BufferSize);
                    else
                        BytesRead = Request.CommonStream.Read(Buffer, 0, BufferSize);

                    if (BytesRead != 0)
                    {
                        TotalBytesRead += BytesRead;
                        BytesWraper.Length = BytesRead;

                        yield return BytesWraper;
                    }
                    else
                    {
                        WaitStream().Wait();
                    }
                }
            }
            else
            {
                while (true)
                {
                    string GetLine = Content.Get(true);

                    if (GetLine == "\r\n")
                        continue;

                    GetLine = GetLine.Trim(' ', '\r', '\n');

                    if (GetLine == string.Empty)
                        yield break;

                    int BlockLength = 0;
                    long TotalBytesRead = 0;

                    BlockLength = Convert.ToInt32(GetLine, 16);

                    if (BlockLength == 0)
                        yield break;

                    while (TotalBytesRead != BlockLength)
                    {
                        long Length = BlockLength - TotalBytesRead;

                        if (Length > BufferSize)
                            Length = BufferSize;

                        int BytesRead = 0;

                        if (Content.HasData)
                            BytesRead = Content.Read(Buffer, 0, (int)Length);
                        else
                            BytesRead = Request.CommonStream.Read(Buffer, 0, (int)Length);

                        if (BytesRead != 0)
                        {
                            TotalBytesRead += BytesRead;
                            BytesWraper.Length = BytesRead;

                            yield return BytesWraper;
                        }
                        else
                        {
                            WaitStream().Wait();
                        }
                    }
                }
            }
        }

        private IEnumerable<BytesWraper> ReceiveUnsizeBody(Stream Stream)
        {
            BytesWraper BytesWraper = new BytesWraper();

            int BufferSize = Request.Connection.ReceiveBufferSize;

            byte[] Buffer = new byte[BufferSize];

            BytesWraper.Value = Buffer;

            int BeginBytesRead = 0;

            if (Stream is GZipStream || Stream is DeflateStream)
            {
                BeginBytesRead = Stream.ReadAsync(Buffer, 0, BufferSize).GetAwaiter().GetResult();
            }
            else
            {
                if (Content.HasData)
                    BeginBytesRead = Content.Read(Buffer, 0, BufferSize);

                if (BeginBytesRead < BufferSize)
                    BeginBytesRead += Stream.ReadAsync(Buffer, BeginBytesRead, BufferSize - BeginBytesRead).GetAwaiter().GetResult();
            }

            BytesWraper.Length = BeginBytesRead;

            yield return BytesWraper;

            string Html = Encoding.ASCII.GetString(Buffer);

            if (Html.Contains("<html") && Html.Contains("</html>"))
                yield break;

            while (true)
            {
                int Bytes = Stream.ReadAsync(Buffer, 0, BufferSize).GetAwaiter().GetResult();

                if (Html.Contains("<html"))
                {
                    if (Bytes == 0)
                    {
                        WaitStream().Wait();
                        continue;
                    }

                    Html = Encoding.ASCII.GetString(Buffer);

                    if (Html.Contains("</html>"))
                    {
                        BytesWraper.Length = Bytes;

                        yield return BytesWraper;
                        yield break;
                    }
                }
                else if (Bytes == 0)
                {
                    yield break;
                }

                BytesWraper.Length = Bytes;

                yield return BytesWraper;
            }
        }

        private async Task WaitStream()
        {
            int Sleep = 0;
            int Delay = (Request.Connection.ReceiveTimeout < 10) ? 10 : Request.Connection.ReceiveTimeout;

            while (!Request.NetworkStream.DataAvailable)
            {
                if (Sleep < Delay)
                {
                    Sleep += 10;
                    await Task.Delay(10);

                    continue;
                }

                throw new Exception($"Timeout waiting for data - {Request.Address.AbsoluteUri}");
            }
        }

        public string Parser(string Start, string End)
        {
            if (string.IsNullOrEmpty(Start) || string.IsNullOrEmpty(End))
                throw new ArgumentNullException("Start or End is null or empty.");

            return HttpUtils.Parser(Start, Body, End);
        }

        public async Task<string> ToFile(string LocalPath, string Filename = null)
        {
            if (NoContent)
                throw new NullReferenceException("Content not found.");

            if (string.IsNullOrEmpty(LocalPath))
                throw new ArgumentNullException("Path is null or empty.");

            string FullPath = string.Empty;

            if (Filename == null)
            {
                if (Headers["Content-Disposition"] != null)
                {
                    FullPath = $"{LocalPath.TrimEnd('/')}/{HttpUtils.Parser("filename=\"", Headers["Content-Disposition"], "\"")}";
                }
                else
                {
                    Filename = Path.GetFileName(new Uri(Address.AbsoluteUri).LocalPath);

                    if (string.IsNullOrEmpty(Filename))
                        throw new ArgumentNullException("Could not find filename.");
                }
            }

            FullPath = $"{LocalPath.TrimEnd('/')}/{Filename}";

            using (FileStream Stream = new FileStream(FullPath, FileMode.Append))
            {
                foreach (BytesWraper Bytes in GetResponseBody())
                    await Stream.WriteAsync(Bytes.Value, 0, Bytes.Length);
            }

            return FullPath;
        }

        public async Task<byte[]> ToBytes()
        {
            if (NoContent)
                throw new NullReferenceException("Content not found.");

            using (MemoryStream Stream = new MemoryStream())
            {
                foreach (BytesWraper Bytes in GetResponseBody())
                    await Stream.WriteAsync(Bytes.Value, 0, Bytes.Length);

                return Stream.ToArray();
            }
        }

        public async Task<MemoryStream> ToMemoryStream()
        {
            if (NoContent)
                throw new NullReferenceException("Content not found.");

            MemoryStream Stream = new MemoryStream();

            foreach (BytesWraper Bytes in GetResponseBody())
                await Stream.WriteAsync(Bytes.Value, 0, Bytes.Length);

            Stream.Position = 0;

            return Stream;
        }

        private sealed class BytesWraper
        {
            public int Length { get; set; }

            public byte[] Value { get; set; }
        }
    }
}