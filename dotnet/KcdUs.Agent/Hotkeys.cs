// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KcdUs.Agent;

/// <summary>
/// F11 (join your host) and F12 (stay in the open world), read while the game window is in front. The agent reads the keyboard
/// state like any overlay does; it injects nothing into the game. A key press is an edge: held keys do not repeat.
/// </summary>
public sealed class Hotkeys
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public const int VkF11 = 0x7A, VkF12 = 0x7B;

    /// <summary>The virtual-key codes of the two answers (0 = that answer has no key). The Multiplayer tab's Keys page sets them (KeyPreset).</summary>
    public int JoinVk { get; set; } = VkF11;
    public int StayVk { get; set; } = VkF12;
    public string JoinName => KeyPreset.Name(JoinVk);
    public string StayName => KeyPreset.Name(StayVk);

    private readonly Func<bool> _gameInFront;
    private bool _f11, _f12;

    public Hotkeys(Func<bool>? gameInFront = null) { _gameInFront = gameInFront ?? GameWindowInFront; }

    public event Action? Join;
    public event Action? Stay;

    /// <summary>Is a key down right now (injectable for tests).</summary>
    public Func<int, bool> IsDown { get; set; } = vk => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool GameWindowInFront()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            GetWindowThreadProcessId(h, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("KingdomCome", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Poll once (about every 50 ms).</summary>
    public void Poll()
    {
        bool front = _gameInFront();
        bool f11 = front && JoinVk != 0 && IsDown(JoinVk), f12 = front && StayVk != 0 && IsDown(StayVk);
        if (f11 && !_f11) Join?.Invoke();
        if (f12 && !_f12) Stay?.Invoke();
        _f11 = f11; _f12 = f12;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Poll(); } catch { }
            try { await Task.Delay(50, ct).ConfigureAwait(false); } catch { break; }
        }
    }
}
