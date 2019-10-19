using System;
using System.IO;

namespace MDXRetroPort
{
    class SubStream : Stream
    {
        private readonly MemoryStream _stream;

        public SubStream() => _stream = new MemoryStream(0x400);

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _stream.Length;

        public override long Position { get => _stream.Position; set => _stream.Position = value; }

        public override void Flush() => _stream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

        public override void SetLength(long value) => _stream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

        public void WriteTo(Stream stream)
        {
            _stream.Position = 0;
            _stream.WriteTo(stream);
        }

        public void Write(int value) => _stream.Write(BitConverter.GetBytes(value));
    }
}
