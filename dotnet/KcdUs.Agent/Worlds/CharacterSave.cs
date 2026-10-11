// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
namespace KcdUs.Agent.Worlds;

public static partial class ExactTraitsSave
{
    // These are the seven fields emitted by the retail soul-core serializer.
    // World-owned soul fields (quest knowledge, companions, position) stay put.
    private static readonly Dictionary<ushort, int> CoreSizes = new()
    { [0x138B] = 24, [0x137D] = 37, [0x137F] = 4, [0x138F] = 4 };
    /// <param name="WorldStateKept">The source Henry had an ACTIVE world-referenced soul state (0x137D/0x04B0: a world actor reference and a timer, e.g. a companion
    /// or a scripted follow). Such a reference names an actor of the SOURCE world, which does not exist in another world, so it is classified as world-owned: the
    /// captured character carries an empty one, and preparing a save keeps the destination's own 0x137D untouched.</param>
    public sealed record CharacterState(Traits Traits, InventoryState Inventory,
        Dictionary<ushort, byte[]> Core, Dictionary<ushort, byte[]>? Live = null, bool WorldStateKept = false);

    // The soul's live state (Henry's 0x12FC, beside the core) that is the CHARACTER's, not the world's:
    //   0x0931  every buff instance: injuries, bleeding, poison, drunkenness, hangover, alcoholism, overeating, starvation, exhaustion, sleep and perk
    //           effects. Each record is the engine's own serialization (instance id, definition, optional override, elapsed timer in seconds), which the
    //           engine's own loader reads back (case 0x931 of the soul-state reader, FUN_1811f8d04). Timers are ELAPSED time, so a different world clock
    //           does not shorten or lengthen them. Proved: added, saved and parsed in the private engine (2026-10-11).
    //   0x0926  eight personal accumulators (0x1896..0x189D, doubles, default 0); not world-clock timestamps (all far below the world's clock).
    // World-owned live state stays from the destination: 0x0934 (memory of the world), 0x0935, 0x0936, 0x0937.
    public const ushort LiveBuffs = 0x0931, LiveAccumulators = 0x0926;
    public const int MaxBuffs = 256, MaxBuffRecordBytes = 4096;

    public static CharacterState CaptureCharacter(byte[] file)
    {
        var a = Inflate(file); var core = Chain(a.Raw)[^1];
        var fields = Children(a.Raw, core.Begin, core.End);
        var extra = CoreSizes.ToDictionary(p => p.Key, p => Bytes(a.Raw, One(fields, p.Key)));
        bool worldKept = WorldReferenceActive(extra);
        if (worldKept) extra[0x137D] = EmptyWorldState(extra[0x137D]);
        ValidateCore(extra);
        var chain = Chain(a.Raw); var state = chain[^2];
        var stateFields = Children(a.Raw, state.Begin, state.End);
        // Older or synthetic streams without the live fields carry no live state (the destination's stays).
        Dictionary<ushort, byte[]>? live = null;
        if (stateFields.Any(f => f.Tag == LiveBuffs) && stateFields.Any(f => f.Tag == LiveAccumulators))
        {
            live = new() { [LiveBuffs] = Bytes(a.Raw, One(stateFields, LiveBuffs)), [LiveAccumulators] = Bytes(a.Raw, One(stateFields, LiveAccumulators)) };
            ValidateLive(live);
        }
        return new(Capture(file), CaptureInventory(file), extra, live, worldKept);
    }

    private static bool WorldReferenceActive(Dictionary<ushort, byte[]> core)
    {
        if (!core.TryGetValue(0x137D, out var b) || b.Length != CoreSizes[0x137D]) return false;     // framing is judged by ValidateCore
        var state = Children(b, 0, b.Length).Where(n => n.Tag == 0x04B0).ToArray();
        return state.Length == 1 && state[0].Length == 31 && b.AsSpan(state[0].Begin, state[0].Length).IndexOfAnyExcept((byte)0) >= 0;
    }

    // the same framing with the 0x04B0 payload empty (the observed inactive state)
    private static byte[] EmptyWorldState(byte[] b)
    {
        var copy = b.ToArray();
        var state = One(Children(copy, 0, copy.Length), 0x04B0);
        copy.AsSpan(state.Begin, state.Length).Clear();
        return copy;
    }

    /// <summary>The buff list and the personal accumulators, checked field by field before they can enter another world's save.</summary>
    public static void ValidateLive(Dictionary<ushort, byte[]> live)
    {
        if (!live.TryGetValue(LiveBuffs, out var buffs) || !live.TryGetValue(LiveAccumulators, out var acc) || live.Count != 2)
            throw new InvalidDataException("Live character state framing differs.");
        if (buffs.Length < 4 || buffs.Length > MaxBuffs * (MaxBuffRecordBytes + 6) + 4) throw new InvalidDataException("Buff list exceeds bounds.");
        var records = Children(buffs, 4, buffs.Length);
        if (Int(buffs, 0) != records.Count || records.Count > MaxBuffs) throw new InvalidDataException("Buff count differs from its records.");
        var seen = new HashSet<Guid>();
        foreach (var r in records)
        {
            if (r.Tag != 0x0807 || r.Length < 32 || r.Length > MaxBuffRecordBytes) throw new InvalidDataException("Unknown buff record framing.");
            var instance = new Guid(buffs.AsSpan(r.Begin, 16));
            if (instance == Guid.Empty || !seen.Add(instance)) throw new InvalidDataException("Buff instance identity missing or duplicated.");
            if (new Guid(buffs.AsSpan(r.Begin + 16, 16)) == Guid.Empty) throw new InvalidDataException("Buff definition missing.");
            var fields = Children(buffs, r.Begin + 32, r.End);
            if (fields.Count(f => f.Tag == 0x94E7) != 1 || One(fields, 0x94E7).Length != 16) throw new InvalidDataException("Buff override framing differs.");
            foreach (var f in fields.Where(f => f.Tag == 0x0002))
                if (f.Length != 8 || !double.IsFinite(BitConverter.ToDouble(buffs, f.Begin)) || BitConverter.ToDouble(buffs, f.Begin) < 0)
                    throw new InvalidDataException("Buff timer is not a finite elapsed time.");
        }
        var counters = Children(acc, 0, acc.Length);
        if (counters.Count > 8 || counters.Any(c => c.Tag is < 0x1896 or > 0x189D || c.Length != 8 || !double.IsFinite(BitConverter.ToDouble(acc, c.Begin)))
            || counters.Select(c => c.Tag).Distinct().Count() != counters.Count) throw new InvalidDataException("Personal accumulator framing differs.");
    }

    /// <summary>The definition of each buff instance (readback: the engine may drop expired ones and add place-dependent ones on load).</summary>
    public static IReadOnlyList<Guid> BuffDefinitions(byte[] buffs) =>
        Children(buffs, 4, buffs.Length).Where(r => r.Tag == 0x0807 && r.Length >= 32).Select(r => new Guid(buffs.AsSpan(r.Begin + 16, 16))).ToList();

    // Replaces fields of Henry's soul state (0x12FC) and fixes every enclosing length.
    private static byte[] ReplaceStateFields(byte[] file, Dictionary<ushort, byte[]> replacements)
    {
        var a = Inflate(file); var chain = Chain(a.Raw); var state = chain[^2];
        var fields = Children(a.Raw, state.Begin, state.End);
        foreach (ushort tag in replacements.Keys) _ = One(fields, tag);
        using var payload = new MemoryStream();
        foreach (var f in fields)
        {
            if (!replacements.TryGetValue(f.Tag, out var value)) payload.Write(a.Raw, f.Off, f.Length + 6);
            else
            {
                var h = new byte[6]; BinaryPrimitives.WriteUInt16LittleEndian(h, f.Tag); Put(h, 2, value.Length);
                payload.Write(h); payload.Write(value);
            }
        }
        var bytes = payload.ToArray(); int delta = bytes.Length - state.Length, length = checked(a.Raw.Length + delta);
        if (length > MaxRawBytes) throw new InvalidDataException("Prepared stream exceeds bounds.");
        var raw = new byte[length]; a.Raw.AsSpan(0, state.Begin).CopyTo(raw); bytes.CopyTo(raw.AsSpan(state.Begin));
        a.Raw.AsSpan(state.End).CopyTo(raw.AsSpan(state.Begin + bytes.Length));
        foreach (var n in chain.Take(chain.Count - 1)) Put(raw, n.Off + 2, checked(n.Length + delta));   // the core (chain[^1]) lies inside the state
        return Encode(new(raw, a.Footer));
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
            if (tag == 0x137D && character.WorldStateKept) continue;                  // world-owned: the destination's stays
            var field = One(fields, tag);
            if (field.Length != value.Length) throw new InvalidDataException("Destination character core framing differs.");
            value.CopyTo(raw, field.Begin);
        }
        var result = Encode(new(raw, a.Footer));
        if (character.Live is { } live)
        {
            ValidateLive(live);
            result = ReplaceStateFields(result, live);
            var readback = CaptureCharacter(result);
            if (readback.Live is null || !readback.Live[LiveBuffs].SequenceEqual(live[LiveBuffs]) || !readback.Live[LiveAccumulators].SequenceEqual(live[LiveAccumulators])
                || !readback.Traits.Perks.SequenceEqual(character.Traits.Perks)) throw new InvalidDataException("Prepared live state readback differs.");
        }
        return result;
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
            && (expected.WorldStateKept || actual.WorldStateKept || expected.Core[0x137D].SequenceEqual(actual.Core[0x137D]));
        // Health/stamina/food/energy naturally change while the acknowledgement save is written.
    }
}
