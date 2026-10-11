// SPDX-License-Identifier: GPL-3.0-only
using Coop.Contract;
using KcdUs.Agent;
using Xunit;
namespace KcdUs.Tests;
public class QuestXpDeliveryTests:IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"kcdus-native-xp-"+Guid.NewGuid().ToString("N"));
    private string PathOf=>Path.Combine(_dir,"effects.jsonl");
    private static QuestXpAward Award()=>new("n"+new string('a',32)+".1","world-1",new string('b',32),new string('c',32),"1","18","128","21c8fd0",
        "530a0000000000058089ea04090000000000000000000000");
    public void Dispose(){if(Directory.Exists(_dir))Directory.Delete(_dir,true);}
    [Fact]
    public void Recipient_intent_is_durable_before_call_and_completion_replays_after_restart()
    {
        var award=Award();
        using(var d=new QuestXpDelivery(PathOf))
        {
            Assert.Equal(BeginKind.New,d.Prepare(award).Kind);
            Assert.Equal(BeginKind.InProgress,d.Prepare(award).Kind);
            Assert.NotNull(d.Complete(award.Key,award.Soul,"128","0000000000000000","0000000080000000",true,true));
        }
        using(var j=new OperationJournal(PathOf))Assert.Equal(OpState.Complete,j.Get("xp-i:"+award.Key)!.State);
        using var reopened=new QuestXpDelivery(PathOf);
        Assert.Equal(BeginKind.Replay,reopened.Prepare(award).Kind);Assert.NotNull(reopened.Receipt(award.Key));
        Assert.Equal(BeginKind.Conflict,reopened.Prepare(award with{Raw="256"}).Kind);
    }
    [Fact]
    public void Interrupted_native_application_is_quarantined_instead_of_invoked_again()
    {
        var a=Award();using(var d=new QuestXpDelivery(PathOf))d.Prepare(a);
        using var reopened=new QuestXpDelivery(PathOf);Assert.Equal(BeginKind.Quarantined,reopened.Prepare(a).Kind);
        Assert.Null(reopened.Receipt(a.Key));
    }
    [Theory]
    [InlineData(false,true)] [InlineData(true,false)]
    public void Unverified_or_failed_native_results_have_no_receipt(bool applied,bool verified)
    {
        var a=Award();using var d=new QuestXpDelivery(PathOf);d.Prepare(a);
        Assert.Null(d.Complete(a.Key,a.Soul,"128","0000000000000000","0000000080000000",applied,verified));
        Assert.Equal(BeginKind.Quarantined,d.Prepare(a).Kind);
    }
    [Fact]
    public void Another_character_readback_cannot_complete_the_award()
    {
        var a=Award();using var d=new QuestXpDelivery(PathOf);d.Prepare(a);
        Assert.Null(d.Complete(a.Key,new string('d',32),"128","0000000000000000","0000000080000000",true,true));
        Assert.Null(d.Receipt(a.Key));
    }
    [Fact]
    public void Source_outbox_survives_restart_and_only_the_intended_participant_and_world_acknowledge_it()
    {
        var a=Award();using(var d=new QuestXpDelivery(PathOf)) {d.Offer(a);d.Offer(a);Assert.Single(d.Pending(a.World));}
        using var reopened=new QuestXpDelivery(PathOf);Assert.Single(reopened.Pending(a.World));
        reopened.Acknowledge(a.Key,"other-world",a.Participant,a.Soul);Assert.Single(reopened.Pending(a.World));
        reopened.Acknowledge(a.Key,a.World,new string('d',32),a.Soul);Assert.Single(reopened.Pending(a.World));
        reopened.Acknowledge(a.Key,a.World,a.Participant,new string('d',32));Assert.Single(reopened.Pending(a.World));
        reopened.Acknowledge(a.Key,a.World,a.Participant,a.Soul);Assert.Empty(reopened.Pending(a.World));
    }
    [Theory]
    [InlineData("n00000000000000000000000000000000.0","1","18","128","21c8fd0")]
    [InlineData("n00000000000000000000000000000000.1","1","33","128","21c8fd0")]
    [InlineData("n00000000000000000000000000000000.1","0","9","0","2306d30")]
    [InlineData("n00000000000000000000000000000000.1","1","18","128","2201b10")]
    public void Native_delivery_rejects_invalid_keys_indices_amounts_and_nonquest_causes(string key,string kind,string index,string raw,string cause)
    {Assert.False(QuestMirrorRules.ValidNativeXp(key,kind,index,raw,cause,Award().Data));}
}
