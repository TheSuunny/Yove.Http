using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace Yove.Http
{
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

        public int ContentLength { get; private set; } = -1;
        public int KeepAliveTimeout { get; private set; }
        public int KeepAliveMax { get; private set; }

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

                MemoryStream Stream = new MemoryStream((ContentLength == -1) ? 0 : ContentLength);

                foreach (var Bytes in GetResponseBody())
                    Stream.Write(Bytes.Value, 0, Bytes.Length);

                SourceBody = CharacterSet.GetString(Stream.GetBuffer(), 0, (int)Stream.Length);

                return SourceBody;
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

            string HeaderSource = Content.GetAsync(false).ConfigureAwait(false).GetAwaiter().GetResult().Replace("\r", null);

            if (string.IsNullOrEmpty(HeaderSource))
            {
                NoContent = true;
                return;
            }

            ProtocolVersion = HttpUtils.Parser("HTTP/", HeaderSource, " ")?.Trim();
            StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), HttpUtils.Parser($"HTTP/{ProtocolVersion} ", HeaderSource, " ")?.Trim());
            ContentType = HttpUtils.Parser("Content-Type: ", HeaderSource, "\n")?.Trim();
            ContentEncoding = HttpUtils.Parser("Content-Encoding: ", HeaderSource, "\n")?.Trim();
            Location = HttpUtils.Parser("location: ", HeaderSource.ToLower(), "\n")?.Trim();

            if (Location != null && !Location.StartsWith("/"))
                Location = $"{Address.Scheme}://{Address.Authority}/{Location.TrimStart('/')}";

            if (HeaderSource.Contains("Content-Length"))
                ContentLength = Convert.ToInt32(HttpUtils.Parser("Content-Length: ", HeaderSource, "\n")?.Trim());

            if (HeaderSource.Contains("Keep-Alive"))
            {
                KeepAliveTimeout = Convert.ToInt32(HttpUtils.Parser("Keep-Alive: timeout=", HeaderSource, ",")?.Trim()) * 1000;
                KeepAliveMax = Convert.ToInt32(HttpUtils.Parser($"Keep-Alive: timeout={KeepAliveTimeout}, max=", HeaderSource, "\n")?.Trim());
            }

            if (ContentType != null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                string Charset = HttpUtils.Parser("charset=", HeaderSource, "\n");

                if (Charset != null)
                    CharacterSet = Encoding.GetEncoding(Charset);
                else
                    CharacterSet = Request.CharacterSet ?? Encoding.Default;
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
        }

        private IEnumerable<BytesWraper> GetResponseBody()
        {
            if (Headers["Content-Encoding"] != null)
            {
                if (Headers["Transfer-Encoding"] != null)
                    return ReceiveZipBody(true);

                if (ContentLength != -1)
                    return ReceiveZipBody(false);

                return ReceiveUnsizeBody(GetZipStream(new StreamWrapper(Request.CommonStream, Content)));
            }

            if (Headers["Transfer-Encoding"] != null)
                return ReceiveStandartBody(true);

            if (ContentLength != -1)
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
            StreamWrapper StreamWrapper = new StreamWrapper(Request.CommonStream, Content);

            using (Stream Stream = GetZipStream(StreamWrapper))
            {
                int BufferSize = Request.Connection.ReceiveBufferSize;

                byte[] Buffer = new byte[BufferSize];

                BytesWraper.Value = Buffer;

                while (true)
                {
                    if (!Chunked)
                    {
                        int Bytes = Stream.Read(Buffer, 0, BufferSize);

                        if (Bytes == 0)
                        {
                            if (StreamWrapper.TotalBytesRead != ContentLength)
                            {
                                WaitStream().ConfigureAwait(false).GetAwaiter().GetResult();
                                continue;
                            }

                            yield break;
                        }

                        BytesWraper.Length = Bytes;

                        yield return BytesWraper;
                    }
                    else
                    {
                        string GetLine = Content.GetAsync(true).ConfigureAwait(false).GetAwaiter().GetResult();

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
                            int Bytes = Stream.Read(Buffer, 0, BufferSize);

                            if (Bytes == 0)
                            {
                                if (StreamWrapper.TotalBytesRead != BlockLength)
                                {
                                    WaitStream().ConfigureAwait(false).GetAwaiter().GetResult();
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

        private IEnumerable<BytesWraper> ReceiveStandartBody(bool Chunked)
        {
            BytesWraper BytesWraper = new BytesWraper();

            int BufferSize = Request.Connection.ReceiveBufferSize;

            byte[] Buffer = new byte[BufferSize];

            BytesWraper.Value = Buffer;

            if (!Chunked)
            {
                int TotalBytesRead = 0;

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
                        WaitStream().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                }
            }
            else
            {
                while (true)
                {
                    string GetLine = Content.GetAsync(true).ConfigureAwait(false).GetAwaiter().GetResult();

                    if (GetLine == "\r\n")
                        continue;

                    GetLine = GetLine.Trim(' ', '\r', '\n');

                    if (GetLine == string.Empty)
                        yield break;

                    int BlockLength = 0;
                    int TotalBytesRead = 0;

                    BlockLength = Convert.ToInt32(GetLine, 16);

                    if (BlockLength == 0)
                        yield break;

                    while (TotalBytesRead != BlockLength)
                    {
                        int Length = BlockLength - TotalBytesRead;

                        if (Length > BufferSize)
                            Length = BufferSize;

                        int BytesRead = 0;

                        if (Content.HasData)
                            BytesRead = Content.Read(Buffer, 0, Length);
                        else
                            BytesRead = Request.CommonStream.Read(Buffer, 0, Length);

                        if (BytesRead != 0)
                        {
                            TotalBytesRead += BytesRead;
                            BytesWraper.Length = BytesRead;

                            yield return BytesWraper;
                        }
                        else
                        {
                            WaitStream().ConfigureAwait(false).GetAwaiter().GetResult();
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
                BeginBytesRead = Stream.Read(Buffer, 0, BufferSize);
            }
            else
            {
                if (Content.HasData)
                    BeginBytesRead = Content.Read(Buffer, 0, BufferSize);

                if (BeginBytesRead < BufferSize)
                    BeginBytesRead += Stream.Read(Buffer, BeginBytesRead, BufferSize - BeginBytesRead);
            }

            BytesWraper.Length = BeginBytesRead;

            yield return BytesWraper;

            string Html = Encoding.ASCII.GetString(Buffer);

            if (Html.Contains("<html") && Html.Contains("</html>"))
                yield break;

            while (true)
            {
                int Bytes = Stream.Read(Buffer, 0, BufferSize);

                if (Html.Contains("<html"))
                {
                    if (Bytes == 0)
                    {
                        WaitStream().ConfigureAwait(false).GetAwaiter().GetResult();
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
                    yield break;

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
                    await Task.Delay(10).ConfigureAwait(false);

                    continue;
                }

                throw new Exception($"Timeout waiting for data - {Request.Address.AbsoluteUri}");
            }
        }

        public string Parser(string Start, string End)
        {
            if (string.IsNullOrEmpty(Start) || string.IsNullOrEmpty(End))
                throw new ArgumentNullException("Start or End is null or empty");

            return HttpUtils.Parser(Start, Body, End);
        }

        public async Task<string> ToFile(string LocalPath, string Filename = null)
        {
            if (NoContent)
                throw new NullReferenceException("Content not found.");

            if (string.IsNullOrEmpty(LocalPath))
                throw new ArgumentNullException("Path is null or empty");

            string FullPath = string.Empty;

            if (Filename == null)
            {
                if (Headers["Content-Disposition"] != null)
                    FullPath = $"{LocalPath.TrimEnd('/')}/{HttpUtils.Parser("filename=\"", Headers["Content-Disposition"], "\"")}";
                else
                {
                    Filename = Path.GetFileName(new Uri(Address.AbsoluteUri).LocalPath);

                    if (string.IsNullOrEmpty(Filename))
                        throw new ArgumentNullException("Could not find filename");
                }
            }

            FullPath = $"{LocalPath.TrimEnd('/')}/{Filename}";

            FileStream File = new FileStream(FullPath, FileMode.Create);

            foreach (var Bytes in GetResponseBody())
                await File.WriteAsync(Bytes.Value, 0, Bytes.Length).ConfigureAwait(false);

            return FullPath;
        }

        public async Task<byte[]> ToBytes()
        {
            if (NoContent)
                throw new NullReferenceException("Content not found.");

            MemoryStream Stream = new MemoryStream((ContentLength == -1) ? 0 : ContentLength);

            foreach (var Bytes in GetResponseBody())
                await Stream.WriteAsync(Bytes.Value, 0, Bytes.Length).ConfigureAwait(false);

            return Stream.ToArray();
        }

        public async Task<MemoryStream> ToMemoryStream()
        {
            if (NoContent)
                throw new NullReferenceException("Content not found.");

            MemoryStream Stream = new MemoryStream((ContentLength == -1) ? 0 : ContentLength);

            foreach (var Bytes in GetResponseBody())
                await Stream.WriteAsync(Bytes.Value, 0, Bytes.Length).ConfigureAwait(false);

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