using System;
using System.Text;

namespace Yove.Http
{
    public class StringContent : ByteContent
    {
        public StringContent(string Content) : this(Content, Encoding.UTF8) { }

        public StringContent(string Content, Encoding Encoding)
        {
            if (Content == null || Encoding == null)
                throw new ArgumentNullException("Content or Encoding is null.");

            this.Content = Encoding.GetBytes(Content);
            this.Offset = 0;
            this.Count = this.Content.Length;
        }
    }
}