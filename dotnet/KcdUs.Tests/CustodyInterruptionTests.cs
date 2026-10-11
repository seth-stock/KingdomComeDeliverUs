// SPDX-License-Identifier: GPL-3.0-only
using Coop.Contract;
using KcdUs.Agent;
namespace KcdUs.Tests;

public class CustodyInterruptionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdus-custody-interrupt-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "custody.jsonl");
    public CustodyInterruptionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);
    private static CustodyTake Take(GuestCustody c) => c.Record("abcd1234", 1, "bandit_7", "5ef63059-322e-4e1b-abe8-926e100c770e", 1, 1, 0, 6000, "w1");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedTakeEventsKeepTheOperationAndAnswerIncludingAfterCompletion(bool durable)
    {
        using var c = new GuestCustody(durable ? FilePath : "");
        var t = Take(c); c.Answer(t.Op, "ok");
        Assert.Equal(t.Op, Take(c).Op); Assert.Equal("ok", Assert.Single(c.Open()).Verdict);
        Assert.Throws<InvalidDataException>(() => c.Record(t.Nonce, t.Tok, t.Holder, t.Class, t.N, .5, t.Price, t.TakeWt, t.Scope));
        c.Settle(t.Op, "kept"); Assert.Equal(t.Op, Take(c).Op); Assert.Empty(c.Open());
    }

    [Fact]
    public void CompletedReceiptsSurviveAProcessRestart()
    {
        string op;
        using (var c = new GuestCustody(FilePath)) { op = Take(c).Op; c.Answer(op, "ok"); Assert.True(c.Settle(op, "kept")); }
        using var restarted = new GuestCustody(FilePath);
        Assert.Empty(restarted.Open());
        var receipt = Assert.Single(restarted.Receipts());
        Assert.Equal(op, receipt.Take.Op); Assert.Equal("kept", receipt.How);
    }

    [Theory]
    [InlineData(OpState.Delivered)]
    [InlineData(OpState.RecipientVerified)]
    public void InterruptedSettlementContinuesForward(OpState interrupted)
    {
        CustodyTake take;
        using (var c = new GuestCustody(FilePath)) { take = Take(c); c.Answer(take.Op, "gone"); }
        using (var j = new OperationJournal(FilePath))
        {
            j.Advance("gt:" + take.Op, OpState.Delivered, take.Json(), "returned");
            if (interrupted == OpState.RecipientVerified) j.Advance("gt:" + take.Op, interrupted, take.Json(), "returned");
        }
        using var resumed = new GuestCustody(FilePath);
        Assert.True(resumed.Settle(take.Op, "returned")); Assert.Empty(resumed.Open()); Assert.Single(resumed.Receipts());
    }

    [Fact]
    public void PartialReturnIsQuarantinedAndNeverAdvertisedAsComplete()
    {
        using (var c = new GuestCustody(FilePath)) { var t = Take(c); Assert.False(c.Settle(t.Op, "partial")); Assert.Equal(1, c.QuarantinedCount); }
        using var restarted = new GuestCustody(FilePath);
        Assert.Equal(1, restarted.QuarantinedCount); Assert.Empty(restarted.Receipts()); Assert.Empty(restarted.Open());
    }
}
