#if NETFRAMEWORK
using System;
using System.IO;

namespace System.IO.Compression
{
    /// <summary>Minimal RFC1950 zlib facade over .NET Framework raw DeflateStream.</summary>
    public sealed class ZLibStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly DeflateStream _deflate;
        private readonly CompressionMode _mode;
        private readonly bool _leaveOpen;
        private bool _wroteHeader, _disposed;
        private uint _a = 1, _b;

        public ZLibStream(Stream stream, CompressionMode mode) : this(stream, mode, false) { }
        public ZLibStream(Stream stream, CompressionMode mode, bool leaveOpen)
        {
            _baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _mode = mode; _leaveOpen = leaveOpen;
            if (mode == CompressionMode.Decompress)
            {
                var cmf = stream.ReadByte(); var flg = stream.ReadByte();
                if (cmf < 0 || flg < 0 || (cmf & 15) != 8 || ((cmf << 8) + flg) % 31 != 0)
                    throw new InvalidDataException("Invalid zlib header.");
                _deflate = new DeflateStream(stream, CompressionMode.Decompress, true);
            }
            else _deflate = new DeflateStream(stream, CompressionMode.Compress, true);
        }
        public ZLibStream(Stream stream, CompressionLevel level) : this(stream, level, false) { }
        public ZLibStream(Stream stream, CompressionLevel level, bool leaveOpen)
        {
            _baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _mode = CompressionMode.Compress; _leaveOpen = leaveOpen;
            _deflate = new DeflateStream(stream, level, true);
        }
        public override bool CanRead => _mode == CompressionMode.Decompress;
        public override bool CanWrite => _mode == CompressionMode.Compress;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _deflate.Flush();
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override int Read(byte[] b, int o, int c) => _deflate.Read(b, o, c);
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_wroteHeader) { _baseStream.WriteByte(0x78); _baseStream.WriteByte(0x9c); _wroteHeader = true; }
            for (var i = offset; i < offset + count; i++) { _a = (_a + buffer[i]) % 65521; _b = (_b + _a) % 65521; }
            _deflate.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _deflate.Dispose();
                    if (_mode == CompressionMode.Compress && _wroteHeader)
                    {
                        var v = (_b << 16) | _a;
                        _baseStream.WriteByte((byte)(v >> 24)); _baseStream.WriteByte((byte)(v >> 16));
                        _baseStream.WriteByte((byte)(v >> 8)); _baseStream.WriteByte((byte)v);
                    }
                    if (!_leaveOpen) _baseStream.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
#endif
