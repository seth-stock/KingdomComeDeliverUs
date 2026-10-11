// SPDX-License-Identifier: GPL-3.0-only
using Coop.Contract;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace KcdUs.Agent;

public sealed record QuestXpAward(string Key,string World,string Participant,string Soul,
    string Kind,string Index,string Raw,string Cause,string Data)
{
    public static bool Identity(string id)=>id.Length==32 && id.All(char.IsAsciiHexDigitLower) && id!=new string('0',32);
    public bool Valid=>QuestMirrorRules.ValidNativeXp(Key,Kind,Index,Raw,Cause,Data) &&
        OutcomesRules.ValidScope(World) && Identity(Participant) && Identity(Soul);
    public string Json()=>JsonSerializer.Serialize(this);
    public static QuestXpAward? Parse(string json) { try{return JsonSerializer.Deserialize<QuestXpAward>(json);}catch(JsonException){return null;} }
}
public sealed record QuestXpReceipt(string Key,string World,string Participant,string Soul,string Actual,string Before,string After);

/// <summary>Durable native XP delivery. The source repeats network messages;
/// the recipient records intent before calling the engine. Interrupted native
/// applications require checkpoint/native evidence; they are never guessed.</summary>
public sealed class QuestXpDelivery : IDisposable
{
    private readonly OperationJournal _j;
    private readonly object _gate=new();
    private static string Digest(QuestXpAward a)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.Json()))).ToLowerInvariant();
    private static string Inbox(string key)=>"xp-i:"+key;
    private static string Outbox(string key,string who)=>"xp-o:"+key+":"+who;
    public QuestXpDelivery(string path)
    {
        _j=new OperationJournal(path);
        try{_j.Recover();}catch{_j.Dispose();throw;}
    }
    public void Offer(QuestXpAward a)
    {
        if(!a.Valid)throw new ArgumentException("Invalid native quest award.");
        lock(_gate)
        {
            string id=Outbox(a.Key,a.Participant),detail=a.Json();var begin=_j.Begin(id,"quest-xp-out",Digest(a));
            if(begin.Kind!=BeginKind.New)return;
            // The adapter's completed native observation is already applied at
            // the source. These states persist delivery, never invoke it again.
            foreach(var state in new[]{OpState.Reserved,OpState.IntentRecorded,OpState.EngineApplying,OpState.EngineVerified,OpState.LedgerCommitted,OpState.Delivered})
                _j.Advance(id,state,detail,"native-observed");
        }
    }
    public IReadOnlyList<QuestXpAward> Pending(string world)
    {
        lock(_gate)return _j.All().Where(r=>r.Kind=="quest-xp-out" && !OperationJournal.IsTerminal(r.State) && r.Outcome=="native-observed")
            .Select(r=>QuestXpAward.Parse(r.Evidence)).Where(a=>a is not null && a.Valid && a.World==world).Select(a=>a!).ToArray();
    }
    public BeginResult Prepare(QuestXpAward a)
    {
        if(!a.Valid)throw new ArgumentException("Invalid native quest award.");
        lock(_gate)
        {
            string id=Inbox(a.Key);var begin=_j.Begin(id,"quest-xp-in",Digest(a));
            if(begin.Kind==BeginKind.New)
                foreach(var state in new[]{OpState.Reserved,OpState.IntentRecorded,OpState.EngineApplying})_j.Advance(id,state,a.Json());
            return begin;
        }
    }
    public QuestXpReceipt? Receipt(string key)
    {
        lock(_gate)
        {
            var r=_j.Get(Inbox(key));if(r?.State!=OpState.Complete)return null;
            try{return JsonSerializer.Deserialize<QuestXpReceipt>(r.Outcome);}catch(JsonException){return null;}
        }
    }
    public QuestXpReceipt? Complete(string key,string soul,string actual,string before,string after,bool applied,bool verified)
    {
        lock(_gate)
        {
            var r=_j.Get(Inbox(key));var a=r is null?null:QuestXpAward.Parse(r.Evidence);
            if(r is null || a is null || r.State!=OpState.EngineApplying)return Receipt(key);
            bool pair(string value)=>value.Length==16 && value.All(char.IsAsciiHexDigitLower);
            bool amount=uint.TryParse(actual,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out _);
            if(!applied || !verified || soul!=a.Soul || !amount || !pair(before) || !pair(after))
            {
                _j.Advance(r.OperationId,OpState.RecoveryRequired,r.Evidence,"native XP result unverified");return null;
            }
            var receipt=new QuestXpReceipt(key,a.World,a.Participant,soul,actual,before,after);string outcome=JsonSerializer.Serialize(receipt);
            foreach(var state in new[]{OpState.EngineVerified,OpState.LedgerCommitted,OpState.Delivered,OpState.RecipientVerified,OpState.Complete})
                _j.Advance(r.OperationId,state,r.Evidence,outcome);
            return receipt;
        }
    }
    public void Acknowledge(string key,string world,string participant,string soul)
    {
        lock(_gate)
        {
            var r=_j.Get(Outbox(key,participant));var a=r is null?null:QuestXpAward.Parse(r.Evidence);
            if(r is null || a is null || a.World!=world || a.Soul!=soul || OperationJournal.IsTerminal(r.State))return;
            if(r.State==OpState.RecoveryRequired)_j.Resolve(r.OperationId,Observed.Applied,r.Evidence,"recipient-native-verified");
            else if(r.State==OpState.Delivered)
            {
                _j.Advance(r.OperationId,OpState.RecipientVerified,r.Evidence,"recipient-native-verified");
                _j.Advance(r.OperationId,OpState.Complete,r.Evidence);
            }
        }
    }
    public void Dispose()=>_j.Dispose();
}
