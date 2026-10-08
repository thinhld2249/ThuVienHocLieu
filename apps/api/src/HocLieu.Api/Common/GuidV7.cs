using System.Security.Cryptography;

namespace HocLieu.Common;

/// <summary>UUID v7 (xếp theo thời gian) — .NET 8 chưa có <c>Guid.CreateVersion7</c>.</summary>
public static class GuidV7
{
    public static Guid New()
    {
        var bytes = new byte[16];
        long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;
        bytes[6] = 0x70; // version 7
        bytes[7] = 0x00; // rand_a (chỉ cần thứ tự thời gian ở 48 bit đầu)
        RandomNumberGenerator.Fill(bytes.AsSpan(8, 8));
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 10xx
        return new Guid(bytes);
    }
}
