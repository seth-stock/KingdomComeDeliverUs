// SPDX-License-Identifier: GPL-3.0-only
// The agent's half of the engine adapter's pause gate (PauseGate.cs). The layout matches native/KcdUs.EngineBridge/EngineBridge.cpp (struct PauseShared).
using System.IO.MemoryMappedFiles;
using KcdUs.Agent;

namespace KcdUs.Tests;

public class PauseGateTests
{
    [Fact]
    public void The_layout_matches_the_adapters_struct()
    {
        // unsigned magic, version; long leversOn; unsigned mask; long long agentBeatMs; long armed, calls, declined, histNext; unsigned hist[16]
        Assert.Equal(4 + 4 + 4 + 4 + 8 + 4 * 4 + 16 * 4, PauseGateLayout.Size);
        Assert.Equal(16, PauseGateLayout.OffBeat);
        Assert.Equal(24, PauseGateLayout.OffArmed);
        Assert.Equal(1u << 7, PauseGateLayout.DefaultMask);   // the ESC menu's pause source, read live in the retail engine
    }

    [Fact]
    public void Without_a_game_the_gate_is_unavailable_and_applying_it_does_nothing()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("KCDUS_LIVE_GATE") == "1") return;
        using var g = new BridgePauseGate();
        if (g.Available) return;                              // a real game with the adapter is running on this machine: nothing to prove here
        g.Apply(true);
        g.Apply(false);
        Assert.False(g.Available);
    }

    [Fact]
    public void A_fake_adapter_mapping_receives_the_heartbeat_the_mask_and_the_levers()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("KCDUS_LIVE_GATE") == "1") return;
        using (var probe = new BridgePauseGate()) if (probe.Available) return;   // never write into a real game's gate from a unit test
        using var map = MemoryMappedFile.CreateNew(PauseGateLayout.MapName, PauseGateLayout.Size);
        using var v = map.CreateViewAccessor();
        v.Write(PauseGateLayout.OffMagic, PauseGateLayout.Magic);
        v.Write(PauseGateLayout.OffVersion, 1u);
        v.Write(PauseGateLayout.OffArmed, 1);
        using var g = new BridgePauseGate();
        Assert.True(g.Available);
        long before = Environment.TickCount64;
        g.Apply(true);
        Assert.Equal(1, v.ReadInt32(PauseGateLayout.OffLevers));
        Assert.Equal(PauseGateLayout.DefaultMask, v.ReadUInt32(PauseGateLayout.OffMask));
        Assert.InRange(v.ReadInt64(PauseGateLayout.OffBeat), before, Environment.TickCount64);
        g.Apply(false);
        Assert.Equal(0, v.ReadInt32(PauseGateLayout.OffLevers));
    }

    /// <summary>Live only (KCDUS_LIVE_GATE=1, a private game started with the adapter, its ESC menu closed): the gate is armed and accepts the heartbeat.</summary>
    [Fact]
    public void Live_the_running_adapter_accepts_the_agents_heartbeat()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("KCDUS_LIVE_GATE") != "1") return;
        using var g = new BridgePauseGate();
        Assert.True(g.Available);
        g.Apply(true);
        using var map = MemoryMappedFile.OpenExisting(PauseGateLayout.MapName);
        using var v = map.CreateViewAccessor(0, PauseGateLayout.Size);
        Assert.Equal(1, v.ReadInt32(PauseGateLayout.OffLevers));
        Assert.InRange(Environment.TickCount64 - v.ReadInt64(PauseGateLayout.OffBeat), 0, 2000);
    }
}
