// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
namespace KcdUs.Agent;

public sealed partial class NpcCoordinator
{
    private string _authority="";
    private long _ownerRevision,_termCounter,_sampleSequence;
    private readonly Dictionary<string,long> _terms=new(StringComparer.Ordinal);
    private readonly Dictionary<int,long> _receivedSequences=new();
    private readonly HashSet<string> _retiredAuthorities=new(StringComparer.Ordinal);
    public string Authority { get { lock(_gate) return _authority; } }
    private void ResetAuthority(bool host)
    {
        _authority=host?Guid.NewGuid().ToString("N"):"";
        _ownerRevision=_termCounter=_sampleSequence=0;
        _terms.Clear(); _receivedSequences.Clear(); _retiredAuthorities.Clear(); _stateRate.Clear();
    }
    private void SetOwner(string name,int id)
    {
        _owner[name]=id; _terms[name]=++_termCounter;
    }
    private static bool Counter(string text,out long value) => long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value) && value is >0 and <1000000000000;
    private static Dictionary<string,(int Owner,long Term)>? OwnersV2(string text)
    {
        if(text.Length is 0 or >3200) return null;
        var rows=text.Split(';'); if(rows.Length>24) return null;
        var result=new Dictionary<string,(int,long)>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            var f=row.Split(':');
            if(f.Length!=3 || !OutcomesRules.ValidName(f[0]) || !int.TryParse(f[1],NumberStyles.None,CultureInfo.InvariantCulture,out int id)
                || id is <1 or >255 || !Counter(f[2],out long term) || !result.TryAdd(f[0],(id,term))) return null;
        }
        return result;
    }
    public void HostOwnersV2(string[] f)
    {
        lock(_gate)
        {
            if(!_on || f.Length!=5 || f[1]!=_scope || !Guid.TryParseExact(f[2],"N",out _) || !Counter(f[3],out long revision)
                || OwnersV2(f[4]) is not {} owners || _retiredAuthorities.Contains(f[2])) return;
            if(_authority!=f[2])
            {
                if(_retiredAuthorities.Count>=256) { Reset("too many authority incarnations; rejoin required"); return; }
                if(_authority.Length>0) _retiredAuthorities.Add(_authority);
                _authority=f[2]; _ownerRevision=0; _owner.Clear(); _terms.Clear(); _receivedSequences.Clear(); _sampleSequence=0;
            }
            if(revision<_ownerRevision) return;
            // Equal revisions can carry different chunks, but conflicting/revoked terms cannot return.
            foreach(var (name,o) in owners)
                if(_terms.TryGetValue(name,out long term) && (o.Term<term || o.Term==term && _owner[name]!=o.Owner)) return;
            _ownerRevision=revision;
            foreach(var (name,o) in owners) { _owner[name]=o.Owner; _terms[name]=o.Term; }
            _toGame(string.Join('|',"NPCOWN2",f[1],f[2],f[3],f[4]));
        }
    }
    private void SendStatesV2(string text)
    {
        if(_authority.Length==0) return;
        var rows=new List<string>();
        foreach(var row in text.Split(';'))
        {
            string name=row.Split(',')[0];
            if(_owner.TryGetValue(name,out int owner) && owner==_myId && _terms.TryGetValue(name,out long term))
                rows.Add(name+","+term.ToString(CultureInfo.InvariantCulture)+row[name.Length..]);
        }
        if(rows.Count>0) _toPeers(FormattableString.Invariant($"npcst2|{_scope}|{_authority}|{++_sampleSequence}|{string.Join(';',rows)}"));
    }
    public void PeerStatesV2(int from,string[] f)
    {
        lock(_gate)
        {
            if(!_on || f.Length!=5 || f[1]!=_scope || f[2]!=_authority || _authority.Length==0 || !Counter(f[3],out long sequence)
                || sequence<=_receivedSequences.GetValueOrDefault(from) || f[4].Length is 0 or >2200) return;
            var records=f[4].Split(';'); if(records.Length>12) return;
            var selected=new List<string>(); var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in records)
            {
                var v=row.Split(',');
                if(v.Length!=9 || !Counter(v[1],out long term) || !names.Add(v[0]) || !ValidStates(v[0]+","+string.Join(',',v.Skip(2)))) return;
                if(_owner.TryGetValue(v[0],out int owner) && owner==from && _terms.TryGetValue(v[0],out long expected) && expected==term) selected.Add(row);
            }
            if(selected.Count==0) return;
            long now=_now(); var rate=_stateRate.TryGetValue(from,out var r) && now-r.Window<1000?r:(now,0);
            if(rate.Item2>=MaxStatesPerSecond) return;
            _stateRate[from]=(rate.Item1,rate.Item2+1); _receivedSequences[from]=sequence;
            _toGame(FormattableString.Invariant($"NPCSET2|{_scope}|{_authority}|{from}|{sequence}|{string.Join(';',selected)}"));
        }
    }
}
