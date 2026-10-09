// SPDX-License-Identifier: GPL-3.0-only
using KcdUs.Agent;
namespace KcdUs.Tests;
public class NpcAuthorityTests
{
    private const string A="0123456789abcdef0123456789abcdef", B="1123456789abcdef0123456789abcdef";
    private const string State="bandit_7,5,14,20,30,0,0,0,0";
    [Fact] public void SupportedInterestSetFitsTheFiveHertzBudgetWithoutStarvingTheLaterChunks()
    {
        var game=new List<string>();var n=new NpcCoordinator(()=>50000,game.Add,_=>{},_=>{},_=>{});
        n.Tick("w1",false,1);
        for(int part=0;part<4;part++)
            n.HostOwnersV2(new[]{"npcown2","w1",A,"1",string.Join(';',Enumerable.Range(part*24,24).Select(i=>$"bandit_{i}:2:{i+1}"))});
        int sequence=0;
        for(int tick=0;tick<5;tick++)
            for(int part=0;part<8;part++)
                n.PeerStatesV2(2,new[]{"npcst2","w1",A,(++sequence).ToString(),string.Join(';',Enumerable.Range(part*12,12).Select(i=>$"bandit_{i},{i+1},14,20,30,0,0,0,0"))});
        Assert.Equal(40,game.Count(x=>x.StartsWith("NPCSET2")));
        Assert.Contains(game,x=>x.Contains("bandit_95,96,"));
    }
    [Fact] public void OldTermsWrongOwnersAndReorderedSamplesNeverReachTheGame()
    {
        long now=50000; var game=new List<string>(); var peers=new List<string>();
        var n=new NpcCoordinator(()=>now,game.Add,peers.Add,_=>{},_=>{});
        n.Tick("w1",false,1); n.HostOwnersV2(new[]{"npcown2","w1",A,"1","bandit_7:2:5"});
        n.PeerStatesV2(2,new[]{"npcst2","w1",A,"2",State});
        int count=game.Count;
        n.PeerStatesV2(2,new[]{"npcst2","w1",A,"1",State});
        n.PeerStatesV2(3,new[]{"npcst2","w1",A,"3",State});
        n.PeerStatesV2(2,new[]{"npcst2","w1",A,"3",State.Replace(",5,",",4,")});
        Assert.Equal(count,game.Count);
        n.HostOwnersV2(new[]{"npcown2","w1",A,"2","bandit_7:3:6"});
        count=game.Count;
        n.HostOwnersV2(new[]{"npcown2","w1",A,"1","bandit_7:2:5"});
        n.PeerStatesV2(2,new[]{"npcst2","w1",A,"4",State});
        Assert.Equal(count,game.Count);
        Assert.Equal(3,n.Owners["bandit_7"]);
    }
    [Fact] public void RestartedAuthorityRetiresThePreviousIncarnationAndResetsSampleCounters()
    {
        var game=new List<string>(); var n=new NpcCoordinator(()=>50000,game.Add,_=>{},_=>{},_=>{});
        n.Tick("w1",false,1);
        n.HostOwnersV2(new[]{"npcown2","w1",A,"9","bandit_7:2:5"});
        n.PeerStatesV2(2,new[]{"npcst2","w1",A,"50",State});
        n.HostOwnersV2(new[]{"npcown2","w1",B,"1","bandit_7:2:1"});
        n.PeerStatesV2(2,new[]{"npcst2","w1",B,"1",State.Replace(",5,",",1,")});
        Assert.StartsWith("NPCSET2|w1|"+B+"|2|1|",game.Last());
        int count=game.Count;
        n.HostOwnersV2(new[]{"npcown2","w1",A,"10","bandit_7:2:6"});
        n.PeerStatesV2(2,new[]{"npcst2","w1",A,"51",State});
        Assert.Equal(count,game.Count); Assert.Equal(B,n.Authority);
    }
    [Fact] public void LuaPuppetsRespectTermsSequencesAndReplacedNativeHandles()
    {
        var m=new LuaMod(); m.InWorld(); m.Do("__mkNpc('bandit_7',100,14,20,30)");
        m.Send("1~NPCMODE|1|w1|1"); m.Send("2~NPCOWN2|w1|"+A+"|1|bandit_7:2:5");
        m.Send("3~NPCSET2|w1|"+A+"|2|2|"+State);
        Assert.False(m.Bool("__world_ents[1].brain"));
        m.Send("4~NPCSET2|w1|"+A+"|2|1|bandit_7,5,999,20,30,0,0,0,0");
        Assert.Equal(14,m.Num("KCDUS.Npcs.puppets.bandit_7.x"));
        m.Send("5~NPCOWN2|w1|"+A+"|2|bandit_7:3:6");
        Assert.True(m.Bool("__world_ents[1].brain"));
        m.Send("6~NPCSET2|w1|"+A+"|2|3|"+State);
        Assert.False(m.Bool("KCDUS.Npcs.puppets.bandit_7~=nil"));
        m.Send("7~NPCSET2|w1|"+A+"|3|1|bandit_7,6,14,20,30,0,0,0,0");
        m.Do("__mkNpc('bandit_7',100,18,20,30); __world_ents[1]=__world_ents[2]; __world_ents[2]=nil; __world_ents[1].brain=false");
        m.Do("KCDUS.Npcs.releaseAll('test')");
        Assert.False(m.Bool("__world_ents[1].brain")); // never release a different actor's brain
    }
}
