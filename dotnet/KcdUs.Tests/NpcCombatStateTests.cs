// SPDX-License-Identifier: GPL-3.0-only
using KcdUs.Agent;
namespace KcdUs.Tests;

public class NpcCombatStateTests
{
    private const string Authority = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void ExtendedCombatSamplesKeepTheirOwnershipTermAndSequenceFence()
    {
        var game = new List<string>(); var peers = new List<string>();
        var n = new NpcCoordinator(() => 50000, game.Add, peers.Add, _ => {}, _ => {});
        n.Tick("w1", false, 1);
        n.HostOwnersV2(["npcown2", "w1", Authority, "1", "bandit_7:2:5"]);
        const string sample = "bandit_7,5,14,20,30,0,0,0,1,4,1,1";
        n.PeerStatesV2(2, ["npcst2", "w1", Authority, "1", sample]);
        Assert.Equal($"NPCSET2|w1|{Authority}|2|1|{sample}", game.Last());
        int count = game.Count;
        n.PeerStatesV2(2, ["npcst2", "w1", Authority, "1", sample]);
        n.PeerStatesV2(3, ["npcst2", "w1", Authority, "2", sample]);
        n.PeerStatesV2(2, ["npcst2", "w1", Authority, "2", sample.Replace(",5,", ",4,")]);
        n.PeerStatesV2(2, ["npcst2", "w1", Authority, "2", sample.Replace(",4,1,1", ",65,1,1")]);
        Assert.Equal(count, game.Count);
    }

    [Fact]
    public void ACurrentFightKeepsItsOwnerAndDisengagementAllowsNormalHandoff()
    {
        long now = 50000;
        var n = new NpcCoordinator(() => now, _ => {}, _ => {}, _ => {}, _ => {});
        n.Tick("w1", true, 1);
        n.GameNear(["NPCNEAR", "bandit_7:2:1"], true);
        n.PeerNear(2, ["npcnear", "w1", "bandit_7:20"], true);
        now += 1000; n.Tick("w1", true, 1);
        for (int i = 0; i < 5; i++)
        {
            n.GameNear(["NPCNEAR", "bandit_7:20:1"], true);
            n.PeerNear(2, ["npcnear", "w1", "bandit_7:2"], true);
            now += 1000; n.Tick("w1", true, 1);
        }
        Assert.Equal(1, n.Owners["bandit_7"]);
        for (int i = 0; i < 3; i++)
        {
            n.GameNear(["NPCNEAR", "bandit_7:20"], true);
            n.PeerNear(2, ["npcnear", "w1", "bandit_7:2"], true);
            now += 1000; n.Tick("w1", true, 1);
        }
        Assert.Equal(2, n.Owners["bandit_7"]);
    }

    [Fact]
    public void LuaReportsNumericEntityHandlesWithoutReinterpretingDecimalDigitsAsHex()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("player.id=10; __mkNpc('bandit_7',100,14,20,30); __world_ents[1].human.PlayAnim=function() return {present=1,active=1,mode=4,target=10} end");
        m.Send("1~NPCMODE|1|w1|1"); m.Send("2~NPCOWN|w1|bandit_7:1");
        m.Advance(1.1);
        Assert.Contains(m.Lines("KCDUS|NPCNEAR|"), l => l.EndsWith("bandit_7:4.0:1"));
        Assert.Contains(m.Lines("KCDUS|NPCST|"), l => l.EndsWith(",4,1,1"));
    }

    [Fact]
    public void RejectedAttackStillClearsTheTargetBeforeReturningAnActorToItsBrain()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("__mkNpc('bandit_7',100,14,20,30); __clear=0; local ghost={id=10,GetName=function() return 'kcdus_ghost_2' end}; System.GetEntity=function(id) if id==10 then return ghost end end; KCDUS.Ghosts.list['2']={id=10}; __world_ents[1].human.PlayAnim=function(self,f,t) if f=='@kcdus/mirror/target' then __target=t; return {reason=0} elseif f=='@kcdus/mirror/attack' then return {reason=7} elseif f=='@kcdus/mirror/clear' then __clear=__clear+1; return {reason=0} end end");
        m.Send("1~NPCMODE|1|w1|1"); m.Send("2~NPCOWN|w1|bandit_7:2");
        m.Send("3~NPCSET|w1|2|bandit_7,14,20,30,0,0,0,1,4,1,1");
        m.Advance(0.2); m.Do("KCDUS.Npcs.releaseAll('test')");
        Assert.Equal("0000000a", m.Str("__target")); Assert.Equal(1, m.Num("__clear"));
        Assert.True(m.Bool("__world_ents[1].brain"));
    }
}
