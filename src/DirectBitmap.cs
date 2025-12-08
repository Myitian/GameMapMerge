using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GameMapMerge;

public class DirectBitmap : IDisposable
{
    public Bitmap Bitmap { get; private set; }
    public uint[] Bits { get; private set; }
    public bool Disposed { get; private set; }
    public int Height { get; private set; }
    public int Width { get; private set; }
    protected GCHandle BitsHandle { get; private set; }
    public DirectBitmap(Bitmap src, Size size) : this(size.Width, size.Height)
    {
        using Graphics g = Graphics.FromImage(Bitmap);
        using ImageAttributes attr = new();
        attr.SetWrapMode(WrapMode.TileFlipXY);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(src, new(0, 0, Width, Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
    }
    public DirectBitmap(int width, int height)
    {
        Width = width;
        Height = height;
        Bits = GC.AllocateUninitializedArray<uint>(Width * Height, true);
        BitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
        Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, BitsHandle.AddrOfPinnedObject());
    }
    public void CopyTo(DirectBitmap dst, Rectangle rect)
    {
        int dstOffset = rect.Top * dst.Width + rect.Left;
        int thisOffset = 0;
        for (int y = 0; y < Height; y++, dstOffset += dst.Width, thisOffset += Width)
            Bits.AsSpan(thisOffset, rect.Width).CopyTo(dst.Bits.AsSpan(dstOffset));
    }
    public void CopyTo(DirectBitmap dst, Point lt)
    {
        CopyTo(dst, new Rectangle(
            lt.X,
            lt.Y,
            Math.Min(Width, dst.Width - lt.X),
            Math.Min(Height, dst.Height - lt.Y)));
    }
    public void CopyFrom(Bitmap src, Rectangle dst)
    {
        // Do not copy directly from GDI+
        using DirectBitmap dbmp = new(src, dst.Size);
        dbmp.CopyTo(this, dst.Location);
    }
    public void Fill(uint color, Rectangle rect)
    {
        int offset = rect.Top * Width + rect.Left;
        if (rect.Width == Width)
            Array.Fill(Bits, color, offset, rect.Width * rect.Height);
        else
        {
            for (int y = 0; y < rect.Height; y++, offset += Width)
                Array.Fill(Bits, color, offset, rect.Width);
        }
    }
    private static ReadOnlySpan<byte> PNGHeader => [
          0x89, (byte)'P', (byte)'N', (byte)'G',(byte)'\r',(byte)'\n',      0x1A,(byte)'\n',
          0x00,      0x00,      0x00,      0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
    /* W   */0,/* W   */0,/* W   */0,/* W   */0,/* H   */0,/* H   */0,/* H   */0,/* H   */0,
          0x08,      0x06,      0x00,      0x00,      0x00,/* CRC */0,/* CRC */0,/* CRC */0,
    /* CRC */0];
    private static ReadOnlySpan<byte> PNGFooter => [
              0x00,      0x00,      0x00,      0x00, (byte)'I', (byte)'E', (byte)'N', (byte)'D',
              0xAE,      0x42,      0x60,      0x82];
    public void SavePNG(Stream destination, CompressionLevel compressionLevel, int approxIDATSize)
    {
        // Header + IHDR
        Span<byte> header = stackalloc byte[33];
        PNGHeader.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header[16..], Width);
        BinaryPrimitives.WriteInt32BigEndian(header[20..], Height);
        BinaryPrimitives.WriteUInt32BigEndian(header[29..], Crc32.HashToUInt32(header.Slice(12, 17)));
        destination.Write(header);
        using (ZLibStream body = new(new IDATStream(destination, approxIDATSize), compressionLevel, false))
        {
            ReadOnlySpan<uint> span = Bits;
            Span<uint> line = GC.AllocateUninitializedArray<uint>(Width);
            while (!span.IsEmpty)
            {
                // GDI+  BGRA(LE) -> PNG   RGBA(BE)
                // 0xAARRGGBB(LE) -> 0xAABBGGRR(LE)
                // Modern Windows will not be in big-endian byte order.
                int i = 0;
                foreach (uint px in span[..Width])
                    line[i++] = (px & 0xFF00FF00) | BitOperations.RotateRight(px & 0x00FF00FF, 16);
                body.WriteByte(0);
                body.Write(MemoryMarshal.AsBytes(line));
                span = span[Width..];
            }
        }
        // IEND
        destination.Write(PNGFooter);
    }
    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        Bitmap.Dispose();
        BitsHandle.Free();
        GC.SuppressFinalize(this);
    }
    public sealed class IDATStream : Stream
    {
        private readonly Crc32 _crc32 = new();
        private readonly Stream _baseStream;
        private readonly MemoryStream _memoryStream;
        private readonly int _approxIDATSize;
        public IDATStream(Stream baseStream, int approxIDATSize)
        {
            _baseStream = baseStream;
            _approxIDATSize = approxIDATSize;
            _crc32.Append("IDAT"u8);
            _memoryStream = new(approxIDATSize);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value)
            => throw new NotSupportedException();
        public override void WriteByte(byte value)
            => Write([value]);
        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_memoryStream.Length + buffer.Length > _memoryStream.Capacity)
                Flush();
            _crc32.Append(buffer);
            _memoryStream.Write(buffer);
        }
        public override void Flush()
        {
            if (_memoryStream.Length == 0)
                return;
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, (int)_memoryStream.Length);
            _baseStream.Write(buffer);
            _baseStream.Write("IDAT"u8);
            _memoryStream.Position = 0;
            _memoryStream.CopyTo(_baseStream);
            _memoryStream.SetLength(0); // use filter 0 to simplify code
            BinaryPrimitives.WriteUInt32BigEndian(buffer, _crc32.GetCurrentHashAsUInt32());
            _baseStream.Write(buffer);
            _crc32.Reset();
            _crc32.Append("IDAT"u8);
        }
        public override void Close()
        {
            Flush();
            _memoryStream.Dispose();
        }
    }
}
