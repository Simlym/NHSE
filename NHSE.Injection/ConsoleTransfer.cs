using System;
using System.IO;
using System.Linq;

namespace NHSE.Injection;

/// <summary>Bounded transfers shared by the local workspace and offline transport tests.</summary>
public static class ConsoleTransfer
{
    public const int MaxLength = 16384;
    public static void Check(uint offset, int length)
    {
        if (offset == 0 || length is < 1 or > MaxLength || (ulong)offset + (uint)length > (ulong)uint.MaxValue + 1)
            throw new ArgumentException("consoleInvalid");
    }
    public static byte[] Read(IRAMReadWriter bot, uint offset, int length)
    {
        Check(offset, length);
        if (!bot.Connected) throw new InvalidOperationException("consoleDisconnected");
        var bytes = bot.ReadBytes(offset, length);
        if (bytes.Length != length) throw new IOException("consoleShortRead");
        return bytes.ToArray();
    }
    public static void Write(IRAMReadWriter bot, uint offset, byte[] bytes)
    {
        Check(offset, bytes.Length);
        if (!bot.Connected) throw new InvalidOperationException("consoleDisconnected");
        bot.WriteBytes(bytes.ToArray(), offset);
    }
}
