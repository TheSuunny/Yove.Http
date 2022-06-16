using System;
using System.Text;

namespace Yove.Http
{
    public class StringContent : ByteContent
    {
        public StringContent(string content) : this(content, Encoding.UTF8) { }

        public StringContent(string content, Encoding encoding)
        {
            if (content == null || encoding == null)
                throw new NullReferenceException("Content or Encoding is null.");

            Content = encoding.GetBytes(content);
            Offset = 0;
            Count = Content.Length;
        }
    }
}
