using KcdUs.Agent;
namespace KcdUs.Tests;
public class OutfitTests
{
    private const string Gear="1;bbbbbbbb-0000-0000-0000-000000000001,0.50000";
    [Theory]
    [InlineData("1",true)]
    [InlineData(Gear,true)]
    [InlineData("1;00000000-0000-0000-0000-00000000001c,1.00000",true)]
    [InlineData("1;bbbbbbbb-0000-0000-0000-000000000001,NaN",false)]
    [InlineData("1;bbbbbbbb-0000-0000-0000-000000000001,1.01",false)]
    [InlineData("1;bbbbbbbb-0000-0000-0000-000000000001,0.5~PA",false)]
    [InlineData("1;bbbbbbbb-0000-0000-0000-000000000001,0.5;bbbbbbbb-0000-0000-0000-000000000001,0.5",false)]
    public void AgentAndLuaAgreeOnSnapshotValidation(string snapshot,bool expected)
    {
        var m=new LuaMod();
        Assert.Equal(expected,OutfitSnapshot.Valid(snapshot));
        Assert.Equal(expected,m.Bool($"KCDUS.Outfits.parse([==[{snapshot}]==])~=nil"));
    }
    [Fact]
    public void EquipmentArrivingBeforePositionIsAppliedOnceAndClearedOnLeave()
    {
        var m=new LuaMod();m.InWorld();m.Do("__world.engineBridge=true");
        m.Send("1~OUT|2|"+Gear+"~P|2|Hans|10,20,30|0|0,0,0|0|100|100|MotionIdle");m.Advance(1);
        Assert.Equal(Gear,m.Str("KCDUS.Outfits.capture(__spawned[1])"));
        Assert.Single(m.Lines("KCDUS|OUTFIT|applied|2"));
        m.Advance(1);Assert.Single(m.Lines("KCDUS|OUTFIT|applied|2"));
        m.Send("2~PD|2");Assert.True(m.Bool("KCDUS.Outfits.pending['2']==nil"));
        Assert.Equal(1,m.Num("__henry.items[1].amount"));
    }
    [Fact]
    public void UnsupportedEngineOrFailedStagingDoesNotRemoveProxyClothes()
    {
        var m=new LuaMod();m.InWorld();m.Send("1~OUT|2|"+Gear+"~P|2|Hans|10,20,30|0|0,0,0|0|100|100|MotionIdle");m.Advance(1);
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001",m.Str("__spawned[1].items[1].class"));
        m.Do("__world.engineBridge=true;__spawned[1].items[1].kcdusBridge=1;__world.failCreate=true");
        m.Advance(5);Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001",m.Str("__spawned[1].items[1].class"));
        m.Do("__world.failCreate=false");m.Advance(5);Assert.Equal(Gear,m.Str("KCDUS.Outfits.capture(__spawned[1])"));
    }
    [Fact]
    public void CaptureSortsOnlyEquipmentAndAllowsClassBeginningWithZero()
    {
        var m=new LuaMod();m.Do("__henry.items={{class='00000000-0000-0000-0000-00000000001c',health=1,kcdusBridge=1,kcdusEquipped=1},{class='aaaaaaaa-0000-0000-0000-000000000001',health=1,kcdusBridge=1,kcdusEquipped=0}}");
        Assert.Equal("1;00000000-0000-0000-0000-00000000001c,1.00000",m.Str("KCDUS.Outfits.capture(player)"));
    }
}
