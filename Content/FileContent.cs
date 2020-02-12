using System.IO;
using System;

namespace Yove.Http
{
    public class FileContent : StreamContent
    {
        internal string Path { get; set; }

        public FileContent(string Path, int BufferSize = 32768)
        {
            if (string.IsNullOrEmpty(Path))
                throw new ArgumentNullException("Path is null or empty.");

            this.Content = new FileStream(Path, FileMode.Open, FileAccess.Read);
            this.BufferSize = BufferSize;
            this.Path = Path;
        }

        public FileContent(Stream Stream, int BufferSize = 32768)
        {
            if (Stream == null)
                throw new ArgumentNullException("Stream is null.");

            this.Content = Stream;
            this.BufferSize = BufferSize;
        }
    }
}