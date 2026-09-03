using System.Buffers.Binary;
using System.IO.Compression;

namespace MedNote.Core;

/// <summary>
/// Small dependency-free PNG writer for crop results. Input rows are tightly
/// packed BGRA8 and output uses lossless RGBA8 with PNG filter type 0.
/// </summary>
public static class PngImageEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] EncodeBgra(byte[] bgraBytes, uint width, uint height, uint stride)
    {
        ArgumentNullException.ThrowIfNull(bgraBytes);
        if (width == 0 || height == 0 || stride < checked(width * 4u)
            || bgraBytes.LongLength < checked((long)stride * height))
        {
            throw new ArgumentException("Buffer BGRA không hợp lệ.", nameof(bgraBytes));
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked((int)width * 4 + 1)];
            for (var y = 0u; y < height; y++)
            {
                row[0] = 0;
                var sourceOffset = checked((int)(y * stride));
                for (var x = 0u; x < width; x++)
                {
                    var source = checked(sourceOffset + (int)x * 4);
                    var target = checked(1 + (int)x * 4);
                    row[target] = bgraBytes[source + 2];
                    row[target + 1] = bgraBytes[source + 1];
                    row[target + 2] = bgraBytes[source];
                    row[target + 3] = bgraBytes[source + 3];
                }

                zlib.Write(row);
            }
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = Crc32(type, data);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        output.Write(checksum);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}
