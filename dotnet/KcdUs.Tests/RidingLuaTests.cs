namespace KcdUs.Tests;
public class RidingLuaTests
{
    private const string Walking = "P|2|Hans|10,20,30|0|0,0,0|0|100|100|MotionIdle";
    private const string Riding = "P|2|Hans|10,20,30|0|0,0,0|2|100|100|MotionIdle";
    [Fact]
    public void MyHorseIsReportedWhenMountedAndClearedWhenNot()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("__world.horse=__mkNpc('Pebble',100,5,5,5,'Horse'); __world.mounted=true");
        m.Advance(1);
        Assert.Single(m.Lines("KCDUS|HRS|Pebble"));
        m.Do("__world.mounted=false"); m.Advance(1);
        Assert.Contains("KCDUS|HRS|", m.Log());
    }
    [Fact]
    public void AFriendOnAHorseGetsThatHorseCarriedAndSeated()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("__horse=__mkNpc('Pebble',100,500,500,5,'Horse')");
        m.Send("1~HORSE|2|Pebble~" + Riding); m.Advance(1);
        Assert.Single(m.Lines("KCDUS|RIDING|taken|2|Pebble"));
        Assert.False(m.Bool("__horse.brain"));
        Assert.True(m.Num("math.abs(__horse.pos.x-10)") < 1);
        Assert.True(m.Bool("__spawned[1].human.IsMounted()"));
        m.Advance(3); Assert.True(m.Num("__spawned[1].mountCalls") == 1);   // seated once, not every tick
    }
    [Fact]
    public void WhenTheFriendDismountsTheHorseGetsItsBrainBack()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("__horse=__mkNpc('Pebble',100,500,500,5,'Horse')");
        m.Send("1~HORSE|2|Pebble~" + Riding); m.Advance(1);
        m.Send("2~" + Walking); m.Advance(1);
        Assert.True(m.Bool("__horse.brain"));
        Assert.False(m.Bool("__spawned[1].human.IsMounted()"));
        Assert.Single(m.Lines("KCDUS|RIDING|released|2"));
    }
    [Fact]
    public void ALeavingFriendReleasesTheHorseAndAnUnsafeNameIsIgnored()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("__horse=__mkNpc('Pebble',100,500,500,5,'Horse')");
        m.Send("1~HORSE|2|Pebble~" + Riding); m.Advance(1);
        m.Send("2~PD|2"); Assert.True(m.Bool("__horse.brain"));
        m.Send("3~HORSE|3|a;b'c~P|3|Jan|10,20,30|0|0,0,0|2|100|100|MotionIdle"); m.Advance(1);
        Assert.Empty(m.Lines("KCDUS|RIDING|taken|3"));
    }
    [Fact]
    public void ARidersOwnHorseOrADeadOrUnknownHorseIsNeverTaken()
    {
        var m = new LuaMod(); m.InWorld();
        m.Do("__horse=__mkNpc('Pebble',100,500,500,5,'Horse'); __world.horse=__horse; __world.mounted=true");
        m.Send("1~HORSE|2|Pebble~" + Riding); m.Advance(1);
        Assert.True(m.Bool("__horse.brain"));
        m.Do("__world.mounted=false; __horse.dead=true"); m.Advance(1);
        Assert.True(m.Bool("__horse.brain"));
        m.Send("2~HORSE|2|NoSuchHorse"); m.Advance(1);
        Assert.Empty(m.Lines("KCDUS|RIDING|taken|2|NoSuchHorse"));
    }
}
