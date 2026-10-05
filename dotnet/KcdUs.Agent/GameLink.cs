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

/// <summary>A TCP client for the engine's remote console: connects, drains whatever the engine says, sends '5'+line+NUL.</summary>
public sealed class RemoteConsoleClient
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly object _sendLock = new();

    public RemoteConsoleClient(string host, int port) { _host = host; _port = port; }

    public bool Connected { get; private set; }
    public event Action<bool>? StateChanged;

    private void Set(bool v)
    {
        if (Connected == v) return;
        Connected = v;
        StateChanged?.Invoke(v);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _tcp = new TcpClient { NoDelay = true };
                await _tcp.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
                _stream = _tcp.GetStream();
                Set(true);
                while (!ct.IsCancellationRequested)
                {
                    int n = await _stream.ReadAsync(buf, ct).ConfigureAwait(false);   // the hello and autocomplete noise: read and ignore
                    if (n == 0) break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
            Set(false);
            try { _tcp?.Close(); } catch { }
            try { await Task.Delay(1500, ct).ConfigureAwait(false); } catch { break; }
        }
        Set(false);
    }

    public bool TrySend(string line)
    {
        lock (_sendLock)
        {
            if (!Connected || _stream is null) return false;
            try
            {
                var b = Encoding.UTF8.GetBytes(line);
                var msg = new byte[b.Length + 2];
                msg[0] = (byte)'5';
                Buffer.BlockCopy(b, 0, msg, 1, b.Length);
                msg[^1] = 0;
                _stream.Write(msg, 0, msg.Length);
                return true;
            }
            catch { Set(false); return false; }
        }
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
    private CancellationTokenSource? _cts;
    private readonly List<Task> _tasks = new();

    public ConsoleGameLink(string gameDir, string host = "127.0.0.1", int port = 4600)
    {
        _console = new RemoteConsoleClient(host, port);
        _tail = new LogTail(Path.Combine(gameDir, "kcd.log"));
        _tail.LineRead += l => { if (GameLine.Parse(l) is { } g) Line?.Invoke(g); };
        _console.StateChanged += v => { if (!v) _packer.Clear(); ConsoleStateChanged?.Invoke(v); };
    }

    public event Action<GameLine>? Line;
    public event Action<bool>? ConsoleStateChanged;
    public bool ConsoleConnected => _console.Connected;
    public int Queued => _packer.Queued;
    public int Dropped => _packer.Dropped;

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
            if (_console.Connected && _packer.Next() is { } cmd)
                _console.TrySend(cmd);
            try { await Task.Delay(CommandPacker.SpacingMs, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        foreach (var t in _tasks) { try { await t.ConfigureAwait(false); } catch { } }
    }
}
