using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

namespace Yove.Http
{
    public class MultipartContent : HttpContent, IEnumerable<HttpContent>
    {
        private sealed class Element
        {
            public string Name { get; set; }
            public string Filename { get; set; }

            public HttpContent Content { get; set; }

            public bool IsFieldFile
            {
                get
                {
                    return Filename != null;
                }
            }
        }

        private string Boundary { get; set; }

        private List<Element> Elements = new List<Element>();

        public override long ContentLength
        {
            get
            {
                if (Elements.Count == 0)
                    throw new ObjectDisposedException("Content disposed or empty");

                long Length = 0;

                foreach (Element Item in Elements)
                {
                    Length += Item.Content.ContentLength;

                    if (Item.IsFieldFile)
                    {
                        Length += 72;
                        Length += Item.Name.Length;
                        Length += Item.Filename.Length;
                        Length += Item.Content.ContentType.Length;
                    }
                    else
                    {
                        Length += 43;
                        Length += Item.Name.Length;
                    }

                    Length += Boundary.Length + 6;
                }

                return Length += Boundary.Length + 6;
            }
        }

        public MultipartContent() : this($"----------------{HttpUtils.RandomString(16)}") { }

        public MultipartContent(string Boundary)
        {
            if (string.IsNullOrEmpty(Boundary))
                throw new ArgumentNullException("Boundary is null or empty");

            this.Boundary = Boundary;
            this.ContentType = $"multipart/form-data; boundary={Boundary}";
        }

        public void Add(string Name, HttpContent Content)
        {
            if (string.IsNullOrEmpty(Name))
                throw new ArgumentNullException("Name is null or empty");

            if (Content == null)
                throw new ArgumentNullException("Content is null");

            Elements.Add(new Element
            {
                Name = Name,
                Content = Content
            });
        }

        public void Add(string Name, HttpContent Content, string Filename)
        {
            if (string.IsNullOrEmpty(Name))
                throw new ArgumentNullException("Name is null or empty");

            if (string.IsNullOrEmpty(Filename))
                throw new ArgumentNullException("Filename is null or empty");

            if (Content == null)
                throw new ArgumentNullException("Content is null");

            Content.ContentType = "multipart/form-data";

            Elements.Add(new Element
            {
                Name = Name,
                Filename = Filename,
                Content = Content
            });
        }

        public void Add(string Name, string ContentType, HttpContent Content, string Filename)
        {
            if (string.IsNullOrEmpty(Name))
                throw new ArgumentNullException("Name is null or empty");

            if (string.IsNullOrEmpty(Filename))
                throw new ArgumentNullException("Filename is null or empty");

            if (string.IsNullOrEmpty(ContentType))
                throw new ArgumentNullException("ContentType is null or empty");

            if (Content == null)
                throw new ArgumentNullException("Content is null");

            Content.ContentType = ContentType;

            Elements.Add(new Element
            {
                Name = Name,
                Filename = Filename,
                Content = Content
            });
        }

        public void Add(string Name, FileContent Content, string Filename = null)
        {
            if (string.IsNullOrEmpty(Name))
                throw new ArgumentNullException("Name is null or empty");

            if (Filename == null)
            {
                if (Content.Path.Split('/').Last().Contains("."))
                    Filename = Content.Path.Split('/').Last();
                else
                    throw new ArgumentNullException("Path is null or empty");
            }

            if (Content == null)
                throw new ArgumentNullException("Content is null");

            Content.ContentType = "multipart/form-data";

            Elements.Add(new Element
            {
                Name = Name,
                Filename = Filename,
                Content = Content
            });
        }

        public void Add(string Name, string ContentType, FileContent Content, string Filename = null)
        {
            if (string.IsNullOrEmpty(Name))
                throw new ArgumentNullException("Name is null or empty");

            if (Filename == null)
            {
                if (Content.Path.Split('/').Last().Contains("."))
                    Filename = Content.Path.Split('/').Last();
                else
                    throw new ArgumentNullException("Path is null or empty");
            }

            if (string.IsNullOrEmpty(ContentType))
                throw new ArgumentNullException("ContentType is null or empty");

            if (Content == null)
                throw new ArgumentNullException("Content is null");

            Content.ContentType = ContentType;

            Elements.Add(new Element
            {
                Name = Name,
                Filename = Filename,
                Content = Content
            });
        }

        public override async Task WriteAsync(Stream CommonStream)
        {
            if (Elements.Count == 0)
                throw new ObjectDisposedException("Content disposed or empty");

            if (CommonStream == null)
                throw new ArgumentNullException("CommonStream is null");

            byte[] LineBytes = Encoding.ASCII.GetBytes("\r\n");
            byte[] BoundaryBytes = Encoding.ASCII.GetBytes($"--{Boundary}\r\n");

            foreach (Element Item in Elements)
            {
                await CommonStream.WriteAsync(BoundaryBytes, 0, BoundaryBytes.Length).ConfigureAwait(false);

                string Field = string.Empty;

                if (Item.IsFieldFile)
                    Field = $"Content-Disposition: form-data; name=\"{Item.Name}\"; filename=\"{Item.Filename}\"\r\nContent-Type: {Item.Content.ContentType}\r\n\r\n";
                else
                    Field = $"Content-Disposition: form-data; name=\"{Item.Name}\"\r\n\r\n";

                byte[] FieldBytes = Encoding.ASCII.GetBytes(Field);

                await CommonStream.WriteAsync(FieldBytes, 0, FieldBytes.Length).ConfigureAwait(false);
                await Item.Content.WriteAsync(CommonStream).ConfigureAwait(false);
                await CommonStream.WriteAsync(LineBytes, 0, LineBytes.Length).ConfigureAwait(false);
            }

            BoundaryBytes = Encoding.ASCII.GetBytes($"--{Boundary}--\r\n");

            await CommonStream.WriteAsync(BoundaryBytes, 0, BoundaryBytes.Length).ConfigureAwait(false);
        }

        public override void Dispose()
        {
            if (Elements.Count > 0)
            {
                foreach (Element Item in Elements)
                    Item.Content.Dispose();

                Elements.Clear();
            }
        }

        public IEnumerator<HttpContent> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}