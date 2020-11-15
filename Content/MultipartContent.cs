using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Collections;

namespace Yove.Http
{
    public class MultipartContent : HttpContent, IEnumerable<HttpContent>
    {
        private List<Element> _elements = new List<Element>();

        private string _boundary { get; set; }

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

        public override long ContentLength
        {
            get
            {
                if (_elements.Count == 0)
                    throw new ObjectDisposedException("Content disposed or empty.");

                long length = 0;

                foreach (Element item in _elements)
                {
                    length += item.Content.ContentLength;

                    if (item.IsFieldFile)
                    {
                        length += 72;
                        length += item.Name.Length;
                        length += item.Filename.Length;
                        length += item.Content.ContentType.Length;
                    }
                    else
                    {
                        length += 43;
                        length += item.Name.Length;
                    }

                    length += _boundary.Length + 6;
                }

                return length += _boundary.Length + 6;
            }
        }

        public MultipartContent() : this($"----------------{HttpUtils.RandomString(16)}") { }

        public MultipartContent(string boundary)
        {
            if (string.IsNullOrEmpty(boundary))
                throw new ArgumentNullException("Boundary is null or empty.");

            _boundary = boundary;
            ContentType = $"multipart/form-data; boundary={boundary}";
        }

        public void Add(string name, HttpContent content)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("Name is null or empty.");

            if (content == null)
                throw new ArgumentNullException("Content is null.");

            _elements.Add(new Element
            {
                Name = name,
                Content = content
            });
        }

        public void Add(string name, HttpContent content, string filename)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("Name is null or empty.");

            if (string.IsNullOrEmpty(filename))
                throw new ArgumentNullException("Filename is null or empty.");

            if (content == null)
                throw new ArgumentNullException("Content is null.");

            content.ContentType = "multipart/form-data";

            _elements.Add(new Element
            {
                Name = name,
                Filename = filename,
                Content = content
            });
        }

        public void Add(string name, string contentType, HttpContent content, string filename)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("Name is null or empty.");

            if (string.IsNullOrEmpty(filename))
                throw new ArgumentNullException("Filename is null or empty.");

            if (string.IsNullOrEmpty(contentType))
                throw new ArgumentNullException("ContentType is null or empty.");

            if (content == null)
                throw new ArgumentNullException("Content is null.");

            content.ContentType = contentType;

            _elements.Add(new Element
            {
                Name = name,
                Filename = filename,
                Content = content
            });
        }

        public void Add(string name, FileContent content, string filename = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("Name is null or empty.");

            if (filename == null)
            {
                filename = Path.GetFileName(content.Path);

                if (string.IsNullOrEmpty(filename))
                    throw new ArgumentNullException("Path is null or empty.");
            }

            if (content == null)
                throw new ArgumentNullException("Content is null.");

            content.ContentType = "multipart/form-data";

            _elements.Add(new Element
            {
                Name = name,
                Filename = filename,
                Content = content
            });
        }

        public void Add(string name, string contentType, FileContent content, string filename = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("Name is null or empty.");

            if (filename == null)
            {
                if (string.IsNullOrEmpty(filename))
                    throw new ArgumentNullException("Path is null or empty.");
            }

            if (string.IsNullOrEmpty(contentType))
                throw new ArgumentNullException("ContentType is null or empty.");

            if (content == null)
                throw new ArgumentNullException("Content is null.");

            content.ContentType = contentType;

            _elements.Add(new Element
            {
                Name = name,
                Filename = filename,
                Content = content
            });
        }

        public override void Write(Stream commonStream)
        {
            if (_elements.Count == 0)
                throw new ObjectDisposedException("Content disposed or empty.");

            if (commonStream == null)
                throw new ArgumentNullException("CommonStream is null.");

            byte[] lineBytes = Encoding.ASCII.GetBytes("\r\n");
            byte[] boundaryBytes = Encoding.ASCII.GetBytes($"--{_boundary}\r\n");

            foreach (Element item in _elements)
            {
                commonStream.Write(boundaryBytes, 0, boundaryBytes.Length);

                string field = string.Empty;

                if (item.IsFieldFile)
                    field = $"Content-Disposition: form-data; name=\"{item.Name}\"; filename=\"{item.Filename}\"\r\nContent-Type: {item.Content.ContentType}\r\n\r\n";
                else
                    field = $"Content-Disposition: form-data; name=\"{item.Name}\"\r\n\r\n";

                byte[] fieldBytes = Encoding.ASCII.GetBytes(field);

                commonStream.Write(fieldBytes, 0, fieldBytes.Length);
                item.Content.Write(commonStream);
                commonStream.Write(lineBytes, 0, lineBytes.Length);
            }

            boundaryBytes = Encoding.ASCII.GetBytes($"--{_boundary}--\r\n");

            commonStream.Write(boundaryBytes, 0, boundaryBytes.Length);
        }

        public override void Dispose()
        {
            if (_elements.Count > 0)
            {
                foreach (Element item in _elements)
                    item.Content.Dispose();

                _elements.Clear();
            }
        }

        ~MultipartContent()
        {
            Dispose();

            GC.SuppressFinalize(this);
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