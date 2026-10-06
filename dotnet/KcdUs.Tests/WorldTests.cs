// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using KcdUs.Agent.Worlds;
using Xunit;

namespace KcdUs.Tests;

/// <summary>Shared worlds (docs/SHARED-WORLDS.md): reading a save's description, the playline store, who wins a reconnect, and the file transfer. (synthetic saves shaped like the real ones)</summary>
public class WorldTests
{
    /// <summary>A save file in the real framing: blocks of [compressed length][inflated length][zlib], then a 64-byte footer that begins 0XBP.</summary>
    internal static byte[] FakeSave(string desc, int extraBlocks = 2, int seed = 1)
    {
        var o = new MemoryStream();
        var rnd = new Random(seed);
        for (int i = 0; i <= extraBlocks; i++)
        {
            var raw = new byte[40_000];
            rnd.NextBytes(raw);
            if (i == 0) { var d = Encoding.Latin1.GetBytes("\0\0\0\0" + desc + "\0"); Array.Copy(d, raw, d.Length); }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) zs.Write(raw);
            o.Write(BitConverter.GetBytes((int)z.Length)); o.Write(BitConverter.GetBytes(raw.Length)); o.Write(z.ToArray());
        }
        var footer = new byte[SaveInfo.FooterBytes];
        Encoding.ASCII.GetBytes(SaveInfo.FooterMagic).CopyTo(footer, 0);
        o.Write(footer);
        return o.ToArray();
    }

    internal static string Desc(long unix, double hours, int kind = 1, int n = 4) =>
        $"{kind}|{n}|@subchapter_298_name||@location_Skalice|{unix}|06/10/2026 13:18|{hours.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)}|";

    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), "kcdus-w-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void A_saves_description_is_read_from_its_first_block()
    {
        var info = SaveInfo.Read(FakeSave(Desc(1791314320, 1.388895)))!;
        Assert.Equal(1791314320, info.SavedUnix);
        Assert.Equal(1.388895, info.Hours, 6);
        Assert.Equal("@location_Skalice", info.Place);
        Assert.Equal(1, info.Kind);
        Assert.Equal(4, info.Number);
    }

    [Fact]
    public void A_whole_save_validates_and_a_damaged_one_does_not()
    {
        var f = FakeSave(Desc(1791314320, 2.0));
        Assert.True(SaveInfo.Validate(f, out _));
        var cut = f[..^1000];
        Assert.False(SaveInfo.Validate(cut, out _));
        var flipped = (byte[])f.Clone(); flipped[40] ^= 0xFF;   // inside the first block's zlib data
        Assert.False(SaveInfo.Validate(flipped, out _));
        var nofooter = (byte[])f.Clone(); nofooter[^64] = (byte)'X';
        Assert.False(SaveInfo.Validate(nofooter, out var why)); Assert.Equal("no footer", why);
        Assert.False(SaveInfo.Validate(new byte[10], out _));
    }

    [Fact]
    public void The_store_lists_a_playline_newest_first_by_the_time_inside_the_save()
    {
        var root = Temp();
        try
        {
            var s = new SaveStore(root, Path.Combine(root, "_bk"));
            var dir = s.PlaylineDir(2); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "a.whs"), FakeSave(Desc(1000000001, 1.0)));
            File.WriteAllBytes(Path.Combine(dir, "b.whs"), FakeSave(Desc(1000000900, 3.0)));
            File.WriteAllBytes(Path.Combine(dir, "c.whs"), FakeSave(Desc(1000000500, 2.0)));
            File.WriteAllText(Path.Combine(dir, "junk.whs"), "not a save");
            Assert.Equal(new[] { "b.whs", "c.whs", "a.whs" }, s.Saves(2).Select(x => Path.GetFileName(x.Path)));
            Assert.Equal(new[] { 2 }, s.Playlines());
            Assert.Equal(3.0, s.Newest(2)!.Info.Hours);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_friends_world_goes_into_a_playline_of_the_mods_own_and_what_was_there_is_backed_up()
    {
        var root = Temp();
        try
        {
            var s = new SaveStore(root, Path.Combine(root, "_bk"));
            var first = FakeSave(Desc(1000000100, 1.0), seed: 1);
            var second = FakeSave(Desc(1000000200, 2.0), seed: 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.InstallWorld(40, "w1", first, "t0", false));  // the game has five playlines; a sixth folder stops it starting
            Assert.Throws<InvalidDataException>(() => s.InstallWorld(4, "w1", first[..^10], "t0", false));   // never a broken file
            s.InstallWorld(4, "w1", first, "t1", replace: false);                                            // an empty slot is fine
            Assert.Throws<InvalidOperationException>(() => s.InstallWorld(4, "w1", second, "t2", false));  // a slot with a game in it is not, unless it is the world's own
            var target = s.InstallWorld(4, "w1", second, "t2", replace: true);
            Assert.Equal(new[] { target }, Directory.GetFiles(s.PlaylineDir(4), "*.whs"));                // the only save there
            Assert.Equal(2.0, s.Newest(4)!.Info.Hours);
            Assert.Contains(Directory.GetFiles(Path.Combine(root, "_bk"), "world.whs", SearchOption.AllDirectories),
                p => File.ReadAllBytes(p).SequenceEqual(first)); // verified originals live in unique transaction archives
            Assert.Empty(Directory.GetFiles(s.PlaylineDir(4), "*.part"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(ResolvePolicy.Furthest, 5.0, 100, 9.0, 50, true, Winner.Remote)]    // the one who has played further wins, whoever saved last
    [InlineData(ResolvePolicy.Furthest, 9.0, 50, 5.0, 100, false, Winner.Local)]
    [InlineData(ResolvePolicy.Furthest, 5.00, 100, 5.01, 200, true, Winner.Remote)] // within a few minutes of play: the newer save
    [InlineData(ResolvePolicy.Host, 5.0, 100, 9.0, 200, true, Winner.Local)]
    [InlineData(ResolvePolicy.Host, 5.0, 100, 9.0, 200, false, Winner.Remote)]
    [InlineData(ResolvePolicy.Newest, 9.0, 100, 5.0, 200, true, Winner.Remote)]
    [InlineData(ResolvePolicy.Newest, 5.0, 300, 9.0, 200, false, Winner.Local)]
    [InlineData(ResolvePolicy.Furthest, 5.0, 100, 5.0, 100, true, Winner.Same)]
    public void The_copy_that_is_behind_is_the_one_that_is_replaced(ResolvePolicy p, double lh, long lu, double rh, long ru, bool localHost, Winner expected)
    {
        Assert.Equal(expected, WorldResolve.Decide(new WorldStamp("w", lh, lu), new WorldStamp("w", rh, ru), p, localHost));
    }

    [Fact]
    public void Both_ends_reach_the_same_answer()
    {
        var a = new WorldStamp("w", 12.0, 500); var b = new WorldStamp("w", 3.0, 900);
        foreach (var p in Enum.GetValues<ResolvePolicy>())
        {
            var atHost = WorldResolve.Decide(a, b, p, localIsHost: true);
            var atGuest = WorldResolve.Decide(b, a, p, localIsHost: false);
            Assert.Equal(atHost == Winner.Local, atGuest == Winner.Remote);
            Assert.Equal(atHost == Winner.Remote, atGuest == Winner.Local);
        }
        Assert.Throws<ArgumentException>(() => WorldResolve.Decide(new WorldStamp("x", 1, 1), new WorldStamp("y", 1, 1), ResolvePolicy.Host, true));
    }

    [Fact]
    public void A_stamp_survives_the_wire_and_a_garbled_one_is_refused()
    {
        var s = new WorldStamp("ab12", 12.3456, 1791314320, "Our world");
        var back = WorldStamp.Decode(s.Encode().Split('|'), 0)!;
        Assert.Equal(s, back);
        Assert.Null(WorldStamp.Decode(new[] { "-", "1", "2" }, 0));
        Assert.Null(WorldStamp.Decode(new[] { "w", "x", "2" }, 0));
        Assert.Null(WorldStamp.Decode(new[] { "w", "1" }, 0));
    }

    [Fact]
    public void A_save_crosses_the_wire_in_pieces_in_any_order_and_arrives_whole_or_not_at_all()
    {
        var file = FakeSave(Desc(1000000100, 1.0), extraBlocks: 40, seed: 7);
        var offer = WorldTransfer.MakeOffer("t1", "w", file);
        Assert.Equal((file.Length + 29_999) / 30_000, offer.Chunks);
        var pieces = WorldTransfer.Pieces(file).ToList();
        Assert.All(pieces, p => Assert.True(p.Base64.Length < 60_000));                  // fits the relay's frame
        var rx = new WorldTransfer.Receiver(offer);
        Assert.Null(rx.Assemble());
        foreach (var p in pieces.AsEnumerable().Reverse()) { Assert.True(rx.Add(p.Index, p.Base64)); rx.Add(p.Index, p.Base64); }   // backwards, and twice
        Assert.True(rx.Complete);
        Assert.Equal(file, rx.Assemble());

        var rx2 = new WorldTransfer.Receiver(offer);
        foreach (var p in pieces.Skip(1)) rx2.Add(p.Index, p.Base64);
        Assert.Equal(new[] { 0 }, rx2.Missing());
        Assert.Null(rx2.Assemble());
        var bad = Convert.ToBase64String(new byte[WorldTransfer.ChunkBytes]);             // the right size, the wrong bytes
        rx2.Add(0, bad);
        Assert.Null(rx2.Assemble());                                                      // the checksum catches it
        Assert.False(rx2.Add(9999, "AAAA"));
        Assert.False(rx2.Add(0, "###"));
        Assert.Throws<InvalidDataException>(() => new WorldTransfer.Receiver(offer with { Chunks = 3 }));
    }

    [Fact]
    public void The_registry_keeps_worlds_across_restarts_and_hands_out_free_playlines()
    {
        var root = Temp();
        try
        {
            var path = Path.Combine(root, "worlds.json");
            var r = new WorldRegistry();
            var w = r.Upsert("w1", "Our world"); w.Playline = 4; w.Slot = true; w.Hours = 4.5; w.SavedUnix = 123;
            r.Active = "w1";
            r.Upsert("w2", "Other").Playline = 0;                                       // the player's own game: not a slot
            r.Save(path);
            var again = WorldRegistry.Load(path);
            Assert.Equal("w1", again.ActiveWorld!.Id);
            Assert.Equal(4.5, again.Find("w1")!.Hours);
            Assert.Equal(new[] { 4 }, again.SlotsInUse());
            Assert.Empty(WorldRegistry.Load(Path.Combine(root, "missing.json")).Worlds);
            File.WriteAllText(path, "{ not json");
            Assert.Throws<InvalidDataException>(() => WorldRegistry.Load(path));          // damaged metadata cannot silently discard world/home bindings
        }
        finally { Directory.Delete(root, true); }
    }
}
