// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Coop.Contract;
using KcdUs.Wire;

namespace KcdUs.Relay;

public sealed class RelayOptions
{
    public int Port { get; set; } = Proto.DefaultPort;
    public int MaxPlayers { get; set; } = 4;
    public string ServerName { get; set; } = "Deliver Us";
    public string Password { get; set; } = "";
    public string Release { get; set; } = KcdUs.Wire.Release.Current;
    public int IdleTimeoutSeconds { get; set; } = 45;
    public int HelloTimeoutSeconds { get; set; } = 10;
    public int StateMaxPerSecond { get; set; } = 30;
    public IPAddress BindAddress { get; set; } = IPAddress.Any;
    /// <summary>Development only: admit a peer whose mod payload cannot be verified (a real room refuses it).</summary>
    public bool AllowUnverifiedPayload { get; set; }
    /// <summary>Where the room remembers participant public keys (so a restart does not let another connection take an id); null: in memory only.</summary>
    public string? BindingsFile { get; set; }
}

/// <summary>
/// The relay: it forwards. It holds no world state: it checks the version, numbers the players, rate-limits a player's state
/// stream and lets only the host speak with the host's voice (HostEvent). Everything else is the agents' business.
/// </summary>
public sealed class RelayServer : IAsyncDisposable
{
    private sealed class Peer
    {
        public int Id;
        public string Name = "";
        public string Role = "guest";
        public TcpClient Client = null!;
        public Channel<byte[]> Outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(600) { FullMode = BoundedChannelFullMode.DropOldest });
        public DateTime WindowStart = DateTime.UtcNow;
        public int StatesInWindow;
        public bool Hello;
        public int HostEventsRefused;
        public string Challenge = "";
        public string ParticipantId = "";
        public RoomHandshake? Handshake;
        public RoomMode Mode = RoomMode.Presence;
    }

    private readonly ParticipantBindings _bindings = new();

    private readonly RelayOptions _o;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<int, Peer> _peers = new();
    private TcpListener? _listener;
    private CancellationTokenSource _cts = new();
    private Task? _accept;
    private readonly object _idLock = new();

    public RelayServer(RelayOptions options, Action<string>? log = null)
    {
        _o = options;
        _log = log ?? (_ => { });
    }

    public int Port => ((IPEndPoint?)_listener?.LocalEndpoint)?.Port ?? _o.Port;
    public int PlayerCount => _peers.Values.Count(p => p.Hello);
    public int? HostId => _peers.Values.FirstOrDefault(p => p.Hello && p.Role == "host")?.Id;

    private void SaveBindings()
    {
        if (_o.BindingsFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_o.BindingsFile))!);
            File.WriteAllLines(_o.BindingsFile + ".part", _bindings.Snapshot().Select(kv => kv.Key + " " + kv.Value));
            File.Move(_o.BindingsFile + ".part", _o.BindingsFile, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log("warning: the participant bindings could not be saved: " + e.Message); }
    }

    public void Start()
    {
        if (_o.BindingsFile is not null && File.Exists(_o.BindingsFile))
            _bindings.Restore(File.ReadAllLines(_o.BindingsFile).Select(l => l.Split(' ', 2)).Where(p => p.Length == 2).Select(p => new KeyValuePair<string, string>(p[0], p[1])));
        _listener = new TcpListener(_o.BindAddress, _o.Port);
        _listener.Start();
        _log($"listening on {_listener.LocalEndpoint} as \"{_o.ServerName}\" release {_o.Release} (max {_o.MaxPlayers})");
        _accept = Task.Run(() => AcceptLoop(_cts.Token));
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch { return; }
            c.NoDelay = true;
            _ = Task.Run(() => Serve(c, ct), ct);
        }
    }

    private int NextId()
    {
        lock (_idLock)
        {
            for (int i = 1; i < 250; i++)
                if (!_peers.ContainsKey(i)) return i;
        }
        return -1;
    }

    private async Task Serve(TcpClient client, CancellationToken ct)
    {
        var peer = new Peer { Client = client, Challenge = _bindings.Challenge() };
        var stream = client.GetStream();
        peer.Outbox.Writer.TryWrite(FrameIO.Encode(MessageType.Challenge, peer.Challenge));   // first: the proof of identity is signed over this
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writer = Task.Run(async () =>
        {
            try
            {
                await foreach (var buf in peer.Outbox.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
                {
                    await stream.WriteAsync(buf, linked.Token).ConfigureAwait(false);
                    await stream.FlushAsync(linked.Token).ConfigureAwait(false);
                }
            }
            catch { }
        });
        try
        {
            while (!linked.IsCancellationRequested)
            {
                int idle = peer.Hello ? _o.IdleTimeoutSeconds : _o.HelloTimeoutSeconds;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(idle));
                Frame? frame;
                try { frame = await FrameIO.ReadAsync(stream, timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { _log($"{Label(peer, remote)} timed out"); break; }
                if (frame is null) break;
                if (!await Handle(peer, frame.Value, remote).ConfigureAwait(false)) break;
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            lock (_idLock)
            {
            if (peer.Id != 0 && _peers.TryRemove(peer.Id, out _) && peer.Hello)
            {
                _log($"{Label(peer, remote)} left");
                Broadcast(MessageType.PlayerLeft, $"{peer.Id}|left", except: peer.Id);
            }
            }
            peer.Outbox.Writer.TryComplete();
            linked.Cancel();
            try { await writer.ConfigureAwait(false); } catch { }
            client.Close();
        }
    }

    private static string Label(Peer p, string remote) => p.Hello ? $"#{p.Id} {p.Name}" : remote;

    private void Send(Peer p, MessageType t, string text) => p.Outbox.Writer.TryWrite(FrameIO.Encode(t, text));

    private void Broadcast(MessageType t, string text, int except = -1)
    {
        var buf = FrameIO.Encode(t, text);
        foreach (var p in _peers.Values)
            if (p.Hello && p.Id != except)
                p.Outbox.Writer.TryWrite(buf);
    }

    private async Task<bool> Handle(Peer peer, Frame f, string remote)
    {
        switch (f.Type)
        {
            case MessageType.InfoRequest:
                var host = _peers.Values.FirstOrDefault(p => p.Hello && p.Role == "host")?.Name ?? "";
                Send(peer, MessageType.Info, $"{_o.ServerName}|{PlayerCount}|{_o.MaxPlayers}|{_o.Release}|{host}");
                await Task.Delay(50).ConfigureAwait(false); // let the writer flush before the connection closes
                return false;

            case MessageType.Hello:
                return OnHello(peer, f, remote);

            case MessageType.Bye:
                return false;
        }

        if (!peer.Hello)
        {
            _log($"{remote} sent {f.Type} before Hello; dropped");
            return false;
        }

        switch (f.Type)
        {
            case MessageType.Ping:
                Send(peer, MessageType.Pong, f.Text);
                break;

            case MessageType.State:
                var now = DateTime.UtcNow;
                if ((now - peer.WindowStart).TotalSeconds >= 1) { peer.WindowStart = now; peer.StatesInWindow = 0; }
                if (++peer.StatesInWindow <= _o.StateMaxPerSecond)
                    Broadcast(MessageType.PState, $"{peer.Id}|{f.Text}", except: peer.Id);
                break;

            case MessageType.Chat:
                var text = Safe.Clean(f.Text, 200);
                if (text.Length > 0)
                {
                    _log($"<{peer.Name}> {text}");
                    Broadcast(MessageType.PChat, $"{peer.Id}|{peer.Name}|{text}", except: peer.Id);
                }
                break;

            case MessageType.Event:
                if (ValidEvent(f.Text))
                    Broadcast(MessageType.PEvent, $"{peer.Id}|{f.Text}", except: peer.Id);
                break;

            case MessageType.HostEvent:
                if (peer.Role != "host")
                {
                    if (++peer.HostEventsRefused <= 3) _log($"{Label(peer, remote)} sent a host event and is not the host; ignored");
                }
                else if (ValidEvent(f.Text))
                {
                    Broadcast(MessageType.PHostEvent, $"{peer.Id}|{f.Text}", except: peer.Id);
                }
                break;
        }
        return true;
    }

    private static bool ValidEvent(string text)
    {
        int bar = text.IndexOf('|');
        string kind = bar < 0 ? text : text[..bar];
        return kind.Length is > 0 and <= 24 && kind.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private bool OnHello(Peer peer, Frame f, string remote)
    {
        if (peer.Hello) return true;
        var h = f.Fields;
        // proto|release|name|role|password
        if (h.Length < 4 || !int.TryParse(h[0], out var proto) || proto != Proto.ProtocolVersion)
        {
            Send(peer, MessageType.Reject, $"protocol|relay speaks protocol {Proto.ProtocolVersion}");
            _log($"{remote} refused: protocol {(h.Length > 0 ? h[0] : "?")}");
            return RejectAndClose();
        }
        if (!Release.Same(h[1], _o.Release))
        {
            Send(peer, MessageType.Reject, $"version|relay is {Release.Normalize(_o.Release)}, you are {Release.Normalize(h[1])}");
            _log($"{remote} refused: release {h[1]} != {_o.Release}");
            return RejectAndClose();
        }
        string pw = h.Length > 4 ? h[4] : "";
        if (_o.Password.Length > 0 && pw != _o.Password)
        {
            Send(peer, MessageType.Reject, "password|wrong password");
            _log($"{remote} refused: password");
            return RejectAndClose();
        }
        string role = h[3] == "host" ? "host" : "guest";

        // the room handshake: same game, same contract, same mod payload; and who this participant is
        var hs = h.Length > 5 ? RoomHandshake.TryDecode(h[5]) : null;
        if (hs is null)
        {
            Send(peer, MessageType.Reject, "contract|this build did not send a room handshake: everyone must install the same build");
            _log($"{remote} refused: no room handshake");
            return RejectAndClose();
        }
        var idParts = (h.Length > 6 ? h[6] : "").Split(';');
        var claim = idParts.Length == 3 ? _bindings.Claim(idParts[0], idParts[1], peer.Challenge, idParts[2]) : ParticipantBindings.Result.Malformed;
        if (claim is not (ParticipantBindings.Result.Bound or ParticipantBindings.Result.Accepted))
        {
            Send(peer, MessageType.Reject, "identity|" + claim switch
            {
                ParticipantBindings.Result.WrongKey => "that participant id belongs to another player's key",
                ParticipantBindings.Result.BadSignature => "the identity proof is not valid",
                ParticipantBindings.Result.UnknownChallenge => "the identity proof answered an unknown challenge",
                _ => "the identity proof is malformed",
            });
            _log($"{remote} refused: identity {claim}");
            return RejectAndClose();
        }
        if (claim == ParticipantBindings.Result.Bound) SaveBindings();
        peer.ParticipantId = idParts[0];
        var policy = new RoomPolicy(_o.AllowUnverifiedPayload);
        NegotiationResult room;
        lock (_idLock)
        {
            if (_peers.Values.Any(p => p.Hello && p.ParticipantId == peer.ParticipantId))
            {
                Send(peer, MessageType.Reject, "identity|this participant is already connected from another process (a cloned profile?)");
                _log($"{remote} refused: participant already connected");
                return RejectAndClose();
            }
            var host = _peers.Values.FirstOrDefault(p => p.Hello && p.Role == "host");
            if (role == "host")
            {
                room = Negotiation.Negotiate(hs, hs, policy);
                foreach (var g in _peers.Values.Where(p => p.Hello && p.Handshake is not null))
                {
                    var r = Negotiation.Negotiate(hs, g.Handshake!, policy);
                    if (!r.Admitted) { room = r; break; }
                }
            }
            else room = host?.Handshake is null ? Negotiation.Negotiate(hs, hs, policy) : Negotiation.Negotiate(host.Handshake, hs, policy);
        }
        if (!room.Admitted)
        {
            Send(peer, MessageType.Reject, "contract|" + room.Describe().Replace('|', '/'));
            _log($"{remote} refused: {room.Describe()}");
            return RejectAndClose();
        }
        peer.Handshake = hs;
        peer.Mode = room.Mode;
        // Admission, ID reservation, names, host selection and the initial roster
        // must be one operation. NextId's old lock released before insertion.
        lock (_idLock)
        {
        if (_peers.Values.Any(p => p.Hello && p.ParticipantId == peer.ParticipantId))   // checked again where the peer is inserted: two simultaneous Hellos cannot both pass
        {
            Send(peer, MessageType.Reject, "identity|this participant is already connected from another process (a cloned profile?)");
            _log($"{remote} refused: participant already connected");
            return RejectAndClose();
        }
        if (role == "host" && HostId != null)
        {
            Send(peer, MessageType.Reject, "host-taken|this relay already has a host");
            _log($"{remote} refused: host taken");
            return RejectAndClose();
        }
        if (PlayerCount >= _o.MaxPlayers)
        {
            Send(peer, MessageType.Reject, $"full|{_o.MaxPlayers} players is the limit");
            _log($"{remote} refused: full");
            return RejectAndClose();
        }

        string name = Safe.Name(h[2]);
        string baseName = name;
        int n = 2;
        while (_peers.Values.Any(p => p.Hello && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} ({n++})";

        int id = NextId();
        if (id < 0) return RejectAndClose();
        peer.Id = id;
        peer.Name = name;
        peer.Role = role;
        _peers[id] = peer;
        peer.Hello = true;

        int hostId = HostId ?? 0;
        Send(peer, MessageType.Welcome, $"{id}|{hostId}|{Proto.ProtocolVersion}|{_o.Release}|{_o.ServerName}|{ModeWord(room.Mode)}|{string.Join(',', room.Missing)}|{FlagsOf(room)}");
        var others = string.Join(';', _peers.Values.Where(p => p.Hello && p.Id != id).OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.Name}:{p.Role}"));
        Send(peer, MessageType.PlayerList, others);
        Broadcast(MessageType.PlayerJoined, $"{id}|{name}|{role}|{ModeWord(room.Mode)}|{FlagsOf(room)}", except: id);
        _log($"#{id} {name} joined as {role} ({remote}); {PlayerCount}/{_o.MaxPlayers}");
        return true;
        }
    }

    /// <summary>
    /// Comma-separated flags about the joining peer against the host (Coop.Contract warns, it does not refuse a presence room):
    /// "content" = other mods; "dlc-host-extra=A+B" = the host has DLC the peer lacks (the host's world cannot be loaded by the peer);
    /// "dlc-peer-extra=C" = the peer has DLC the host lacks (it stays out of the shared game).
    /// </summary>
    public static string FlagsOf(NegotiationResult room, bool peerIsRemote = true)
    {
        var flags = new List<string>();
        if (room.Notes.Any(n => n.StartsWith("different game content", StringComparison.Ordinal))) flags.Add("content");
        var hostExtra = peerIsRemote ? room.LocalExtraDlc : room.LocalLacksDlc;     // Negotiate(host, peer): local = host
        var peerExtra = peerIsRemote ? room.LocalLacksDlc : room.LocalExtraDlc;
        if (hostExtra.Count > 0) flags.Add("dlc-host-extra=" + string.Join('+', hostExtra));
        if (peerExtra.Count > 0) flags.Add("dlc-peer-extra=" + string.Join('+', peerExtra));
        return string.Join(',', flags);
    }

    public static string ModeWord(RoomMode m) => m switch { RoomMode.Partial => "partial", RoomMode.SharedSimulation => "shared", _ => "presence" };

    private static bool RejectAndClose()
    {
        Thread.Sleep(30); // the Reject is on the outbox; give the writer a moment before the socket closes
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        foreach (var p in _peers.Values)
        {
            try { p.Client.Close(); } catch { }
        }
        if (_accept != null)
        {
            try { await _accept.ConfigureAwait(false); } catch { }
        }
    }
}
