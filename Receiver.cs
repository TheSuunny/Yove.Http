using System;
using System.IO;
using System.Text;

namespace Yove.Http;

internal class Receiver
{
    private byte[] _buffer { get; }
    private byte[] _temporaryBuffer = new byte[1024];
    private Stream _stream { get; }
    private int _length { get; set; }
    public int Position { get; set; }

    public bool HasData
    {
        get { return (_length - Position) != 0; }
    }

    public Receiver(int size, Stream stream)
    {
        _buffer = new byte[size];
        _stream = stream;
    }

    public string Get(bool readLine)
    {
        int currentPosition = 0;

        while (true)
        {
            if (Position == _length)
            {
                Position = 0;
                _length = _stream.Read(_buffer, 0, _buffer.Length);

                if (_length == 0)
                    break;
            }

            byte symbol = _buffer[Position++];

            _temporaryBuffer[currentPosition++] = symbol;

            if ((!readLine && Encoding.ASCII.GetString(_temporaryBuffer, 0, currentPosition).EndsWith("\r\n\r\n")) ||
                (readLine && symbol == (byte)'\n'))
            {
                break;
            }

            if (currentPosition == _temporaryBuffer.Length)
            {
                byte[] temporaryBufferX2 = new byte[_temporaryBuffer.Length * 2];

                _temporaryBuffer.CopyTo(temporaryBufferX2, 0);
                _temporaryBuffer = temporaryBufferX2;
            }
        }

        return Encoding.ASCII.GetString(_temporaryBuffer, 0, currentPosition);
    }

    public int Read(byte[] buffer, int index, int length)
    {
        int currentLength = _length - Position;

        if (currentLength > length)
            currentLength = length;

        Array.Copy(_buffer, Position, buffer, index, currentLength);

        Position += currentLength;

        return currentLength;
    }
}
