using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KcdUs.Agent.Worlds;
namespace KcdUs.Tests;
public class ExactInventoryTests
{
    private static readonly Guid Hero=ExactTraitsSave.Henry;
    private static readonly Guid Item=new("10000000-1111-2222-3333-444444444444");
    private static byte[] Join(params byte[][] b)=>b.SelectMany(x=>x).ToArray();
    private static byte[] I(int i)=>BitConverter.GetBytes(i);
    private static byte[] T(ushort tag,byte[] p)=>Join(BitConverter.GetBytes(tag),I(p.Length),p);
    private static byte[] Ref(byte kind,Guid id)=>Join(new byte[]{1,kind},id.ToByteArray());
    private static byte[] Inventory(Guid owner,Guid item,int count,float health=0.5f)
    {
        var state=new byte[74];new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToByteArray().CopyTo(state,0);
        I(count).CopyTo(state,16);BitConverter.GetBytes(health).CopyTo(state,20);I(1).CopyTo(state,24);
        Ref(3,owner).CopyTo(state,56);
        var extension=T(0x9999,new byte[]{0x72,0x41,0x11});
        var records=T(0x030a,Join(item.ToByteArray(),T(0x0630,state),extension));
        return Join(owner.ToByteArray(),owner.ToByteArray(),Ref(5,owner),Ref(5,owner),new byte[25],Encoding.ASCII.GetBytes(owner==Hero?"Dude\0":"NPC\0"),I(1),records);
    }
    private static byte[] Save(params byte[][] inventories)
    {
        var list=Join(I(inventories.Length),inventories.Select(x=>T(0x1e66,x)).SelectMany(x=>x).ToArray());
        var raw=Join(I(20),T(0x1f4,T(0x1f6,T(0x7303,T(0x0bba,list)))),T(0x1fd,Array.Empty<byte>()),new byte[1]);
        using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Optimal,true))z.Write(raw);
        var payload=packed.ToArray();var footer=new byte[64];"0XBP"u8.CopyTo(footer);
        var file=Join(I(payload.Length),I(raw.Length),payload,footer);MD5.HashData(file).CopyTo(file,file.Length-60);return file;
    }
    [Fact]
    public void NativeInventoryReplacesExtrasAmountsConditionEquipmentAndExtensionsExactly()
    {
        var source=Save(Inventory(Hero,Item,1));var destination=Save(Inventory(Hero,Guid.NewGuid(),22,1));
        var original=destination.ToArray();var state=ExactTraitsSave.CaptureInventory(source);
        var prepared=ExactTraitsSave.PrepareInventory(destination,state);
        Assert.Equal(state.Payload,ExactTraitsSave.CaptureInventory(prepared).Payload);
        Assert.Equal(original,destination);
        Assert.Equal(prepared,ExactTraitsSave.PrepareInventory(prepared,state));
    }
    [Fact]
    public void ReadbackAllowsOnlyNativeLinkedListOrderChanges()
    {
        // Build two valid records by combining the parsed single-item fixture
        // headers and changing the item count; their metadata stays distinct.
        var first = ExactTraitsSave.CaptureInventory(Save(Inventory(Hero, Item, 1))).Payload;
        var secondId = new Guid("20000000-1111-2222-3333-444444444444");
        var second = ExactTraitsSave.CaptureInventory(Save(Inventory(Hero, secondId, 2))).Payload;
        int header = 102; var prefix = first[..header]; I(2).CopyTo(prefix, header-4);
        var a = new ExactTraitsSave.InventoryState(Join(prefix, first[header..], second[header..]));
        var b = new ExactTraitsSave.InventoryState(Join(prefix, second[header..], first[header..]));
        Assert.True(ExactTraitsSave.SameInventory(a,b));
        var different = ExactTraitsSave.CaptureInventory(Save(Inventory(Hero, secondId, 3))).Payload;
        var changed = new ExactTraitsSave.InventoryState(Join(prefix, different[header..], first[header..]));
        Assert.False(ExactTraitsSave.SameInventory(a, changed));
    }
    [Fact]
    public void InstanceAlreadyOwnedByAnotherWorldInventoryIsRefused()
    {
        var source=Save(Inventory(Hero,Item,1));
        var destination=Save(Inventory(Hero,Guid.NewGuid(),1),Inventory(Guid.NewGuid(),Item,1));
        var original=destination.ToArray();
        Assert.Throws<InvalidDataException>(()=>ExactTraitsSave.PrepareInventory(destination,ExactTraitsSave.CaptureInventory(source)));
        Assert.Equal(original,destination);
    }
    [Fact]
    public void UnrelatedAndNonpersistentInventoriesDoNotPreventPreparation()
    {
        var source=Save(Inventory(Hero,Item,1));
        var destination=Save(Inventory(Hero,Guid.NewGuid(),1),Inventory(Guid.NewGuid(),Guid.NewGuid(),1),Guid.NewGuid().ToByteArray());
        var prepared=ExactTraitsSave.PrepareInventory(destination,ExactTraitsSave.CaptureInventory(source));
        Assert.Equal(ExactTraitsSave.CaptureInventory(source).Payload,ExactTraitsSave.CaptureInventory(prepared).Payload);
    }
    [Theory]
    [InlineData(0,0.5f)]
    [InlineData(1,1.1f)]
    [InlineData(1,float.NaN)]
    public void InvalidItemDataIsRefused(int count,float condition)
    {
        Assert.Throws<InvalidDataException>(()=>ExactTraitsSave.CaptureInventory(Save(Inventory(Hero,Item,count,condition))));
    }
    [Fact]
    public void InventoryOwnedByAnotherActorCannotMasqueradeAsHenry()
    {
        var bad=Inventory(Hero,Item,1);Ref(5,Guid.NewGuid()).CopyTo(bad,32);
        Assert.Throws<InvalidDataException>(()=>ExactTraitsSave.CaptureInventory(Save(bad)));
    }
}
