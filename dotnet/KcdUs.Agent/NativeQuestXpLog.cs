// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KcdUs.Agent;

public sealed record NativeQuestXp(string Key, string World, bool Host, string Soul,
    uint Kind, uint Index, uint Requested, uint Actual, bool Applied, bool Verified,
    string Before, string After, string Cause, string CauseData);

/// <summary>Read the adapter's pointer-free, hash-chained native quest XP queue.
/// Incomplete writes stay unread. Missing results remain pending intents.</summary>
public sealed class NativeQuestXpLog
{
    public const int RecordSize = 268;
    private const int MaxPending = 4096;
    private readonly Dictionary<ulong, byte[]> _intents = new();
    private byte[] _previous = new byte[32];
    private byte[]? _boot;
    private ulong _sequence;
    public long Offset => checked((long)_sequence * RecordSize);
    public int Pending => _intents.Count;

    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
    private static ulong U64(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt64LittleEndian(b[at..]);
    private static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).ToLowerInvariant();
    private static bool WorldByte(byte b) => b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z'
        or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'_' or (byte)'.';

    private static string World(ReadOnlySpan<byte> b)
    {
        int length = b.IndexOf((byte)0); if (length < 0) length = b.Length;
        for (int i = 0; i < length; i++) if (!WorldByte(b[i])) throw new InvalidDataException("Invalid native XP world identity.");
        for (int i = length; i < b.Length; i++) if (b[i] != 0) throw new InvalidDataException("Noncanonical native XP world padding.");
        return Encoding.ASCII.GetString(b[..length]);
    }

    /// <summary>Read at most maxRecords records. The caller keeps the stream
    /// positioned arbitrarily; this method seeks only to its verified cursor.</summary>
    public IReadOnlyList<NativeQuestXp> Read(Stream stream, int maxRecords = 64)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Native XP log needs a seekable readable stream.");
        if (maxRecords is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        if (stream.Length < Offset) throw new InvalidDataException("Native XP queue was truncated after verification.");
        stream.Position = Offset;
        var result = new List<NativeQuestXp>();
        for (int count = 0; count < maxRecords && stream.Length - stream.Position >= RecordSize; count++)
        {
            var b = new byte[RecordSize]; stream.ReadExactly(b);
            uint phase = U32(b, 12), kind = U32(b, 80), index = U32(b, 84), requested = U32(b, 88);
            uint applied = U32(b, 96), verified = U32(b, 100), cause = U32(b, 104), length = U32(b, 108), flags = U32(b, 200);
            ulong sequence = U64(b, 16), id = U64(b, 24);
            if (U32(b, 0) != 0x3150584e || U32(b, 4) != 1 || U32(b, 8) != RecordSize || phase is not (1 or 2)
                || sequence != _sequence + 1 || id == 0 || id > sequence || kind > 1 || index > (kind == 1 ? 32 : 9)
                || requested is 0 or > 128 * 100000 || applied > 1 || verified > 1 || flags > 7 || (flags & 3) == 2
                || !(cause == 0x2306d30 && length == 16 || cause == 0x21c8fd0 && length == 24))
                throw new InvalidDataException("Invalid native XP record framing or sequence.");
            string world = World(b.AsSpan(136, 64));
            if ((flags & 1) != 0 && world.Length == 0) throw new InvalidDataException("Shared native XP has no world.");
            int dataBytes = length == 24 ? 20 : 16;
            for (int i = dataBytes; i < 24; i++) if (b[112 + i] != 0) throw new InvalidDataException("Noncanonical native XP cause padding.");
            if (!b.AsSpan(204, 32).SequenceEqual(_previous) || !b.AsSpan(236, 32).SequenceEqual(SHA256.HashData(b.AsSpan(0, 236)))
                || _boot is not null && !b.AsSpan(32, 16).SequenceEqual(_boot)) throw new InvalidDataException("Native XP queue hash chain changed.");
            if (phase == 1)
            {
                if (id != sequence || U64(b, 72) != 0 || U32(b, 92) != 0 || applied != 0 || verified != 0 || _intents.Count == MaxPending)
                    throw new InvalidDataException("Invalid or excessive native XP intents.");
                _intents.Add(id, b);
            }
            else
            {
                if (!_intents.TryGetValue(id, out var intent)
                    || !intent.AsSpan(32, 40).SequenceEqual(b.AsSpan(32, 40))
                    || !intent.AsSpan(80, 12).SequenceEqual(b.AsSpan(80, 12))
                    || !intent.AsSpan(104, 100).SequenceEqual(b.AsSpan(104, 100)))
                    throw new InvalidDataException("Native XP result has no matching intent.");
                result.Add(new NativeQuestXp("n" + Hex(b.AsSpan(32, 16)) + "." + id, world, flags == 3,
                    Hex(b.AsSpan(48, 16)), kind, index, requested, U32(b, 92), applied == 1, verified == 1,
                    Hex(b.AsSpan(64, 8)), Hex(b.AsSpan(72, 8)), cause.ToString("x"), Hex(b.AsSpan(112, (int)length))));
                _intents.Remove(id);
            }
            _boot ??= b.AsSpan(32, 16).ToArray(); _previous = b.AsSpan(236, 32).ToArray(); _sequence = sequence;
        }
        return result;
    }
}
