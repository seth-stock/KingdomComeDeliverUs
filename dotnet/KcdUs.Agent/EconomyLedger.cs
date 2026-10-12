// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KcdUs.Agent;

/// <summary>What changed this player's pack, as the mod could attribute it.</summary>
public enum EconKind : byte
{
    Take = 1,        // from a shared holder (chest, body, ground, stable) into the pack
    Put = 2,         // from the pack into a shared holder
    BarterBuy = 3,   // from a shop's stock into the pack (the price is its own Money entry)
    BarterSell = 4,  // from the pack into a shop's stock
    Loss = 5,        // left the pack and no holder received it: eaten, used up, broken, dropped, spent on a craft
    Gain = 6,        // entered the pack and no holder gave it: found, crafted, picked up
    Reward = 7,      // a quest reward (the reward window's observation)
    Crime = 8,       // a fine paid or goods confiscated by a judged crime
}

public readonly record struct EconEntry(long Seq, EconKind Kind, string Class, long Delta, double WorldTime, string Ref);

/// <summary>
/// Session 10: the per-machine economy AUDIT ledger. Every change of this player's pack that the mod's watchers could attribute (take,
/// put, barter, loss, gain, reward, crime) is appended here, durably and hash-chained, as a signed quantity per item class (money is
/// the money class). It does not move or restore anything; it makes the pack's history checkable: <see cref="Residual"/> says which
/// classes changed by a quantity the ledger cannot explain. Takes and puts of SHARED holders are additionally decided and journalled by
/// the host (DurableLootLedger) and the joiner (JoinerCustody); this ledger is the part for everything the host never sees (a meal, a
/// purchase, a fine). Attribution is by observation of the pack, so a coincident gain and loss of the same class in one watch interval
/// nets out; that is a limit of observation, not hidden.
/// </summary>
public sealed class EconomyLedger : IDisposable
{
    public const int MaxClassLength = 64;
    private readonly object _gate = new();
    private readonly FileStream? _file;
    private readonly List<EconEntry> _entries = new();
    private string _prev = new('0', 64);

    /// <summary>An unterminated final write ignored on opening (a crash mid-append).</summary>
    public bool IgnoredTornTail { get; private set; }

    /// <summary>In memory only (tests, or when no journal path is available).</summary>
    public EconomyLedger() { }

    public EconomyLedger(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        Load();
    }

    private sealed record Line(long S, byte K, string C, long D, double T, string R, string H);

    private static string HashOf(string prev, long s, byte k, string c, long d, double t, string r) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', prev, s, k, c, d, t.ToString("R", CultureInfo.InvariantCulture), r)))).ToLowerInvariant();

    private void Load()
    {
        _file!.Position = 0;
        using var reader = new StreamReader(_file, Encoding.UTF8, false, 4096, leaveOpen: true);
        string text = reader.ReadToEnd();
        long good = 0;
        int at = 0, lineNo = 0;
        while (at < text.Length)
        {
            int nl = text.IndexOf('\n', at);
            if (nl < 0) { IgnoredTornTail = true; break; }                 // unterminated: a torn final write
            string raw = text.Substring(at, nl - at);
            lineNo++;
            var l = JsonSerializer.Deserialize<Line>(raw) ?? throw new InvalidDataException($"The economy ledger has an unreadable record at line {lineNo}; it is not repaired automatically.");
            if (l.S != _entries.Count + 1 || l.H != HashOf(_prev, l.S, l.K, l.C, l.D, l.T, l.R) || !Enum.IsDefined((EconKind)l.K))
                throw new InvalidDataException($"The economy ledger's chain breaks at line {lineNo}; it is not repaired automatically.");
            _entries.Add(new EconEntry(l.S, (EconKind)l.K, l.C, l.D, l.T, l.R));
            _prev = l.H;
            at = nl + 1;
            good = Encoding.UTF8.GetByteCount(text.AsSpan(0, at));
        }
        if (IgnoredTornTail) { _file.SetLength(good); }
        _file.Position = _file.Length;
    }

    public static bool ValidClass(string c) => c.Length is > 0 and <= MaxClassLength && c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    public EconEntry Record(EconKind kind, string cls, long delta, double worldTime, string reference = "")
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!ValidClass(cls)) throw new ArgumentException("bad item class", nameof(cls));
        if (delta == 0 || Math.Abs(delta) > 10_000_000) throw new ArgumentOutOfRangeException(nameof(delta));
        if (reference.Length > 80 || reference.Any(ch => ch < 0x20 || ch > 0x7E)) throw new ArgumentException("bad reference", nameof(reference));
        if (double.IsNaN(worldTime) || double.IsInfinity(worldTime)) worldTime = 0;
        lock (_gate)
        {
            long seq = _entries.Count + 1;
            string h = HashOf(_prev, seq, (byte)kind, cls, delta, worldTime, reference);
            if (_file != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Line(seq, (byte)kind, cls, delta, worldTime, reference, h)) + "\n");
                _file.Write(bytes, 0, bytes.Length);
                _file.Flush(true);                                           // on disk before the entry exists in memory
            }
            _prev = h;
            var e = new EconEntry(seq, kind, cls, delta, worldTime, reference);
            _entries.Add(e);
            return e;
        }
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
    public IReadOnlyList<EconEntry> Entries(long fromSeqExclusive = 0) { lock (_gate) return _entries.Where(e => e.Seq > fromSeqExclusive).ToList(); }

    /// <summary>Net quantity per class of the entries after <paramref name="fromSeqExclusive"/> (optionally one kind).</summary>
    public Dictionary<string, long> Net(long fromSeqExclusive = 0, EconKind? kind = null)
    {
        var net = new Dictionary<string, long>(StringComparer.Ordinal);
        lock (_gate)
            foreach (var e in _entries)
                if (e.Seq > fromSeqExclusive && (kind is null || e.Kind == kind)) net[e.Class] = net.GetValueOrDefault(e.Class) + e.Delta;
        return net;
    }

    /// <summary>
    /// The classes whose pack quantity changed from <paramref name="before"/> to <paramref name="after"/> by something other than what the
    /// ledger recorded since <paramref name="fromSeqExclusive"/>: actual change minus ledgered change, only where not zero.
    /// </summary>
    public Dictionary<string, long> Residual(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after, long fromSeqExclusive)
    {
        var net = Net(fromSeqExclusive);
        var residual = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var cls in before.Keys.Concat(after.Keys).Concat(net.Keys).Distinct(StringComparer.Ordinal))
        {
            long actual = after.GetValueOrDefault(cls) - before.GetValueOrDefault(cls);
            long r = actual - net.GetValueOrDefault(cls);
            if (r != 0) residual[cls] = r;
        }
        return residual;
    }

    /// <summary>The mod's event text: "&lt;kind&gt; &lt;class&gt; &lt;delta&gt; &lt;world time&gt; [ref]".</summary>
    public static bool TryParseEvent(string text, out EconKind kind, out string cls, out long delta, out double worldTime, out string reference)
    {
        kind = 0; cls = ""; delta = 0; worldTime = 0; reference = "";
        var f = text.Split(' ');
        if (f.Length is < 4 or > 5) return false;
        if (!byte.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte k) || !Enum.IsDefined((EconKind)k)) return false;
        kind = (EconKind)k;
        cls = f[1];
        if (!ValidClass(cls) || !long.TryParse(f[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out delta) || delta == 0 || Math.Abs(delta) > 10_000_000) return false;
        if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out worldTime) || double.IsNaN(worldTime) || double.IsInfinity(worldTime)) return false;
        if (f.Length == 5) { reference = f[4]; if (reference.Length > 80) return false; }
        return true;
    }

    public void Dispose() { lock (_gate) _file?.Dispose(); }
}
