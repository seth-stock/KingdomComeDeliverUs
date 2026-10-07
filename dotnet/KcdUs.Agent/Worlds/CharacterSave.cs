// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Agent.Worlds;

public static partial class ExactTraitsSave
{
    // These are the seven fields emitted by the retail soul-core serializer.
    // World-owned soul fields (quest knowledge, companions, position) stay put.
    private static readonly Dictionary<ushort, int> CoreSizes = new()
    { [0x138B] = 24, [0x137D] = 37, [0x137F] = 4, [0x138F] = 4 };
    public sealed record CharacterState(Traits Traits, InventoryState Inventory,
        Dictionary<ushort, byte[]> Core);

    public static CharacterState CaptureCharacter(byte[] file)
    {
        var a = Inflate(file); var core = Chain(a.Raw)[^1];
        var fields = Children(a.Raw, core.Begin, core.End);
        var extra = CoreSizes.ToDictionary(p => p.Key, p => Bytes(a.Raw, One(fields, p.Key)));
        ValidateCore(extra);
        return new(Capture(file), CaptureInventory(file), extra);
    }

    private static void ValidateCore(Dictionary<ushort, byte[]> core)
    {
        if (core.Count != CoreSizes.Count || CoreSizes.Any(p => !core.TryGetValue(p.Key, out var b) || b.Length != p.Value))
            throw new InvalidDataException("Unsupported character core framing.");
        foreach (var tag in new ushort[] { 0x138B, 0x137F, 0x138F })
            for (int at = 0; at < core[tag].Length; at += 4)
                if (!float.IsFinite(BitConverter.Int32BitsToSingle(Int(core[tag], at))))
                    throw new InvalidDataException("Non-finite character state.");
        // 137D is a 4B0 record containing a world actor reference and timer,
        // not a simple ability bitset. Only the observed empty state is portable.
        var referenced = Children(core[0x137D], 0, core[0x137D].Length);
        var state = One(referenced, 0x04B0);
        if (state.Length != 31 || core[0x137D].AsSpan(state.Begin, state.Length).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Character has an active world-referenced soul state; finish it before moving Henry.");
    }

    public static byte[] PrepareCharacter(byte[] destination, CharacterState character)
    {
        ValidateCore(character.Core);
        var prepared = PrepareInventory(Prepare(destination, character.Traits), character.Inventory);
        var a = Inflate(prepared); var chain = Chain(a.Raw); var core = chain[^1];
        var fields = Children(a.Raw, core.Begin, core.End);
        var raw = a.Raw.ToArray();
        foreach (var (tag, value) in character.Core)
        {
            var field = One(fields, tag);
            if (field.Length != value.Length) throw new InvalidDataException("Destination character core framing differs.");
            value.CopyTo(raw, field.Begin);
        }
        return Encode(new(raw, a.Footer));
    }

    public static bool SameProgressionAndInventory(CharacterState expected, CharacterState actual)
    {
        // Sparse zero XP serializes differently after a native load; story is world-owned.
        var e = Xp(expected.Traits.Stats); var a = Xp(actual.Traits.Stats);
        e.Remove(8); a.Remove(8);
        foreach (var key in e.Where(p => p.Value == 0).Select(p => p.Key).ToArray()) e.Remove(key);
        foreach (var key in a.Where(p => p.Value == 0).Select(p => p.Key).ToArray()) a.Remove(key);
        return e.SequenceEqual(a)
            && Xp(expected.Traits.Skills).Where(p => p.Value != 0).SequenceEqual(Xp(actual.Traits.Skills).Where(p => p.Value != 0))
            && expected.Traits.Perks.SequenceEqual(actual.Traits.Perks)
            && SameInventory(expected.Inventory, actual.Inventory)
            && expected.Core[0x137D].SequenceEqual(actual.Core[0x137D]);
        // Health/stamina/food/energy naturally change while the acknowledgement save is written.
    }
}
