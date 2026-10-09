// SPDX-License-Identifier: GPL-3.0-only
// The engine adapter's pause gate (native/KcdUs.EngineBridge/EngineBridge.cpp, "the pause gate"). The game's own ESC menu pauses the world natively, through
// CCryAction::PauseGame with source 7; the UI's ResumeGame node and the developer-only console command cannot undo that in a retail game. The adapter declines
// that one pause while this agent keeps a heartbeat going: the menu opens and the world keeps running. Ten seconds without a heartbeat (this agent closed,
// crashed or hung) and every pause runs again, so the gate can never leave a player unable to pause.
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace KcdUs.Agent;

/// <summary>Whether the game's own ESC menu may pause the world (the engine adapter's gate).</summary>
public interface IPauseGate
{
    /// <summary>The adapter is loaded in the running game and its hook is armed.</summary>
    bool Available { get; }
    /// <summary>On: the ESC menu does not pause (call again at least every few seconds: it is a heartbeat). Off: menus pause as usual.</summary>
    void Apply(bool on);
}

public static class PauseGateLayout
{
    public const string MapName = @"Local\KcdUsBridgePause.v1";
    public const int Size = 4 * 4 + 8 + 4 * 4 + 16 * 4;
    public const uint Magic = 0x4b435553;
    /// <summary>CCryAction pause sources of the retail engine (read live: the ESC menu pauses with 7, the same number as in KCD2).</summary>
    public const int SourceInGameMenu = 7;
    public const uint DefaultMask = 1u << SourceInGameMenu;
    public const int OffMagic = 0, OffVersion = 4, OffLevers = 8, OffMask = 12, OffBeat = 16, OffArmed = 24, OffCalls = 28, OffDeclined = 32;
}

/// <summary>The real gate: the adapter's named shared memory in this Windows session.</summary>
[SupportedOSPlatform("windows")]
public sealed class BridgePauseGate : IPauseGate, IDisposable
{
    private readonly Func<long> _tickMs;
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private long _nextOpenMs;

    public BridgePauseGate(Func<long>? tickMs = null) { _tickMs = tickMs ?? (() => Environment.TickCount64); }

    private bool Open()
    {
        if (_view != null) return true;
        long now = _tickMs();
        if (now < _nextOpenMs) return false;
        _nextOpenMs = now + 5000;
        try
        {
            _map = MemoryMappedFile.OpenExisting(PauseGateLayout.MapName, MemoryMappedFileRights.ReadWrite);
            _view = _map.CreateViewAccessor(0, PauseGateLayout.Size, MemoryMappedFileAccess.ReadWrite);
            if (_view.ReadUInt32(PauseGateLayout.OffMagic) != PauseGateLayout.Magic || _view.ReadUInt32(PauseGateLayout.OffVersion) != 1) { Close(); return false; }
            return true;
        }
        catch (Exception) { Close(); return false; }
    }

    private void Close() { _view?.Dispose(); _map?.Dispose(); _view = null; _map = null; }

    public bool Available
    {
        get
        {
            if (!Open()) return false;
            try { return _view!.ReadInt32(PauseGateLayout.OffArmed) == 1; } catch (Exception) { Close(); return false; }
        }
    }

    public long Declined { get { try { return Open() ? _view!.ReadInt32(PauseGateLayout.OffDeclined) : 0; } catch (Exception) { return 0; } } }

    public void Apply(bool on)
    {
        if (!Open()) return;
        try
        {
            _view!.Write(PauseGateLayout.OffMask, PauseGateLayout.DefaultMask);
            _view.Write(PauseGateLayout.OffBeat, Environment.TickCount64);   // the adapter compares with GetTickCount64 in the game: the same clock
            _view.Write(PauseGateLayout.OffLevers, on ? 1 : 0);
        }
        catch (Exception) { Close(); }
    }

    public void Dispose() => Close();
}
