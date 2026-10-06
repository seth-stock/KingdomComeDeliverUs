// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdUs.Agent;

/// <summary>
/// F11 / F12 on Linux (docs/LINUX.md). Windows reads the keyboard with GetAsyncKeyState; a native Linux agent has no such call, and
/// the game runs under Wine, so the keys are read from the kernel's input devices (/dev/input/event*, which needs the user to be in
/// the "input" group). It only watches two keys and keeps nothing else. When no keyboard can be opened the agent says so and the
/// same two answers are one command away: <c>kcdus join-story</c> / <c>kcdus stay-story</c>.
/// </summary>
public sealed class LinuxKeys : IDisposable
{
    // linux/input-event-codes.h
    public const int KeyF11 = 87, KeyF12 = 88;

    /// <summary>The kernel's key code for a Windows F-key virtual-key (F1..F10 are 59..68, F11 87, F12 88); 0 when it is not an F-key.</summary>
    public static int CodeForVk(int vk) => vk is >= 0x70 and <= 0x79 ? 59 + (vk - 0x70) : vk == 0x7A ? KeyF11 : vk == 0x7B ? KeyF12 : 0;
    private static bool Watched(int code) => code is >= 59 and <= 68 or KeyF11 or KeyF12;
    public const int EvKey = 1;
    /// <summary>struct input_event on 64-bit Linux: timeval (2 x 8), type u16, code u16, value i32.</summary>
    public const int EventSize = 24;

    private readonly HashSet<int> _down = new();
    private readonly object _gate = new();
    private readonly List<FileStream> _streams = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Decodes one event; false when it is not a key press/release/repeat of a key.</summary>
    public static bool TryParse(ReadOnlySpan<byte> ev, out int code, out int value)
    {
        code = value = 0;
        if (ev.Length < EventSize) return false;
        ushort type = BitConverter.ToUInt16(ev[16..]);
        if (type != EvKey) return false;
        code = BitConverter.ToUInt16(ev[18..]);
        value = BitConverter.ToInt32(ev[20..]);
        return true;
    }

    /// <summary>Feeds one event into the down-set (value 0 = released; 1 = pressed; 2 = autorepeat, still down).</summary>
    public void Apply(int code, int value)
    {
        lock (_gate) { if (value == 0) _down.Remove(code); else _down.Add(code); }
    }

    public bool IsDownLinux(int keyCode) { lock (_gate) return _down.Contains(keyCode); }

    /// <summary>The Windows virtual-key the shared Hotkeys class asks about, answered from the Linux key state.</summary>
    public bool IsDownVk(int vk) => CodeForVk(vk) is var c && c != 0 && IsDownLinux(c);

    /// <summary>Opens every readable keyboard-ish event device and starts one reader per device. Returns how many opened.</summary>
    public int Start(Action<string>? log = null, string inputDir = "/dev/input")
    {
        if (!Directory.Exists(inputDir)) return 0;
        foreach (var path in Directory.EnumerateFiles(inputDir, "event*"))
        {
            try
            {
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.Asynchronous);
                _streams.Add(fs);
                _ = Task.Run(() => Pump(fs, _cts.Token));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { /* not ours to read */ }
        }
        if (_streams.Count == 0) log?.Invoke("hotkeys: no input device is readable (add yourself to the 'input' group, or use: kcdus join-story / kcdus stay-story)");
        else log?.Invoke($"hotkeys: watching F11/F12 on {_streams.Count} input device(s)");
        return _streams.Count;
    }

    private async Task Pump(FileStream fs, CancellationToken ct)
    {
        var buf = new byte[EventSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int got = 0;
                while (got < EventSize)
                {
                    int n = await fs.ReadAsync(buf.AsMemory(got, EventSize - got), ct).ConfigureAwait(false);
                    if (n <= 0) return;
                    got += n;
                }
                if (TryParse(buf, out int code, out int value) && Watched(code)) Apply(code, value);
            }
        }
        catch { /* device unplugged or the agent is stopping */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var s in _streams) { try { s.Dispose(); } catch { } }
    }
}
