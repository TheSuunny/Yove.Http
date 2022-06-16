using System;
using System.IO;

namespace Yove.Http
{
    public class FileContent : StreamContent
    {
        internal string Path { get; set; }

        public FileContent(string path, int bufferSize = 32768)
        {
            if (string.IsNullOrEmpty(path))
                throw new NullReferenceException("Path is null or empty.");

            Content = new FileStream(path, FileMode.Open, FileAccess.Read);
            BufferSize = bufferSize;
            Path = path;
        }

        public FileContent(Stream stream, int bufferSize = 32768)
        {
            Content = stream ?? throw new NullReferenceException("Stream is null.");
            BufferSize = bufferSize;
        }
    }
}
