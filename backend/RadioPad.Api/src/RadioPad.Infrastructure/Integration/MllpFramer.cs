using System.Text;

namespace RadioPad.Infrastructure.Integration;

/// <summary>
/// Iter-31 INT-006 — minimum lower-layer protocol (MLLP) framing helper for
/// HL7 v2 over TCP. A frame is <c>VT(0x0B) &lt;payload&gt; FS(0x1C) CR(0x0D)</c>.
/// </summary>
public static class MllpFramer
{
    public const byte StartByte = 0x0B; // VT
    public const byte EndByte1 = 0x1C; // FS
    public const byte EndByte2 = 0x0D; // CR

    /// <summary>Wraps a payload string in MLLP envelope bytes.</summary>
    public static byte[] Wrap(string payload)
    {
        var body = Encoding.UTF8.GetBytes(payload ?? "");
        var buf = new byte[body.Length + 3];
        buf[0] = StartByte;
        Buffer.BlockCopy(body, 0, buf, 1, body.Length);
        buf[^2] = EndByte1;
        buf[^1] = EndByte2;
        return buf;
    }

    /// <summary>
    /// Reads a single MLLP frame from a stream and returns the decoded
    /// payload. Returns <c>null</c> when the peer closes the connection
    /// before a complete frame is read. Throws on protocol violation.
    /// </summary>
    public static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buf = new byte[1];
        bool sawStart = false;
        bool sawFs = false;
        while (true)
        {
            int n = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0) return null;
            byte b = buf[0];
            if (!sawStart)
            {
                if (b == StartByte) { sawStart = true; continue; }
                // Tolerate leading whitespace / null bytes between frames.
                continue;
            }
            if (sawFs)
            {
                if (b == EndByte2) return Encoding.UTF8.GetString(ms.ToArray());
                throw new InvalidDataException($"MLLP framing error: expected CR after FS, got 0x{b:X2}.");
            }
            if (b == EndByte1) { sawFs = true; continue; }
            ms.WriteByte(b);
        }
    }
}
