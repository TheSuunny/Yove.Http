using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Yove.Http
{
    public class Receiver
    {
        private byte[] Buffer { get; set; }
        private byte[] TemporaryBuffer = new byte[1500];

        private Stream Stream { get; set; }

        private int Length = 0;
        public int Position = 0;

        public bool HasData
        {
            get { return (Length - Position) != 0; }
        }

        public Receiver(int Size, Stream Stream)
        {
            this.Buffer = new byte[Size];
            this.Stream = Stream;
        }

        public async Task<string> GetAsync(bool ReadLine)
        {
            int CurrentPosition = 0;

            while (true)
            {
                if (Position == Length)
                {
                    Position = 0;
                    Length = await Stream.ReadAsync(Buffer, 0, Buffer.Length).ConfigureAwait(false);

                    if (Length == 0)
                        break;
                }

                byte Symbol = Buffer[Position++];

                TemporaryBuffer[CurrentPosition++] = Symbol;

                if (!ReadLine)
                {
                    if (Encoding.ASCII.GetString(TemporaryBuffer, 0, CurrentPosition).EndsWith("\r\n\r\n"))
                        break;
                }
                else
                {
                    if (Symbol == (byte)'\n')
                        break;
                }

                if (CurrentPosition == TemporaryBuffer.Length)
                {
                    byte[] TemporaryBufferX2 = new byte[TemporaryBuffer.Length * 2];

                    TemporaryBuffer.CopyTo(TemporaryBufferX2, 0);
                    TemporaryBuffer = TemporaryBufferX2;
                }
            }

            return Encoding.ASCII.GetString(TemporaryBuffer, 0, CurrentPosition);
        }

        public int Read(byte[] Buffer, int Index, int Length)
        {
            int CurrentLength = this.Length - Position;

            if (CurrentLength > Length)
                CurrentLength = Length;

            Array.Copy(this.Buffer, Position, Buffer, Index, CurrentLength);

            Position += CurrentLength;

            return CurrentLength;
        }
    }
}