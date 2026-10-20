namespace KcdUs.Tests;

public class LocomotionTests
{
    private static LuaMod Walking()
    {
        var m=new LuaMod();m.InWorld();
        m.Send("1~P|2|Hans|10,20,30|0|0,1.5,0|0|100|100|MotionMovement");m.Advance(0.4);
        m.Send("2~P|2|Hans|10,20,30|0|0,1.5,0|0|100|100|MotionMovement");m.Advance(0.2);return m;
    }
    [Fact]
    public void ReusedEntityHandleDoesNotDeleteOrMoveTheWorldNpc()
    {
        var m=Walking();m.Do("__spawned[1].name='rat_guard';__spawned[1].moves=0");
        m.Advance(0.6);
        Assert.Equal(0,m.Num("__spawned[1].moves"));
        m.Send("3~PD|2");Assert.True(m.Bool("__entities[__spawned[1].id]~=nil"));
        Assert.Equal(1,m.Num("#__removed")); // only the newly created owned proxy
    }
    [Fact]
    public void LoadDiscardsPeerHandlesWithoutRemovingActorsFromTheNewWorld()
    {
        var m=Walking();m.Do("Player.OnLoad(player,{})");
        Assert.Equal(0,m.Num("KCDUS.Ghosts.count()"));Assert.Equal(0,m.Num("#__removed"));
        m.Send("3~P|2|Hans|10,20,30|0|0,1.5,0|0|100|100|MotionMovement");m.Advance(0.6);
        Assert.Equal(2,m.Num("#__spawned"));
    }
    [Fact]
    public void MovingNpcUsesLoopingFullBodyLayerAndDoesNotRestartEveryTick()
    {
        var m=Walking();Assert.Equal("NPC",m.Str("__spawned[1].class"));
        Assert.Equal("relaxed_walk_medium",m.Str("__spawned[1].animation.clip"));
        Assert.True(m.Bool("__spawned[1].animation.loop and __spawned[1].flags.noSave"));
        m.Advance(0.15);Assert.Equal(1,m.Num("__spawned[1].animationStarts"));
    }
    [Fact]
    public void AStaminaDropWithTheSwordOutIsShownAsOneSwingThenWalkingResumes()
    {
        var m=new LuaMod();m.InWorld();
        m.Send("1~P|2|Hans|10,20,30|0|0,0,0|1|100|100|MotionIdle");m.Advance(0.3);
        m.Send("2~P|2|Hans|10,20,30|0|0,0,0|1|100|88|MotionIdle");m.Advance(0.4);
        Assert.Equal("combat_lg_z0_natksw_lngsw",m.Str("__spawned[1].animation.clip"));
        Assert.False(m.Bool("__spawned[1].animation.loop"));
        Assert.Single(m.Lines("KCDUS|SWING|2|1"));
        m.Send("3~P|2|Hans|10,20,30|0|0,0,0|1|100|88|MotionIdle");m.Advance(0.3);
        Assert.Equal("combat_lg_z0_natksw_lngsw",m.Str("__spawned[1].animation.clip"));   // still the same swing, not restarted
        m.Send("4~P|2|Hans|10,20,30|0|0,0,0|1|100|88|MotionIdle");m.Advance(0.8);
        Assert.Equal("relaxed_idle_both",m.Str("__spawned[1].animation.clip"));
    }
    [Fact]
    public void NoSwingWithTheSwordAwayOrOnASmallStaminaStep()
    {
        var m=new LuaMod();m.InWorld();
        m.Send("1~P|2|Hans|10,20,30|0|0,0,0|0|100|100|MotionIdle");m.Advance(0.3);
        m.Send("2~P|2|Hans|10,20,30|0|0,0,0|0|100|80|MotionIdle");m.Advance(0.2);
        m.Send("3~P|2|Hans|10,20,30|0|0,0,0|1|100|79|MotionIdle");m.Advance(0.2);
        m.Send("4~P|2|Hans|10,20,30|0|0,0,0|1|100|77|MotionIdle");m.Advance(0.2);
        Assert.Empty(m.Lines("KCDUS|SWING"));
    }
    [Fact]
    public void AStaminaChangeAcrossALongPacketGapIsNotAnAttack()
    {
        var m=new LuaMod();m.InWorld();
        m.Send("1~P|2|Hans|10,20,30|0|0,0,0|1|100|100|MotionIdle");m.Advance(1.2);
        m.Send("2~P|2|Hans|10,20,30|0|0,0,0|1|100|50|MotionIdle");m.Advance(0.2);
        Assert.Empty(m.Lines("KCDUS|SWING"));
    }
    [Fact]
    public void StaleMovementReturnsToIdleBeforePeerTimeout()
    {
        var m=Walking();m.Advance(0.6);Assert.Equal("relaxed_idle_both",m.Str("__spawned[1].animation.clip"));
    }
    [Fact]
    public void PositionChangesAnimateWhenVelocityIsUnavailableButTeleportsDoNot()
    {
        var m=Walking();m.Advance(0.1);
        m.Send("2~P|2|Hans|10,20.3,30|0|0,0,0|0|100|100|MotionMovement");m.Advance(0.1);
        Assert.Contains("walk",m.Str("__spawned[1].animation.clip"));
        m.Send("3~P|2|Hans|1000,20,30|0|0,0,0|0|100|100|MotionIdle");m.Advance(0.1);
        Assert.Equal("relaxed_idle_both",m.Str("__spawned[1].animation.clip"));
    }
    [Fact]
    public void FailedAnimationRetriesWithoutDeclaringItApplied()
    {
        var m=new LuaMod();m.InWorld();m.Do("__world.failAnimation=true");
        m.Send("1~P|2|Hans|10,20,30|0|0,1.5,0|0|100|100|MotionMovement");m.Advance(0.4);
        m.Send("2~P|2|Hans|10,20,30|0|0,1.5,0|0|100|100|MotionMovement");m.Advance(0.2);
        Assert.True(m.Bool("KCDUS.Ghosts.list['2'].clip==nil"));
        m.Do("__world.failAnimation=false");m.Advance(0.8);
        m.Send("3~P|2|Hans|10,20,30|0|0,1.5,0|0|100|100|MotionMovement");m.Advance(0.3);
        Assert.Equal("relaxed_walk_medium",m.Str("__spawned[1].animation.clip"));
    }
}
