// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net.Sockets;
using System.Text;

namespace KcdUs.Agent;

/// <summary>One KCDUS| line the game's mod wrote to its log: KCDUS|KIND|field|field.</summary>
public readonly record struct GameLine(string Kind, string[] Fields, string Raw)
{
    public static GameLine? Parse(string line)
    {
        int i = line.IndexOf("KCDUS|", StringComparison.Ordinal);
        if (i < 0) return null;
        var parts = line[(i + 6)..].TrimEnd('\r', '\n').Split('|');
        return parts.Length == 0 || parts[0].Length == 0 ? null : new GameLine(parts[0], parts, line);
    }
}

/// <summary>The agent's two-way channel to the game's mod: records in (paced, packed), KCDUS lines out.</summary>
public interface IGameLink : IAsyncDisposable
{
    event Action<GameLine>? Line;
    /// <summary>The engine's remote console accepts our connection (we can talk to the game). It is up from the main menu on.</summary>
    bool ConsoleConnected { get; }
    event Action<bool>? ConsoleStateChanged;
    /// <summary>Queue a record ("TYPE|field|field") for the game. Sent in order, packed into console commands.</summary>
    void Send(string record);
    /// <summary>Queue a record that replaces any waiting one with the same key (a player's latest position).</summary>
    void SendLatest(string key, string record);
    void Start(CancellationToken ct);
}

/// <summary>
/// Packs records into the game's console commands:  kcdus "<seq>~REC|f|f~REC|f"  (the mod's KCDUS_In). The game accepts about 3000
/// bytes a line and drops commands that arrive in bursts, so a command carries at most <see cref="MaxCommandChars"/> and the sender
/// spaces them by <see cref="SpacingMs"/> (docs/KCD1-MODDING.md section 3).
/// </summary>
public sealed class CommandPacker
{
    public const int MaxCommandChars = 1800;
    public const int SpacingMs = 25;
    public const int MaxQueued = 600;

    private readonly object _gate = new();
    private readonly List<string> _fifo = new();
    private readonly List<string> _latestOrder = new();
    private readonly Dictionary<string, string> _latest = new();
    private int _seq;

    public int Queued { get { lock (_gate) return _fifo.Count + _latest.Count; } }
    public int Dropped { get; private set; }
    public int LastSequence => _seq;

    public void Add(string record)
    {
        lock (_gate)
        {
            if (_fifo.Count >= MaxQueued) { _fifo.RemoveAt(0); Dropped++; }
            _fifo.Add(record);
        }
    }

    public void AddLatest(string key, string record)
    {
        lock (_gate)
        {
            if (!_latest.ContainsKey(key)) _latestOrder.Add(key);
            _latest[key] = record;
        }
    }

    /// <summary>The next console line, or null when nothing waits.</summary>
    public string? Next()
    {
        lock (_gate)
        {
            if (_fifo.Count == 0 && _latest.Count == 0) return null;
            int seq = ++_seq;
            var sb = new StringBuilder("kcdus ").Append(seq);
            int taken = 0;
            while (_fifo.Count > 0)
            {
                var r = _fifo[0];
                if (taken > 0 && sb.Length + 1 + r.Length > MaxCommandChars) break;
                sb.Append('~').Append(r);
                _fifo.RemoveAt(0);
                taken++;
            }
            while (_latestOrder.Count > 0)
            {
                var key = _latestOrder[0];
                var r = _latest[key];
                if (taken > 0 && sb.Length + 1 + r.Length > MaxCommandChars) break;
                sb.Append('~').Append(r);
                _latestOrder.RemoveAt(0);
                _latest.Remove(key);
                taken++;
            }
            return sb.ToString();
        }
    }

    public void Clear() { lock (_gate) { _fifo.Clear(); _latest.Clear(); _latestOrder.Clear(); } }
}

/// <summary>
/// A TCP client for the engine's remote console: sends '5'+line+NUL and drains whatever the engine says.
/// <b>The engine processes only 21 commands per connection and then silently stops serving it</b> (found in the real game: a fresh connection
/// answers 21 pings and no more, at any pace and with any line size; new connections work again). So the client opens a new connection
/// every <see cref="MaxCommandsPerConnection"/> commands; a local connect costs about a millisecond.
/// </summary>
public sealed class RemoteConsoleClient
{
    /// <summary>Under the engine's 21, with room for a hello and a mistake.</summary>
    public const int MaxCommandsPerConnection = 16;
    public const int WriteTimeoutMs = 1500;
    public const int ConnectTimeoutMs = 500;

    private readonly string _host;
    private readonly int _port;
    private readonly object _gate = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _sentOnConnection;
    private long _generation;

    public RemoteConsoleClient(string host, int port) { _host = host; _port = port; }

    public bool Connected { get; private set; }
    public int Reconnects { get; private set; }
    public int Rotations { get; private set; }
    public long Sent { get; private set; }
    public string LastReset { get; private set; } = "";
    public event Action<bool>? StateChanged;

    private void Set(bool v)
    {
        if (Connected == v) return;
        Connected = v;
        StateChanged?.Invoke(v);
    }

    private bool Open()
    {
        // caller holds _gate
        try { _tcp?.Close(); } catch { }
        _tcp = null; _stream = null;
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            var connect = tcp.ConnectAsync(_host, _port);
            if (!connect.Wait(ConnectTimeoutMs)) { tcp.Close(); return false; }
            _tcp = tcp;
            _stream = tcp.GetStream();
            _sentOnConnection = 0;
            long gen = ++_generation;
            var s = _stream;
            _ = Task.Run(async () =>   // drain the engine's replies (the hello, autocomplete noise, forwarded log lines) so it never blocks on us
            {
                var buf = new byte[4096];
                try { while (await s.ReadAsync(buf).ConfigureAwait(false) > 0) { } } catch { }
                lock (_gate) { if (gen == _generation) { _stream = null; Set(false); } }
            });
            return true;
        }
        catch { try { tcp.Close(); } catch { } return false; }
    }

    /// <summary>Keeps trying to connect while the game is not up; once connected it only has to notice a drop.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool up;
            lock (_gate)
            {
                if (_stream is null)
                {
                    if (Open()) { Set(true); }
                    else Set(false);
                }
                up = Connected;
            }
            try { await Task.Delay(up ? 500 : 1500, ct).ConfigureAwait(false); } catch { break; }
        }
        lock (_gate) { try { _tcp?.Close(); } catch { } _stream = null; Set(false); }
    }

    /// <summary>Drop the connection (a dead one); the next send or the run loop dials again.</summary>
    public void Reset(string why)
    {
        lock (_gate)
        {
            try { _tcp?.Close(); } catch { }
            _stream = null;
            Reconnects++;
            LastReset = why;
        }
    }

    public bool TrySend(string line)
    {
        NetworkStream s;
        lock (_gate)
        {
            if (!Connected) return false;
            if (_stream is null || _sentOnConnection >= MaxCommandsPerConnection)
            {
                bool rotating = _stream is not null;
                if (!Open()) { Set(false); return false; }
                if (rotating) Rotations++;
            }
            s = _stream!;
            _sentOnConnection++;
        }
        var b = Encoding.UTF8.GetBytes(line);
        var msg = new byte[b.Length + 2];
        msg[0] = (byte)'5';
        Buffer.BlockCopy(b, 0, msg, 1, b.Length);
        msg[^1] = 0;
        try
        {
            using var cts = new CancellationTokenSource(WriteTimeoutMs);
            s.WriteAsync(msg, cts.Token).AsTask().GetAwaiter().GetResult();
            Sent++;
            return true;
        }
        catch (OperationCanceledException) { Reset("a write did not finish in " + WriteTimeoutMs + " ms"); return false; }
        catch { Reset("a write failed"); return false; }
    }
}

/// <summary>Follows kcd.log: new complete lines only. The game truncates the file when it starts; that resets the offset.</summary>
public sealed class LogTail
{
    private readonly string _path;
    private readonly bool _fromStart;
    private long _pos = -1;
    private string _carry = "";

    public LogTail(string path, bool fromStart = false) { _path = path; _fromStart = fromStart; }

    public event Action<string>? LineRead;

    /// <summary>Read what is new once; returns the number of lines delivered.</summary>
    public int Poll()
    {
        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (_pos < 0) _pos = _fromStart ? 0 : len;
            if (len < _pos) { _pos = 0; _carry = ""; }   // a new game start: the file began again
            if (len == _pos) return 0;
            fs.Seek(_pos, SeekOrigin.Begin);
            var buf = new byte[len - _pos];
            int got = fs.Read(buf, 0, buf.Length);
            _pos += got;
            var text = _carry + Encoding.UTF8.GetString(buf, 0, got);
            int last = text.LastIndexOf('\n');
            if (last < 0) { _carry = text; return 0; }
            _carry = text[(last + 1)..];
            int n = 0;
            foreach (var l in text[..last].Split('\n'))
            {
                if (l.Length == 0) continue;
                LineRead?.Invoke(l.TrimEnd('\r'));
                n++;
            }
            return n;
        }
        catch (FileNotFoundException) { _pos = -1; _carry = ""; return 0; }
        catch (DirectoryNotFoundException) { return 0; }
        catch (IOException) { return 0; }
    }

    public async Task RunAsync(CancellationToken ct, int intervalMs = 40)
    {
        while (!ct.IsCancellationRequested)
        {
            Poll();
            try { await Task.Delay(intervalMs, ct).ConfigureAwait(false); } catch { break; }
        }
    }
}

/// <summary>The real link: the remote console for commands in, kcd.log for events out.</summary>
public sealed class ConsoleGameLink : IGameLink
{
    private readonly RemoteConsoleClient _console;
    private readonly LogTail _tail;
    private readonly CommandPacker _packer = new();
    private readonly Action<string> _log;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _tasks = new();

    private long _lastPongAtMs;
    private bool _everPonged;
    private long _lastResetAtMs;
    private readonly Func<long> _now;

    /// <summary>No PONG this long although we ping every 2 s: the game no longer reads our connection.</summary>
    public const int SilentResetMs = 8000;

    public ConsoleGameLink(string gameDir, string host = "127.0.0.1", int port = 4600, Func<long>? nowMs = null, Action<string>? log = null)
    {
        _now = nowMs ?? (() => Environment.TickCount64);
        _log = log ?? (_ => { });
        _console = new RemoteConsoleClient(host, port);
        _tail = new LogTail(Path.Combine(gameDir, "kcd.log"));
        _tail.LineRead += l =>
        {
            if (GameLine.Parse(l) is not { } g) return;
            if (g.Kind == "PONG") { _lastPongAtMs = _now(); _everPonged = true; }   // only a PONG proves the game reads OUR commands: its heartbeat lines flow whatever we do
            Line?.Invoke(g);
        };
        _console.StateChanged += v => { if (!v) _packer.Clear(); ConsoleStateChanged?.Invoke(v); };
    }

    public event Action<GameLine>? Line;
    public event Action<bool>? ConsoleStateChanged;
    public bool ConsoleConnected => _console.Connected;
    public int Queued => _packer.Queued;
    public int Dropped => _packer.Dropped;
    public int Reconnects => _console.Reconnects;
    public int Rotations => _console.Rotations;
    public long CommandsSent => _console.Sent;

    public void Send(string record) => _packer.Add(record);
    public void SendLatest(string key, string record) => _packer.AddLatest(key, record);

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t = _cts.Token;
        _tasks.Add(Task.Run(() => _console.RunAsync(t), t));
        _tasks.Add(Task.Run(() => _tail.RunAsync(t), t));
        _tasks.Add(Task.Run(() => Pump(t), t));
    }

    private async Task Pump(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_console.Connected)
            {
                if (_packer.Next() is { } cmd) _console.TrySend(cmd);
                Watchdog();
            }
            try { await Task.Delay(CommandPacker.SpacingMs, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    /// <summary>The game answered our pings once and no longer does: rebuild the connection (at most every <see cref="SilentResetMs"/>).</summary>
    private void Watchdog()
    {
        long now = _now();
        if (!_everPonged || now - _lastPongAtMs < SilentResetMs || now - _lastResetAtMs < SilentResetMs) return;
        _lastResetAtMs = now;
        _log($"the game has not answered a ping for {(now - _lastPongAtMs) / 1000} s: dropping and re-dialling the remote console");
        _console.Reset("the game stopped answering pings");
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        foreach (var t in _tasks) { try { await t.ConfigureAwait(false); } catch { } }
    }
}
