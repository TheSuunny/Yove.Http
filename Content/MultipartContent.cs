using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yove.Http;

public class MultipartContent : HttpContent, IEnumerable<HttpContent>, IDisposable
{
    private readonly List<Element> _elements = [];

    private string _boundary { get; }

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
            if (_elements.Count != 0)
            {
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

            throw new ObjectDisposedException("Content disposed or empty.");
        }
    }

    public MultipartContent() : this($"----------------{HttpUtils.RandomString(16)}") { }

    public MultipartContent(string boundary)
    {
        if (string.IsNullOrEmpty(boundary))
            throw new NullReferenceException("Boundary is null or empty.");

        _boundary = boundary;
        ContentType = $"multipart/form-data; boundary={boundary}";
    }

    public void Add(string name, HttpContent content)
    {
        if (string.IsNullOrEmpty(name))
            throw new NullReferenceException("Name is null or empty.");

        if (content == null)
            throw new NullReferenceException("Content is null.");

        _elements.Add(new Element
        {
            Name = name,
            Content = content
        });
    }

    public void Add(string name, HttpContent content, string filename)
    {
        if (string.IsNullOrEmpty(name))
            throw new NullReferenceException("Name is null or empty.");

        if (string.IsNullOrEmpty(filename))
            throw new NullReferenceException("Filename is null or empty.");

        if (content == null)
            throw new NullReferenceException("Content is null.");

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
            throw new NullReferenceException("Name is null or empty.");

        if (string.IsNullOrEmpty(filename))
            throw new NullReferenceException("Filename is null or empty.");

        if (string.IsNullOrEmpty(contentType))
            throw new NullReferenceException("ContentType is null or empty.");

        if (content == null)
            throw new NullReferenceException("Content is null.");

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
            throw new NullReferenceException("Name is null or empty.");

        if (filename == null)
        {
            filename = Path.GetFileName(content.Path);

            if (string.IsNullOrEmpty(filename))
                throw new NullReferenceException("Path is null or empty.");
        }

        if (content == null)
            throw new NullReferenceException("Content is null.");

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
            throw new NullReferenceException("Name is null or empty.");

        if (filename == null)
        {
            if (string.IsNullOrEmpty(filename))
                throw new NullReferenceException("Path is null or empty.");
        }

        if (string.IsNullOrEmpty(contentType))
            throw new NullReferenceException("ContentType is null or empty.");

        if (content == null)
            throw new NullReferenceException("Content is null.");

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
        if (_elements.Count != 0)
        {
            if (commonStream == null)
                throw new NullReferenceException("CommonStream is null.");

            byte[] lineBytes = Encoding.ASCII.GetBytes("\r\n");
            byte[] boundaryBytes = Encoding.ASCII.GetBytes($"--{_boundary}\r\n");

            foreach (Element item in _elements)
            {
                commonStream.Write(boundaryBytes, 0, boundaryBytes.Length);

                string field;

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
        else
        {
            throw new ObjectDisposedException("Content disposed or empty.");
        }
    }

    ~MultipartContent()
    {
        Dispose();
    }

    public IEnumerator<HttpContent> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        throw new NotImplementedException();
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
}
