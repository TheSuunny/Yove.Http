using System.IO;
using System;

namespace Yove.Http
{
    public class FileContent : StreamContent
    {
        internal string Path { get; set; }

        public FileContent(string path, int bufferSize = 32768)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException("Path is null or empty.");

            this.Content = new FileStream(path, FileMode.Open, FileAccess.Read);
            this.BufferSize = bufferSize;
            this.Path = path;
        }

        public FileContent(Stream stream, int bufferSize = 32768)
        {
            if (stream == null)
                throw new ArgumentNullException("Stream is null.");

            this.Content = stream;
            this.BufferSize = bufferSize;
        }
    }
}