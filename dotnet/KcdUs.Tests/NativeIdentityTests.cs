// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Tests;
public class NativeIdentityTests
{
    [Fact]
    public void UnsupportedAdapterAndMalformedIdentityDoNotResolve()
    {
        var m=new LuaMod();
        m.Do("e={soul={GetId=function() return 5 end}}; ItemManager.GetItem=function() return {kcdusPersistent=string.rep('a',32)} end");
        Assert.True(m.Bool("KCDUS.NativeIdentity.soul(e)==nil"));
        m.Do("ItemManager.GetItem=function() return {kcdusSoul=1,kcdusPersistent=string.rep('0',32)} end");
        Assert.True(m.Bool("KCDUS.NativeIdentity.soul(e)==nil"));
        Assert.True(m.Bool("KCDUS.NativeIdentity.findActor('bad|identity')==nil"));
    }
    [Fact]
    public void ReusedEntityHandleCannotResolveAsThePreviousWorldActor()
    {
        var m=new LuaMod();
        m.Do("ident=string.rep('a',32); e={id=12,soul={GetId=function() return 5 end}}; ItemManager.GetItem=function() return {kcdusSoul=1,kcdusPersistent=ident} end; System.GetEntitiesByClass=function() return {e} end; __entities[12]=e");
        Assert.True(m.Bool("KCDUS.NativeIdentity.findActor(string.rep('a',32))==e"));
        m.Do("ident=string.rep('b',32)");
        Assert.True(m.Bool("KCDUS.NativeIdentity.findActor(string.rep('a',32))==nil"));
        Assert.True(m.Bool("KCDUS.NativeIdentity.findActor(string.rep('b',32))==e"));
        m.Do("KCDUS.NativeIdentity.clear()");
        Assert.Equal(0,m.Num("#KCDUS.NativeIdentity.actors"));
    }
    [Fact]
    public void AmbiguousActorOrItemIdentityIsRefused()
    {
        var m=new LuaMod();
        m.Do("ident=string.rep('a',32); e={id=12,soul={GetId=function() return 5 end}}; f={id=13,soul=e.soul}; System.GetEntitiesByClass=function() return {e,f} end; ItemManager.GetItem=function() return {kcdusSoul=1,kcdusBridge=1,kcdusPersistent=ident} end; inv={GetInventoryTable=function() return {1,2} end}");
        Assert.True(m.Bool("KCDUS.NativeIdentity.findActor(ident)==nil"));
        Assert.True(m.Bool("KCDUS.NativeIdentity.findItem(inv,ident)==nil"));
    }
}
