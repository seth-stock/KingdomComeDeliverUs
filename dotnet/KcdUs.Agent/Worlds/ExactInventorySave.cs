// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
namespace KcdUs.Agent.Worlds;

public static partial class ExactTraitsSave
{
    /// <summary>Opaque native inventory record. Includes instance identity,
    /// equipment flags, condition, amounts and item extension records.</summary>
    public sealed record InventoryState(byte[] Payload);
    private sealed record ParsedInventory(Guid Owner,Guid Identity,List<Node> Items);
    private static List<Node> InventoryChain(byte[] raw)
    {
        var chain=new List<Node>();var fields=Children(raw,4,raw.Length-1);
        foreach(ushort tag in new ushort[]{0x1f4,0x1f6,0x7303,0x0bba})
        {
            var n=One(fields,tag);chain.Add(n);
            if(tag!=0x0bba)fields=Children(raw,n.Begin,n.End);
        }
        var root=chain[^1];if(root.Length<4)throw new InvalidDataException("Missing inventory count.");
        var inventories=Children(raw,root.Begin+4,root.End);
        if(Int(raw,root.Begin)!=inventories.Count || inventories.Any(n=>n.Tag!=0x1e66 || n.Length<16))
            throw new InvalidDataException("Inventory list framing differs.");
        var identities=new HashSet<Guid>();
        foreach(var n in inventories)
            if(!identities.Add(new Guid(raw.AsSpan(n.Begin,16))))throw new InvalidDataException("Duplicate inventory identity.");
        var hero=inventories.Where(n=>new Guid(raw.AsSpan(n.Begin,16))==Henry).ToArray();
        if(hero.Length!=1)throw new InvalidDataException("Canonical Henry inventory absent or duplicated.");
        chain.Add(hero[0]);return chain;
    }
    private static (byte Kind,Guid Id) InventoryReference(byte[] b,ref int at,int end)
    {
        if(end-at<18)throw new InvalidDataException("Truncated inventory reference.");
        byte present=b[at],kind=b[at+1];var id=new Guid(b.AsSpan(at+2,16));at+=18;
        bool valid=present==0 && ((kind==0 && id==Guid.Empty) || (kind==4 && id!=Guid.Empty))
            || present==1 && (kind==3 || kind==5) && id!=Guid.Empty;
        if(!valid)
            throw new InvalidDataException("Unsupported inventory reference.");
        return(kind,id);
    }
    private static ParsedInventory ParseInventory(byte[] b,int start,int end,bool hero)
    {
        if(end-start<16)throw new InvalidDataException("Truncated inventory identity.");
        var owner=new Guid(b.AsSpan(start,16));
        // Native NO_SAVE inventories can leave only their outer identity.
        if(end-start==16 && !hero)return new(owner,owner,new());
        if(end-start<93)throw new InvalidDataException("Inventory header differs.");
        var identity=new Guid(b.AsSpan(start+16,16));int at=start+32;
        var current=InventoryReference(b,ref at,end);var original=InventoryReference(b,ref at,end);
        byte virtualInventory=b[at++];at+=24; // native flag, loan time and preset GUID
        if(virtualInventory>1)throw new InvalidDataException("Inventory mode differs.");
        int nameStart=at;
        while(at<end && at-nameStart<=128 && b[at]!=0)at++;
        if(at>=end || at-nameStart>128)throw new InvalidDataException("Inventory name is truncated or too long.");
        if(hero && (owner!=Henry || identity!=Henry || current!=(5,Henry) || original!=(5,Henry)
            || virtualInventory!=0 || !b.AsSpan(nameStart,at-nameStart).SequenceEqual("Dude"u8)))
            throw new InvalidDataException("Henry inventory ownership differs.");
        at++;
        if(virtualInventory==1)
        {
            if(at!=end)throw new InvalidDataException("Virtual inventory has unexpected data.");
            return new(owner,identity,new());
        }
        if(end-at<4)throw new InvalidDataException("Inventory count absent.");
        int count=Int(b,at);at+=4;
        if(count<0 || count>4096)throw new InvalidDataException("Inventory exceeds the supported item bound.");
        var items=Children(b,at,end);if(items.Count!=count)throw new InvalidDataException("Inventory item count differs.");
        var seen=new HashSet<Guid>();
        foreach(var item in items)
        {
            if(item.Tag!=0x030a || item.Length<22)throw new InvalidDataException("Inventory item framing differs.");
            var id=new Guid(b.AsSpan(item.Begin,16));
            if(id==Guid.Empty || !seen.Add(id))throw new InvalidDataException("Empty/duplicate item instance identity.");
            var state=One(Children(b,item.Begin+16,item.End),0x0630);
            if(state.Length!=74)throw new InvalidDataException("Only the observed native item state is supported.");
            if(new Guid(b.AsSpan(state.Begin,16))==Guid.Empty)throw new InvalidDataException("Item class is empty.");
            int amount=Int(b,state.Begin+16);float health=BitConverter.Int32BitsToSingle(Int(b,state.Begin+20));
            if(amount<1 || amount>10_000_000 || !float.IsFinite(health) || health<0 || health>1)
                throw new InvalidDataException("Item amount/condition differs.");
            int reference=state.End-18;var holder=InventoryReference(b,ref reference,state.End);
            if(holder!=(3,identity))throw new InvalidDataException("Item is not owned by its containing inventory.");
        }
        return new(owner,identity,items);
    }
    public static InventoryState CaptureInventory(byte[] file)
    {
        var a=Inflate(file);var hero=InventoryChain(a.Raw)[^1];
        _=ParseInventory(a.Raw,hero.Begin,hero.End,true);
        return new(Bytes(a.Raw,hero));
    }
    public static byte[] PrepareInventory(byte[] destination,InventoryState inventory)
    {
        if(inventory.Payload.Length>4*1024*1024)throw new InvalidDataException("Inventory snapshot exceeds bounds.");
        var desired=ParseInventory(inventory.Payload,0,inventory.Payload.Length,true);
        var sourceIds=desired.Items.Select(n=>new Guid(inventory.Payload.AsSpan(n.Begin,16))).ToHashSet();
        var a=Inflate(destination);var chain=InventoryChain(a.Raw);var hero=chain[^1];var root=chain[^2];
        _=ParseInventory(a.Raw,hero.Begin,hero.End,true);
        foreach(var other in Children(a.Raw,root.Begin+4,root.End).Where(n=>n.Off!=hero.Off))
        {
            var parsed=ParseInventory(a.Raw,other.Begin,other.End,false);
            foreach(var item in parsed.Items)
                if(sourceIds.Contains(new Guid(a.Raw.AsSpan(item.Begin,16))))
                    throw new InvalidDataException("A carried item instance belongs to another inventory in this world; refusing duplication.");
        }
        int delta=inventory.Payload.Length-hero.Length,length=checked(a.Raw.Length+delta);
        if(length>MaxRawBytes)throw new InvalidDataException("Prepared stream exceeds bounds.");
        var raw=new byte[length];a.Raw.AsSpan(0,hero.Begin).CopyTo(raw);inventory.Payload.CopyTo(raw,hero.Begin);
        a.Raw.AsSpan(hero.End).CopyTo(raw.AsSpan(hero.Begin+inventory.Payload.Length));
        foreach(var node in chain)Put(raw,node.Off+2,checked(node.Length+delta));
        var result=Encode(new(raw,a.Footer));
        if(!CaptureInventory(result).Payload.SequenceEqual(inventory.Payload))throw new InvalidDataException("Prepared inventory readback differs.");
        return result;
    }
}
