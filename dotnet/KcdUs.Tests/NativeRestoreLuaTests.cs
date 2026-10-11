// SPDX-License-Identifier: GPL-3.0-only
using Xunit;
namespace KcdUs.Tests;
public class NativeRestoreLuaTests
{
    private const string Coin="5ef63059-322e-4e1b-abe8-926e100c770e",Op="0123456789abcdef0123456789abcdef";
    private static LuaMod World()
    {
        var m=new LuaMod();m.InWorld();m.Advance(0.5);
        m.Do(@"__mkNpc('bandit_7',0,12,20,30).dead=true
            local create=ItemManager.CreateItem; __created=0
            ItemManager.CreateItem=function(c,h,n) __created=__created+1;local r=create(c,h,n);r.kcdusBridge=1;r.kcdusPersistent=string.format('%032x',__created);return r end");
        m.Send("1~LOOTMODE|1|w1|1");m.ClearLog();return m;
    }
    private static string Command(LuaMod m,string op=Op) => "2~LOOTRESTORE|w1|"+op+"|bandit_7|"+Coin+"|50|0.5000|"+m.Str("KCDUS.Loot.nonce");
    [Fact]
    public void A_native_same_instance_is_verified_and_duplicate_commands_only_repeat_the_receipt()
    {
        var m=World();var command=Command(m);m.Send(command);m.Send(command.Replace("2~","3~"));
        Assert.Equal(1,m.Num("__created"));Assert.Equal(50,m.Num("__world_ents[1].inventory:GetCountOfClass('"+Coin+"')"));
        Assert.Equal(2,m.Lines("KCDUS|LOOT|restored|"+Op).Count);
        Assert.Contains("|00000000000000000000000000000001|verified|",m.Lines("KCDUS|LOOT|restored|")[0]);
    }
    [Theory]
    [InlineData("__world_ents[1].inventory.AddItem=function() end")]
    [InlineData("__world_ents[1].inventory.AddItem=function(self,it) it.kcdusPersistent='ffffffffffffffffffffffffffffffff';__world_ents[1].items[1]=it end")]
    [InlineData("__world_ents[1].inventory.AddItem=function(self,it) it.amount=49;__world_ents[1].items[1]=it end")]
    [InlineData("__world_ents[1].inventory.AddItem=function(self,it) __world_ents[1].items[1]=it;error('partial native write') end")]
    public void Inert_merged_changed_or_partial_native_writes_are_not_verified_or_retried(string mutation)
    {
        var m=World();m.Do(mutation);var cmd=Command(m);m.Send(cmd);m.Send(cmd.Replace("2~","3~"));
        Assert.Equal(1,m.Num("__created"));Assert.Single(m.Lines("KCDUS|LOOT|restored|"));
        Assert.Contains("|uncertain|",m.Lines("KCDUS|LOOT|restored|")[0]);
    }
    [Fact]
    public void Another_load_scope_or_changed_duplicate_payload_never_creates_a_second_item()
    {
        var m=World();var command=Command(m);m.Send(command.Replace("|w1|","|w2|"));
        m.Send(command.Replace(m.Str("KCDUS.Loot.nonce"),"deadbeef"));Assert.Equal(0,m.Num("__created"));
        m.Send(command);m.Send(command.Replace("|50|","|60|"));Assert.Equal(1,m.Num("__created"));
    }
}
