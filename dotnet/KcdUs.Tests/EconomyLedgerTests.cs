// SPDX-License-Identifier: GPL-3.0-only
// Session 10: the per-machine economy audit ledger (EconomyLedger.cs).
using KcdUs.Agent;
using Xunit;

namespace KcdUs.Tests;

public class EconomyLedgerTests : IDisposable
{
    private const string Apple = "2264f217-590e-4c0f-a4c6-f50c6532b9f6";
    private const string Money = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdus-econ-" + Guid.NewGuid().ToString("N")[..8]);
    public EconomyLedgerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string F(string n) => Path.Combine(_dir, n);

    [Fact]
    public void Entries_survive_a_restart_in_order_and_keep_one_chain()
    {
        using (var l = new EconomyLedger(F("e.jsonl")))
        {
            l.Record(EconKind.BarterBuy, Apple, 3, 6000.5, "shop_a");
            l.Record(EconKind.Loss, Money, -40, 6001);
        }
        using (var l = new EconomyLedger(F("e.jsonl")))
        {
            Assert.Equal(2, l.Count);
            Assert.Equal(3, l.Net()[Apple]);
            var e = l.Record(EconKind.Loss, Apple, -1, 6100);
            Assert.Equal(3, e.Seq);
        }
        using var again = new EconomyLedger(F("e.jsonl"));
        Assert.Equal(3, again.Count);
        Assert.Equal(2, again.Net()[Apple]);
    }

    [Fact]
    public void A_torn_final_write_is_dropped_and_a_tampered_record_is_refused()
    {
        using (var l = new EconomyLedger(F("t.jsonl"))) { l.Record(EconKind.Take, Apple, 1, 1); l.Record(EconKind.Take, Apple, 1, 2); }
        File.AppendAllText(F("t.jsonl"), "{\"S\":3,\"K\":1");                       // a crash mid-append
        using (var l = new EconomyLedger(F("t.jsonl")))
        {
            Assert.True(l.IgnoredTornTail);
            Assert.Equal(2, l.Count);
            l.Record(EconKind.Take, Apple, 1, 3);
        }
        string text = File.ReadAllText(F("t.jsonl")).Replace("\"D\":1,", "\"D\":9,");   // someone edited a quantity
        File.WriteAllText(F("t.jsonl"), text);
        Assert.Throws<InvalidDataException>(() => new EconomyLedger(F("t.jsonl")));
    }

    [Fact]
    public void The_residual_names_exactly_what_the_ledger_cannot_explain()
    {
        using var l = new EconomyLedger();
        var before = new Dictionary<string, long> { [Apple] = 5, [Money] = 100 };
        l.Record(EconKind.BarterBuy, Apple, 2, 1);
        l.Record(EconKind.BarterBuy, Money, -30, 1);
        l.Record(EconKind.Loss, Apple, -1, 2);                                       // eaten
        var after = new Dictionary<string, long> { [Apple] = 6, [Money] = 70 };
        Assert.Empty(l.Residual(before, after, 0));
        var stolen = new Dictionary<string, long> { [Apple] = 6, [Money] = 55 };     // 15 vanished with no entry
        var r = l.Residual(before, stolen, 0);
        Assert.Equal(-15, r[Money]);
        Assert.Single(r);
        var windowed = l.Residual(after, after, 3);                                  // nothing recorded after seq 3, nothing changed
        Assert.Empty(windowed);
    }

    [Fact]
    public void Nonsense_is_refused_before_it_reaches_the_disk()
    {
        using var l = new EconomyLedger(F("n.jsonl"));
        Assert.Throws<ArgumentException>(() => l.Record(EconKind.Gain, "bad class", 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Record(EconKind.Gain, Apple, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Record(EconKind.Gain, Apple, 99_999_999, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Record((EconKind)99, Apple, 1, 0));
        Assert.Throws<ArgumentException>(() => l.Record(EconKind.Gain, Apple, 1, 0, "bad\nref"));
        Assert.Equal(0, l.Count);
        Assert.Equal(0, new FileInfo(F("n.jsonl")).Length);
    }

    [Theory]
    [InlineData("5 2264f217-590e-4c0f-a4c6-f50c6532b9f6 -1 6100.25", true)]
    [InlineData("3 2264f217-590e-4c0f-a4c6-f50c6532b9f6 2 6100 shop_a", true)]
    [InlineData("9 2264f217-590e-4c0f-a4c6-f50c6532b9f6 2 6100", false)]
    [InlineData("5 2264f217-590e-4c0f-a4c6-f50c6532b9f6 0 6100", false)]
    [InlineData("5 bad;class 1 6100", false)]
    [InlineData("5 2264f217-590e-4c0f-a4c6-f50c6532b9f6 1 NaN", false)]
    [InlineData("5 2264f217-590e-4c0f-a4c6-f50c6532b9f6 1", false)]
    public void The_mods_event_text_is_parsed_strictly(string text, bool ok) => Assert.Equal(ok, EconomyLedger.TryParseEvent(text, out _, out _, out _, out _, out _));
}
