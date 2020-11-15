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
                throw new ArgumentNullException("Content or Encoding is null.");

            this.Content = encoding.GetBytes(content);
            this.Offset = 0;
            this.Count = this.Content.Length;
        }
    }
}