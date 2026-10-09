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

    private static LuaMod TransferRig()
    {
        var m=new LuaMod();
        m.Do("ident=string.rep('a',32);calls=0;rows={[1]={kcdusBridge=1,kcdusPersistent=ident,kcdusOwner='old',kcdusEquipped=0,class='class-a',amount=3,health=0.73}};"+
            "ItemManager.GetItem=function(id) return rows[id] end;si={1};di={};src={GetInventoryTable=function() return si end};dst={GetInventoryTable=function() return di end,AddItem=function(self,id) calls=calls+1;si={};di={id};local old=rows[id];rows[id]={kcdusBridge=1,kcdusPersistent=old.kcdusPersistent,kcdusOwner='new',kcdusEquipped=0,class=old.class,amount=old.amount,health=old.health} end}");
        return m;
    }
    [Fact]
    public void WholeInstanceMoveUsesOneNativeAddAndPreservesMetadata()
    {
        var m=TransferRig();m.Do("accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("accepted and reason=='applied' and #si==0 and #di==1"));Assert.Equal(1,m.Num("calls"));
    }
    [Fact]
    public void MergeAndEquippedItemsAreRefusedBeforeMutation()
    {
        var m=TransferRig();m.Do("rows[2]={kcdusBridge=1,kcdusPersistent=string.rep('b',32),class='class-a'};di={2};accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("not accepted and reason=='merge-not-supported'"));Assert.Equal(0,m.Num("calls"));
        m.Do("di={};rows[1].kcdusEquipped=1;accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("not accepted and reason=='equipped'"));Assert.Equal(0,m.Num("calls"));
    }
    [Fact]
    public void InertAndPartialThenFaultedMovesAreUncertain()
    {
        var m=TransferRig();m.Do("dst.AddItem=function() calls=calls+1 end;accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("not accepted and reason=='uncertain'"));Assert.Equal(1,m.Num("calls"));
        m=TransferRig();m.Do("local real=dst.AddItem;dst.AddItem=function(self,id) real(self,id);error('native changed then faulted') end;accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("not accepted and reason=='uncertain' and #si==0 and #di==1"));Assert.Equal(1,m.Num("calls"));
    }
    [Fact]
    public void UnsupportedOwnershipOrAmbiguousSourceCannotMove()
    {
        var m=TransferRig();m.Do("rows[1].kcdusOwner=nil;accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("not accepted and reason=='source-unverified'"));Assert.Equal(0,m.Num("calls"));
        m=TransferRig();m.Do("rows[2]=rows[1];si={1,2};accepted,reason=KCDUS.NativeIdentity.transferInstance(src,dst,ident)");
        Assert.True(m.Bool("not accepted and reason=='source-unverified'"));Assert.Equal(0,m.Num("calls"));
    }
}
