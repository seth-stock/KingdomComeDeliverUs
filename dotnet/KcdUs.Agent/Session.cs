// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using Coop.Contract;
using KcdUs.Wire;

namespace KcdUs.Agent;

public sealed class SessionOptions
{
    public string Role { get; init; } = "guest";
    public string PlayerName { get; init; } = "Henry";
    public RailsPref Pref { get; init; } = RailsPref.Ask;
    public float TetherMeters { get; init; } = 120f;
    /// <summary>The game-time drift (seconds) past which a guest's clock is set to the host's.</summary>
    public double TimeDriftSeconds { get; init; } = 120;
    public string GameVersion { get; init; } = Release.Current;
    /// <summary>The shared pause (a friend's open pause menu holds this world too). The default; the player can turn it off.</summary>
    public bool SharedPause { get; init; } = true;
    /// <summary>Shared fights and loot with the friends in the same shared world (default on).</summary>
    public bool SharedOutcomes { get; init; } = true;
    /// <summary>Where the host's loot decisions are journalled (empty: not durable, tests only).</summary>
    public string LootJournalPath { get; init; } = "";
    /// <summary>The engine adapter's pause gate (pause option off: this player's own ESC menu does not pause their game). Null: not available.</summary>
    public IPauseGate? PauseGate { get; init; }
}

/// <summary>A player the relay told us about, with the last thing we heard of them.</summary>
public sealed class PeerInfo
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "guest";
    public PlayerState? State { get; set; }
    public long StateAtMs { get; set; }
    public bool InWorld { get; set; }
    public RailsChoice Choice { get; set; } = RailsChoice.Pending;
}

/// <summary>
/// The agent's brain. The game's mod tells it what the local player does (KCDUS lines); the relay tells it what the others do. It
/// puts the others into the game (P records, notes, chat, the shared time) and the local player's state, chat and story facts onto
/// the relay. All of the logic is here and takes its clock and both links from outside, so a test drives it with a fake game, a real
/// relay and a fake clock. Every entry point takes the same lock.
/// </summary>
public sealed class Session : IDisposable
{
    public const int StateStaleMs = 3000;
    public const int BeatRepeatMs = 30_000;
    public const int GameSilentMs = 6000;
    /// <summary>After the world loads (or the relay welcomes us) the quest snapshot is the baseline, not news.</summary>
    public const int BaselineMs = 6000;
    /// <summary>A friend whose host has not repeated its beat for this long takes the host's section as over.</summary>
    public const int HostBeatSilentMs = 90_000;

    private readonly object _gate = new();
    private readonly SessionOptions _o;
    private readonly IGameLink _game;
    private readonly IRelayLink _relay;
    private readonly Func<long> _now;
    private readonly Action<string> _log;

    private readonly Dictionary<int, PeerInfo> _peers = new();
    private int _myId;
    private int _hostId;
    private string _serverName = "";
    private string _refused = "";
    private long _lastGameSignalMs;
    private bool _gameKnown;

    // the local player, as the game reports it
    private PlayerState? _local;
    private long _localAtMs;
    private bool _inWorld;
    private string _gameModVersion = "";
    private string? _localOutfit;
    private int _gameFps;
    private int _gameErrors;

    // timers
    private long _lastPingMs, _lastRelayPingMs, _lastTimeSendMs, _lastTimeApplyMs, _lastSecondMs, _lastBeatMs, _lastTetherMs, _lastWarnMs;
    private long _pingStamp;
    private int _rttMs = -1;
    private int _pingN;

    // the story
    private readonly StoryLock _story = new();
    private readonly RailsRoster _roster = new();
    private readonly RailsJoiner _joiner = new();
    private readonly Dictionary<string, (bool Started, bool Completed, HashSet<int> Objs)> _quests = new(StringComparer.Ordinal);
    private long _baselineUntilMs;
    private bool _awaitingBaseline;
    private bool _seedPending;
    private long _lastBeatSeenMs;
    private StoryTier _hostTier = StoryTier.Open;      // the tier of the section the host is in (a friend learns it from the beat)
    private string _hostWhy = "";
    private RailsPref _pref;
    private int _questChanges;
    private bool _candidateQuests;
    private string _questEpoch = Guid.NewGuid().ToString("N");
    private string _questHostEpoch = "";
    private readonly PauseCoordinator _pause;
    private readonly NpcCoordinator _npcs;
    private readonly WeatherCoordinator _weather;
    /// <summary>Shared enemies: one owner per NPC near the players (docs/CAPABILITIES.md).</summary>
    public NpcCoordinator Npcs => _npcs;
    private readonly OutcomesCoordinator _outcomes;
    private OperationJournal? _lootJournal;

    /// <summary>The shared fights and loot with the friends who are in the same shared world.</summary>
    public OutcomesCoordinator Outcomes => _outcomes;

    /// <summary>The id of the shared world this player has loaded (the world registry's active world); "" when none. Set by the agent.</summary>
    public Func<string?>? ScopeProvider { get; set; }

    private OperationJournal? LootJournal()
    {
        if (_lootJournal is not null || _o.LootJournalPath.Length == 0) return _lootJournal;
        try
        {
            _lootJournal = new OperationJournal(_o.LootJournalPath);
            foreach (var q in _lootJournal.Recover()) _log($"loot: an ask was interrupted ({q.OperationId}); it will never be granted again");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log("loot: the decision journal could not be opened (" + e.Message + "): loot is not shared this session");
            _lootDisabled = true;
        }
        return _lootJournal;
    }
    private bool _lootDisabled;
    private bool _questAuto;

    /// <summary>The shared pause: a friend's open pause menu holds this world too (the player's option; see docs/CAPABILITIES.md).</summary>
    public PauseCoordinator Pause => _pause;

    public Session(SessionOptions o, IGameLink game, IRelayLink relay, Func<long> nowMs, Action<string>? log = null)
    {
        _o = o; _game = game; _relay = relay; _now = nowMs; _log = log ?? (_ => { });
        _pref = o.Pref;
        _pause = new PauseCoordinator(nowMs, line => _game.Send(line), text => { if (_myId != 0) _relay.Send(MessageType.Event, text); },
            Notify, _log, id => _peers.TryGetValue(id, out var p) ? p.Name : "#" + id, o.PauseGate);
        _pause.SetShared(o.SharedPause);
        _outcomes = new OutcomesCoordinator(nowMs, line => _game.Send(line), text => { if (_myId != 0) _relay.Send(MessageType.Event, text); },
            text => { if (_myId != 0) _relay.Send(MessageType.HostEvent, text); }, Notify, _log, id => _peers.TryGetValue(id, out var p) ? p.Name : "#" + id, LootJournal);
        _outcomes.SetEnabled(o.SharedOutcomes);
        _npcs = new NpcCoordinator(nowMs, line => _game.Send(line), text => { if (_myId != 0) _relay.Send(MessageType.Event, text); },
            text => { if (_myId != 0) _relay.Send(MessageType.HostEvent, text); }, _log);
        _weather=new WeatherCoordinator(nowMs,line=>_game.Send(line),text=>{if(_myId!=0)_relay.Send(MessageType.HostEvent,text);});
        _game.Line += OnGameLine;
        _game.ConsoleStateChanged += OnConsole;
        _relay.Frame += OnRelayFrame;
        _relay.ConnectionChanged += OnRelayConnection;
    }

    /// <summary>Lets go of the game link and the relay link (the Multiplayer tab swaps sessions when the player hosts, joins or leaves).</summary>
    public void Dispose()
    {
        _game.Line -= OnGameLine;
        _game.ConsoleStateChanged -= OnConsole;
        _relay.Frame -= OnRelayFrame;
        _relay.ConnectionChanged -= OnRelayConnection;
        _lootJournal?.Dispose();
    }

    public bool IsHost => _o.Role == "host";
    public int HostId { get { lock (_gate) return _hostId; } }
    /// <summary>The player is in the open world right now (not in a menu or loading).</summary>
    public bool InWorld { get { lock (_gate) return _inWorld; } }

    /// <summary>A shared-world message from another player (docs/SHARED-WORLDS.md): its sender and its fields, f[0] being the kind ("wstamp", "woffer", ...).
    /// Raised inside the session's lock: handlers must only queue the work.</summary>
    public event Action<int, string[]>? WorldEvent;

    /// <summary>Kinds the shared-world code sends; any other event kind is not passed on.</summary>
    public static bool IsWorldKind(string kind) => kind is "wstamp" or "wnew" or "wreq" or "woffer" or "wchunk" or "wdone" or "wmiss";

    /// <summary>Sends an event to the other players (false when not connected).</summary>
    public bool SendEvent(string text)
    {
        lock (_gate)
        {
            if (_myId == 0) return false;
            _relay.Send(MessageType.Event, text);
            return true;
        }
    }
    public int MyId { get { lock (_gate) return _myId; } }

    // ================================================================ the game's side

    private void OnConsole(bool up)
    {
        lock (_gate)
        {
            if (up)
            {
                _log("the game's remote console is connected");
                _game.Send("HELLO?");
            }
            else
            {
                _log("the game's remote console went away");
                _inWorld = false;
                _gameKnown = false;
            }
        }
    }

    public void OnGameLine(GameLine g)
    {
        lock (_gate)
        {
            long now = _now();
            _lastGameSignalMs = now;
            var f = g.Fields;   // f[0] is the kind
            switch (g.Kind)
            {
                case "HELLO":
                    _gameKnown = true;
                    _gameModVersion = f.Length > 1 ? f[1] : "";
                    _log($"game mod {_gameModVersion} (protocol {(f.Length > 2 ? f[2] : "?")})");
                    if (!Release.Same(_gameModVersion, _o.GameVersion) && _gameModVersion.Length > 0)
                        _log($"WARNING: the game's mod is {_gameModVersion} and this agent is {_o.GameVersion}: reinstall");
                    break;

                case "READY":
                    {
                        bool w = f.Length > 1 && f[1] == "1";
                        if (w == _inWorld) break;
                        _inWorld = w;
                        if (w) _game.Send("PAUSEWATCH");                // the pause menu's open/close events (mod/kcdus/lua/pause.lua)
                        else { _pause.Reset("the player left the world"); _outcomes.Reset("the player left the world"); _npcs.Reset("the player left the world"); _weather.Reset(); }
                        _questHostEpoch = "";
                        _questEpoch = Guid.NewGuid().ToString("N");
                        if (_candidateQuests) _game.Send("QMRESET|" + (IsHost ? _questEpoch : ""));
                        _log(w ? "the player is in the world" : "the player left the world (menu or loading)");
                        if (w)
                        {
                            _game.Send("ADAPTER?");   // is the engine adapter in this process? (rooms are told honestly)
                            _quests.Clear();
                            _story.Reset();
                            _awaitingBaseline = true;
                            _seedPending = true;
                            _baselineUntilMs = now + BaselineMs;
                            _game.Send("QSNAP");
                            if (_myId != 0) _relay.Send(MessageType.Event, "world|1");
                        }
                        else
                        {
                            _localOutfit=null;
                            _local = null;
                            if (_myId != 0) _relay.Send(MessageType.Event, "world|0");
                            _story.Reset();
                            _roster.End();
                            _joiner.Reset();
                        }
                        break;
                    }

                case "ST":
                    {
                        var s = PlayerState.TryDecode(f, 1);   // KCDUS|ST|pos|yaw|vel|flags|hp|stam|anim|wt : f[0] is "ST", f[1] the position
                        if (s is null) break;
                        _local = s;
                        _localAtMs = now;
                        if (!_inWorld) _inWorld = true;
                        if (_myId != 0) _relay.Send(MessageType.State, s.Encode());
                        break;
                    }

                case "OUT":
                    if(_inWorld && f.Length==2 && OutfitSnapshot.Valid(f[1]))
                    {
                        _localOutfit=f[1];
                        if(_myId!=0)_relay.Send(MessageType.Event,"outfit|"+f[1]);
                    }
                    break;

                case "HB":
                    // HB|lastSeq|fps|ticks|errors
                    _gameFps = f.Length > 2 && int.TryParse(f[2], out var fps) ? fps : _gameFps;
                    _gameErrors = f.Length > 4 && int.TryParse(f[4], out var er) ? er : _gameErrors;
                    break;

                case "CHAT":
                    if (f.Length > 1 && _myId != 0) _relay.Send(MessageType.Chat, string.Join(' ', f.Skip(1)));
                    break;

                case "KEY":
                    if (f.Length > 1) Choose(f[1] == "join" ? RailsChoice.Join : RailsChoice.Free, "key");
                    break;

                case "Q":
                    if (f.Length >= 4) OnQuest(f[1], f[2] == "1", f[3] == "1", f.Length > 4 ? f[4] : "", now);
                    break;
                case "QMODE":
                    _candidateQuests = f.Length > 1 && f[1] == "candidate";
                    if (IsHost && _myId != 0) _relay.Send(MessageType.HostEvent, "quest-epoch|" + _questEpoch);
                    _game.Send("QMRESET|" + (IsHost ? _questEpoch : _questHostEpoch));
                    break;
                case "QM":
                    if (_candidateQuests && IsHost && _inWorld && _myId != 0 && f.Length == 5
                        && QuestMirrorRules.Valid(f[1], f[2], f[3], f[4]))
                    {
                        _relay.Send(MessageType.HostEvent, "quest-epoch|" + _questEpoch);
                        _relay.Send(MessageType.HostEvent, $"quest-mirror|{_questEpoch}|{f[1]}|{f[2]}|{f[3]}|{f[4]}");
                    }
                    break;
                case "QREWARD":
                    // (host) a mirrored quest step paid this: the guests in the same shared world get it too (rewards.lua takes off what their game paid already)
                    if (_candidateQuests && IsHost && _inWorld && _myId != 0 && f.Length == 4 && QuestMirrorRules.ValidReward(f[1], f[2], f[3]))
                        _relay.Send(MessageType.HostEvent, $"qreward|{_questEpoch}|{f[1]}|{f[2]}|{f[3]}");
                    break;
                case "QRGIVEN":
                    _log("quest reward: " + string.Join(' ', f.Skip(1)));
                    break;
                case "QMAPPLIED":
                    _log("Candidate quest readback: " + string.Join(' ', f.Skip(1)));
                    break;

                case "CMB": _outcomes.GameCombat(f); break;
                case "LOOT": _outcomes.GameLoot(f, IsHost); break;
                case "NPCNEAR": _npcs.GameNear(f, IsHost); break;
                case "NPCST": _npcs.GameStates(f); break;
                case "WOPT" when f.Length==2 && f[1] is "on" or "off":
                    _weather.Enabled=f[1]=="on";
                    if(!_weather.Enabled) _weather.Reset();
                    break;
                case "NPCPUP":
                    _log("npc puppet: " + string.Join(' ', f.Skip(1)));
                    break;
                case "CMBAPPLIED" or "LOOTAPPLIED" or "CMBMODE" or "LOOTMODE":
                    _log(g.Kind.ToLowerInvariant() + ": " + string.Join(' ', f.Skip(1)));
                    break;

                case "PAUSE":
                    // PAUSE|menu|0|1|2   this player's pause menu closed / opened / the watcher runs;  PAUSE|frozen|<lease>;  PAUSE|released|<why>
                    if (f.Length >= 3 && f[1] == "menu") { if (f[2] == "1") _pause.LocalMenu(true); else if (f[2] == "0") _pause.LocalMenu(false); }
                    else if (f.Length >= 2 && f[1] is "frozen" or "released") _log("pause: " + string.Join(' ', f.Skip(1)));
                    break;

                case "ERR":
                    _log("game script error: " + string.Join('|', f.Skip(1)));
                    break;

                case "GHOST":
                    _log("ghost " + string.Join(' ', f.Skip(1)));
                    break;
            }
        }
    }

    // ================================================================ the story, host side

    private void OnQuest(string code, bool started, bool completed, string objText, long now)
    {
        var objs = objText.Length == 0 ? new HashSet<int>() : objText.Split(',').Select(x => int.TryParse(x, out var v) ? v : 0).ToHashSet();
        bool had = _quests.TryGetValue(code, out var prev);
        _quests[code] = (started, completed, objs);
        _questChanges++;
        if (_awaitingBaseline)
        {
            if (now < _baselineUntilMs) return;        // what the quest log already held when we looked: not news
            _awaitingBaseline = false;
        }
        if (!IsHost) return;                           // only the host's own story decides what a friend is asked

        var states = new List<string>();
        if (completed && !(had && prev.Completed)) { Note(code, "end", true, now); return; }
        if (started && !(had && prev.Started) && objs.Count == 0) states.Add("start");
        foreach (var id in objs.Where(o => !had || !prev.Objs.Contains(o))) states.Add("o" + id);
        foreach (var st in states) Note(code, st, false, now);
    }

    /// <summary>The host loaded INTO a rails stretch of the main story (a save made mid-battle): there is no change to see, so the quest log says so.</summary>
    private void Seed(long now)
    {
        _seedPending = false;
        foreach (var (code, q) in _quests.ToArray())
            if (StorySections.ByCode(code) is { IsMain: true, Tier: StoryTier.Rails } && q.Started && !q.Completed && q.Objs.Count > 0)
                Note(code, "seed", false, now);
    }

    private void Note(string code, string state, bool end, long now)
    {
        foreach (var t in _story.Note(code, state, end, now))
            OnTransition(t, now);
    }

    private void OnTransition(StoryLock.Transition t, long now)
    {
        var s = t.Section!;
        if (t.Kind == StoryLock.Kind.Enter)
        {
            bool fresh = _roster.Start(s.Period);
            _log($"the host entered {s.Code} ({s.Tier}): {s.Why}{(fresh ? " [new period]" : "")}");
            if (fresh) _roster.SeedPending(Friends().Select(p => (byte)p.Id), now);
            _hostTier = s.Tier;
            _hostWhy = s.Why;
            SendBeat(s, now);
            _lastBeatMs = now;
            if (IsHost) Notify($"Story: {s.Why}");
        }
        else if (t.Kind == StoryLock.Kind.Leave)
        {
            _log($"the host left {s.Code} ({t.Reason})");
            _roster.EndSoon(now);
            _relay.Send(MessageType.HostEvent, $"beat|leave|{s.Code}");
            if (_story.Active is null) _hostTier = StoryTier.Open;
        }
    }

    private void SendBeat(StorySection s, long now) =>
        _relay.Send(MessageType.HostEvent, $"beat|enter|{s.Code}|{s.Tier.ToString().ToLowerInvariant()}|{Safe.Clean(s.Why, 120)}");

    private IEnumerable<PeerInfo> Friends() => _peers.Values.Where(p => p.Id != _myId && p.Role != "host");

    // ================================================================ the relay's side

    private void OnRelayConnection(bool up)
    {
        lock (_gate)
        {
            if (up) { _refused = ""; return; }
            _log("the relay connection went away");
            _myId = 0; _hostId = 0;
            foreach (var p in _peers.Values) _game.Send($"PD|{p.Id}");
            _peers.Clear();
            _joiner.Reset(); _roster.End();
            _game.Send("PROMPTCLEAR");
            Notify("Disconnected from the relay");
        }
    }

    private void OnRelayFrame(Frame fr)
    {
        lock (_gate)
        {
            long now = _now();
            var f = fr.Fields;
            switch (fr.Type)
            {
                case MessageType.Welcome:
                    _myId = int.Parse(f[0], CultureInfo.InvariantCulture);
                    _hostId = int.Parse(f[1], CultureInfo.InvariantCulture);
                    _serverName = f.Length > 4 ? f[4] : "";
                    _roomMissing = f.Length > 6 ? f[6] : "";
                    ApplyMyFlags(f.Length > 7 ? f[7] : "");
                    _peerModes[_myId] = f.Length > 5 ? f[5] : "presence";
                    _log($"welcome: I am #{_myId}, the host is #{_hostId}, \"{_serverName}\", room: {RoomWord()}");
                    Notify($"Connected to {_serverName}. {RoomSentence()}");
                    if (_inWorld) { _relay.Send(MessageType.Event, "world|1"); _game.Send("QSNAP"); _awaitingBaseline = true; _seedPending = true; _baselineUntilMs = now + BaselineMs; }
                    if(_inWorld && _localOutfit!=null)_relay.Send(MessageType.Event,"outfit|"+_localOutfit);
                    break;

                case MessageType.Reject:
                    _refused = fr.Text;
                    _log("REFUSED by the relay: " + fr.Text);
                    Notify("The relay refused you: " + (f.Length > 1 ? f[1] : f[0]));
                    break;

                case MessageType.PlayerList:
                    foreach (var e in fr.Text.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = e.Split(':');
                        if (p.Length >= 3 && int.TryParse(p[0], out var id))
                            _peers[id] = new PeerInfo { Id = id, Name = p[1], Role = p[2] };
                    }
                    break;

                case MessageType.PlayerJoined:
                    {
                        int id = int.Parse(f[0], CultureInfo.InvariantCulture);
                        _peers[id] = new PeerInfo { Id = id, Name = f[1], Role = f[2] };
                        if (f[2] == "host") _hostId = id;
                        if (f.Length > 3) _peerModes[id] = f[3];
                        if (f.Length > 4) ApplyPeerFlags(id, f[1], f[4]);
                        _log($"{f[1]} joined as {f[2]} (room: {RoomWord()})");
                        Notify($"{f[1]} joined");
                        if(_inWorld && _localOutfit!=null)_relay.Send(MessageType.Event,"outfit|"+_localOutfit);
                        break;
                    }

                case MessageType.PlayerLeft:
                    {
                        int id = int.Parse(f[0], CultureInfo.InvariantCulture);
                        _peerModes.Remove(id);
                        _pause.PeerLeft(id);
                        _outcomes.PeerLeft(id);
                        _npcs.PeerLeft(id);
                        if (_peers.Remove(id, out var gone))
                        {
                            _game.Send($"PD|{id}");
                            _roster.Forget((byte)id);
                            _log($"{gone.Name} left");
                            Notify($"{gone.Name} left");
                        }
                        if (id == _hostId)
                        {
                            _hostId = 0;
                            _hostTier = StoryTier.Open;
                            _joiner.ForceOver();
                            _game.Send("PROMPTCLEAR");
                        }
                        break;
                    }

                case MessageType.PState:
                    {
                        int id = int.Parse(f[0], CultureInfo.InvariantCulture);
                        var s = PlayerState.TryDecode(f, 1);
                        if (s is null || !_peers.TryGetValue(id, out var p)) break;
                        p.State = s; p.StateAtMs = now; p.InWorld = true;
                        _game.SendLatest("P" + id, $"P|{id}|{Safe.Name(p.Name)}|{s.Encode()}");
                        break;
                    }

                case MessageType.PChat:
                    if (f.Length >= 3) _game.Send($"CHAT|{Safe.Name(f[1])}|{Safe.Clean(string.Join(' ', f.Skip(2)), 160)}");
                    break;

                case MessageType.Pong:
                    if (long.TryParse(fr.Text, out var stamp)) _rttMs = (int)Math.Max(0, now - stamp);
                    break;

                case MessageType.PEvent:
                    OnPeerEvent(int.Parse(f[0], CultureInfo.InvariantCulture), f.Skip(1).ToArray(), now);
                    break;

                case MessageType.PHostEvent:
                    OnHostEvent(int.Parse(f[0], CultureInfo.InvariantCulture), f.Skip(1).ToArray(), now);
                    break;
            }
        }
    }

    private void OnPeerEvent(int from, string[] f, long now)
    {
        if (!_peers.TryGetValue(from, out var p)) return;
        if (IsWorldKind(f[0])) { WorldEvent?.Invoke(from, f); return; }
        switch (f[0])
        {
            case "pause" when f.Length == 2 && f[1] is "0" or "1":
                _pause.PeerMenu(from, f[1] == "1");
                break;
            case "scope" when f.Length == 2:
                _outcomes.PeerScope(from, f[1] == "-" ? "" : f[1]);
                break;
            case "cmb":
                _outcomes.PeerCombat(from, f);
                break;
            case "loot" when IsHost && f.Length == 7:
                _outcomes.PeerAsk(from, f);
                break;
            case "loot" when f.Length == 6 && f[1] is "put" or "drop":
                _outcomes.PeerPutDrop(from, f);
                break;
            case "npcnear":
                _npcs.PeerNear(from, f, IsHost);
                break;
            case "npcst":
                _npcs.PeerStates(from, f);
                break;
            case "npcst2":
                _npcs.PeerStatesV2(from,f);
                break;
            case "outfit" when f.Length==2 && OutfitSnapshot.Valid(f[1]):
                if(_inWorld)_game.SendLatest("OUT"+from,$"OUT|{from}|{f[1]}");
                break;
            case "world":
                if (f.Length > 1 && f[1] == "0")
                {
                    p.InWorld = false; p.State = null;
                    _game.Send($"PD|{from}");
                }
                break;

            case "choice" when IsHost && f.Length > 1:
                if (RailsRules.TryParseChoice(f[1], out var period, out var choice))
                {
                    var outcome = _roster.Choose((byte)from, period.Id, choice, now);
                    if (outcome is RailsRoster.Outcome.Joined or RailsRoster.Outcome.Freed)
                    {
                        p.Choice = choice;
                        _log($"{p.Name} chose to {(choice == RailsChoice.Join ? "join" : "stay in the open world")} for {period.Id}");
                        Notify(choice == RailsChoice.Join ? $"{p.Name} joined you" : $"{p.Name} stays in the open world");
                    }
                }
                break;
        }
    }

    private void OnHostEvent(int from, string[] f, long now)
    {
        if (from != _hostId || IsHost) return;
        switch (f[0])
        {
            case "quest-epoch" when f.Length == 2 && Guid.TryParseExact(f[1], "N", out _):
                if (_candidateQuests) { _questHostEpoch = f[1]; _game.Send("QMRESET|" + f[1]); }
                break;
            case "qreward" when f.Length == 5:
                if (_candidateQuests && _inWorld && !_contentDiffers && f[1] == _questHostEpoch && QuestMirrorRules.ValidReward(f[2], f[3], f[4]))
                    _game.Send($"QRGIVE|{f[2]}|{f[3]}|{f[4]}");
                break;
            case "quest-mirror" when f.Length == 6:
                if (_candidateQuests && _inWorld && !_contentDiffers && f[1] == _questHostEpoch
                    && QuestMirrorRules.Valid(f[2], f[3], f[4], f[5]))
                    _game.Send("QMAPPLY|" + string.Join('|', f.Skip(1)));
                break;
            case "time" when f.Length > 1 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var wt):
                ApplyTime(wt, now);
                break;
            case "loot":
                _outcomes.HostLoot(f, _myId);
                break;
            case "npcown":
                _npcs.HostOwners(f);
                break;
            case "npcown2":
                _npcs.HostOwnersV2(f);
                break;
            case "weather":
                _weather.HostWeather(f);
                break;

            case "beat" when f.Length >= 3:
                if (f[1] == "enter")
                {
                    var tier = f.Length > 3 ? f[3] : "mixed";
                    _hostTier = tier == "rails" ? StoryTier.Rails : tier == "mixed" ? StoryTier.Mixed : StoryTier.Open;
                    _hostWhy = f.Length > 4 ? f[4] : "";
                    _lastBeatSeenMs = now;
                    var step = _joiner.OnEnter(f[2], now, _pref);
                    DoStep(step, f[2], now);
                }
                else if (f[1] == "leave")
                {
                    _joiner.OnLeave(f[2], now);
                }
                break;
        }
    }

    // ================================================================ the story, friend side

    private void DoStep(RailsJoiner.Step step, string code, long now)
    {
        switch (step.Act)
        {
            case RailsJoiner.Act.Ask:
                _game.Send($"PROMPT|{Safe.Clean(RailsRules.Ask(_hostWhy), 150)} F11 join, F12 stay.|{RailsRules.PromptSeconds}");
                _log($"asked: join the host in \"{_hostWhy}\"?");
                break;

            case RailsJoiner.Act.Auto:
                Announce(step.Choice, code, step.Why);
                break;

            case RailsJoiner.Act.Backstop:
                // nobody answered: that is a yes, and it is an ANSWER (the joiner leaves "pending", or this would fire every second)
                if (_joiner.Decide(step.Choice, now)) Announce(step.Choice, code, step.Why);
                break;

            case RailsJoiner.Act.Resend:
                if (step.Period is not null) _relay.Send(MessageType.Event, "choice|" + RailsRules.ChoiceText(step.Period.Id, step.Choice));
                break;

            case RailsJoiner.Act.Over:
                _game.Send("PROMPTCLEAR");
                if (step.Choice == RailsChoice.Free) Notify("Your host's story stretch is over");
                _log("the period is over");
                break;
        }
    }

    /// <summary>Tell the host, tell the player, and bring them along if they joined.</summary>
    private void Announce(RailsChoice c, string code, string why)
    {
        var p = StorySections.PeriodOf(code);
        if (p is not null) _relay.Send(MessageType.Event, "choice|" + RailsRules.ChoiceText(p.Id, c));
        _game.Send("PROMPTCLEAR");
        if (c == RailsChoice.Join)
        {
            Notify(RailsRules.JoinedText);
            BringToHost();
        }
        else
        {
            Notify("You stay in the open world. F11 joins your host at any time.");
        }
        _log($"answer: {RailsRules.ChoiceName(c)}{(why.Length > 0 ? " (" + why + ")" : "")}");
    }

    /// <summary>The player's answer: F11, F12, the launcher, a console command, or the question timing out.</summary>
    public bool Choose(RailsChoice c, string source)
    {
        lock (_gate)
        {
            long now = _now();
            if (IsHost || !_joiner.Open || !_joiner.Decide(c, now)) return false;
            string code = _joiner.Period ?? "";
            Announce(c, code, source);
            return true;
        }
    }

    private void BringToHost()
    {
        if (_peers.TryGetValue(_hostId, out var h) && h.State is { } hs && _now() - h.StateAtMs < 10_000)
        {
            var o = (_myId % 4) * 0.8;   // a little apart, so two friends do not stand in each other
            _game.Send(string.Create(CultureInfo.InvariantCulture, $"TP|{hs.X + 1.5 + o:0.00},{hs.Y:0.00},{hs.Z:0.00}|{hs.Yaw:0.000}"));
        }
    }

    /// <summary>The game only moves its clock FORWARD (its own scripts do SetWorldTime(now + hours*3600); a request for an earlier time is ignored: seen live).
    /// So a friend is aligned to the host's TIME OF DAY by skipping forward, at most <see cref="MaxTimeSkipSeconds"/>; one who would need more keeps their own clock.</summary>
    public const double MaxTimeSkipSeconds = 12 * 3600;
    private long _lastNoSyncLogMs;

    /// <summary>The seconds to skip forward to share the host's time of day, or null when already within the drift or when it would be too long a skip.</summary>
    public static double? TimeSkip(double localTime, double hostTime, double driftSeconds)
    {
        const double day = 86400;
        double forward = ((hostTime - localTime) % day + day) % day;
        if (forward < driftSeconds || forward > day - driftSeconds) return null;   // the same time of day, near enough
        return forward <= MaxTimeSkipSeconds ? forward : null;
    }

    private void ApplyTime(double hostTime, long now)
    {
        if (_local is null || !_inWorld || _local.InDialog) return;
        if (now - _lastTimeApplyMs < 20_000) return;
        double forward = ((hostTime - _local.WorldTime) % 86400 + 86400) % 86400;
        if (forward < _o.TimeDriftSeconds || forward > 86400 - _o.TimeDriftSeconds) return;
        if (TimeSkip(_local.WorldTime, hostTime, _o.TimeDriftSeconds) is not { } skip)
        {
            if (now - _lastNoSyncLogMs > 120_000)
            {
                _lastNoSyncLogMs = now;
                _log($"the clock: your game is {(86400 - forward) / 3600:0.0} h ahead of the host's time of day; the game cannot turn its clock back, so yours stays");
            }
            return;
        }
        _lastTimeApplyMs = now;
        _log($"the clock: skipping {skip / 3600:0.0} h forward to the host's time of day");
        _game.Send(string.Create(CultureInfo.InvariantCulture, $"TIME|{_local.WorldTime + skip:0}"));
    }

    /// <summary>A line of text on the player's screen in the game.</summary>
    public void Notify(string text) => _game.Send("NOTE|" + Safe.Clean(text, 160));

    // ================================================================ the clock

    /// <summary>Call about ten times a second.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            long now = _now();
            _pause.Tick(_inWorld, _myId != 0 && _relay.Connected, _gameFps, _peers.Values.Count(p => p.Id != _myId && p.InWorld));
            _outcomes.Tick(_inWorld, _myId != 0 && _relay.Connected, IsHost, _lootDisabled ? "" : (ScopeProvider?.Invoke() ?? ""));
            _npcs.Tick(_outcomes.Active && _inWorld ? _outcomes.Scope : "", IsHost, _myId);
            _weather.Tick(_outcomes.Active && _inWorld ? _outcomes.Scope : "",IsHost,
                IsHost ? _story.Active is null : _hostTier==StoryTier.Open);
            if (_outcomes.Active != _questAuto)
            {
                // the host's quest progress follows into the guests' copies of the same shared world (one way, vetoes in QuestMirrorRules / quest_mirror.lua)
                _questAuto = _outcomes.Active;
                _game.Send((_questAuto ? "QMODE|candidate|" : "QMODE|off|") + (IsHost ? _questEpoch : _questHostEpoch));
            }
            if (now - _lastPingMs >= 2000)
            {
                _lastPingMs = now;
                _game.Send("PING|" + (++_pingN));
            }
            if (_myId != 0 && now - _lastRelayPingMs >= 5000)
            {
                _lastRelayPingMs = now;
                _relay.Send(MessageType.Ping, now.ToString(CultureInfo.InvariantCulture));
            }
            if (IsHost && _myId != 0 && _local is not null && now - _lastTimeSendMs >= 5000 && now - _localAtMs < StateStaleMs)
            {
                _lastTimeSendMs = now;
                _relay.Send(MessageType.HostEvent, string.Create(CultureInfo.InvariantCulture, $"time|{_local.WorldTime:0}"));
            }
            if (now - _lastSecondMs < 1000) return;
            _lastSecondMs = now;
            Second(now);
        }
    }

    private void Second(long now)
    {
        // the host: a section the host has been idle in is over; the active one is repeated so a friend who arrives mid-way hears it
        if (IsHost)
        {
            if (_awaitingBaseline && now >= _baselineUntilMs) _awaitingBaseline = false;   // the snapshot window is over even if nothing more arrived
            if (_seedPending && !_awaitingBaseline && _inWorld) Seed(now);
            if (_story.Tick(now) is { } t) OnTransition(new StoryLock.Transition(t.Kind, t.Section, t.Reason), now);
            if (_story.Active is { } a && now - _lastBeatMs >= BeatRepeatMs) { SendBeat(a, now); _lastBeatMs = now; }
            foreach (var id in _roster.Tick(now, Friends().Select(p => (byte)p.Id)))
                if (_peers.TryGetValue(id, out var p)) { p.Choice = RailsChoice.Join; _log($"{p.Name} did not answer: taken as joined"); }
        }
        else
        {
            if (_joiner.Open && _lastBeatSeenMs != 0 && now - _lastBeatSeenMs > HostBeatSilentMs && _joiner.ForceOver() is { })
            {
                _game.Send("PROMPTCLEAR");
                _log("the host's beat stopped: the period is over");
                _hostTier = StoryTier.Open;
            }
            DoStep(_joiner.Tick(now), _joiner.Period ?? "", now);
            Tether(now);
        }
    }

    /// <summary>A friend who joined a RAILS section stays within the tether of the host: warned inside it, pulled back past it.</summary>
    private void Tether(long now)
    {
        if (!_joiner.Open || _joiner.Choice != RailsChoice.Join || _hostTier != StoryTier.Rails) return;
        if (_local is null || !_inWorld || now - _localAtMs > StateStaleMs) return;
        if (!_peers.TryGetValue(_hostId, out var h) || h.State is not { } hs || now - h.StateAtMs > StateStaleMs) return;
        double d = Math.Sqrt(Math.Pow(hs.X - _local.X, 2) + Math.Pow(hs.Y - _local.Y, 2) + Math.Pow(hs.Z - _local.Z, 2));
        var (warn, pull) = StoryLock.Tether(650f, 700f, true, _o.TetherMeters);
        if (d > pull && now - _lastTetherMs >= 10_000)
        {
            _lastTetherMs = now;
            Notify("Your host is on rails: you were brought back to them");
            BringToHost();
        }
        else if (d > warn && d <= pull && now - _lastWarnMs >= 20_000)
        {
            _lastWarnMs = now;
            Notify("You are straying from your host");
        }
    }

    // ================================================================ what the launcher and the tests look at

    public sealed record Status(
        string Version, string Role, bool GameConsole, bool GameKnown, bool GameResponding, string GameModVersion, bool InWorld, int Fps, int ScriptErrors,
        bool RelayConnected, string Refused, int MyId, int HostId, string ServerName, int RttMs,
        IReadOnlyList<PeerStatus> Players, StoryStatus Story, string Message, string RoomMode = "presence", string RoomNote = "");

    public sealed record PeerStatus(int Id, string Name, string Role, bool InView, string Choice, double? DistanceM);

    public sealed record StoryStatus(string Section, string Tier, string Why, string Period, string MyChoice, bool Asking, int Joined, int Staying, int Deciding, int QuestChanges);

    // ================================================================ what kind of room this is (Coop.Contract negotiation, done by the relay)

    private readonly Dictionary<int, string> _peerModes = new();
    private string _roomMissing = "";
    private bool _contentDiffers;
    private IReadOnlyList<string> _dlcLacks = Array.Empty<string>();
    private IReadOnlyList<string> _dlcExtra = Array.Empty<string>();
    private readonly Dictionary<int, IReadOnlyList<string>> _peerLacksDlc = new();

    /// <summary>False when a player in the room has other MODS: a world or a Henry must not be moved between differently-modded installs.</summary>
    public bool ContentMatches { get { lock (_gate) return !_contentDiffers; } }

    /// <summary>The DLC the host has active that this game lacks. A world the host saved names its DLC, and the engine will not load it without them.</summary>
    public IReadOnlyList<string> DlcLacks { get { lock (_gate) return _dlcLacks; } }

    /// <summary>The DLC this game has active that the host's lacks. It stays out of the shared game: the room is played as the host's, with the least DLC.</summary>
    public IReadOnlyList<string> DlcExtra { get { lock (_gate) return _dlcExtra; } }

    /// <summary>The DLC the host has that player <paramref name="id"/> lacks (the host asks before serving a world).</summary>
    public IReadOnlyList<string> PeerLacksDlc(int id) { lock (_gate) return _peerLacksDlc.TryGetValue(id, out var l) ? l : Array.Empty<string>(); }

    private static IReadOnlyList<string> FlagNames(string flags, string key)
    {
        foreach (var tok in flags.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (tok.StartsWith(key + "=", StringComparison.Ordinal)) return tok[(key.Length + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries);
        return Array.Empty<string>();
    }

    private const string SteamDlcHint = "To play exactly like your host, turn that DLC off in Steam (Properties, DLC) and restart the game.";

    /// <summary>What the Welcome says about me against the host.</summary>
    private void ApplyMyFlags(string flags)
    {
        _contentDiffers = flags.Split(',').Contains("content");
        _dlcLacks = FlagNames(flags, "dlc-host-extra");
        _dlcExtra = FlagNames(flags, "dlc-peer-extra");
        if (_dlcExtra.Count > 0) { Notify($"Your game has DLC your host's does not ({string.Join(", ", _dlcExtra)}): it stays out of what you share."); Notify(SteamDlcHint); }
        if (_dlcLacks.Count > 0) { Notify($"Your host's game has DLC you do not have ({string.Join(", ", _dlcLacks)}): their world needs it."); Notify("Get that DLC, or ask your host to play without it. Their world cannot be moved to you."); }
        if (_contentDiffers) Notify("Your game has other mods than your host's: worlds and characters will not be moved between you.");
    }

    /// <summary>What the relay says about a player who just joined (against the host).</summary>
    private void ApplyPeerFlags(int id, string name, string flags)
    {
        if (flags.Split(',').Contains("content")) { _contentDiffers = true; Notify($"{name}'s game has other mods: worlds and characters will not be moved between you."); }
        var lacks = FlagNames(flags, "dlc-host-extra");
        if (lacks.Count > 0) { _peerLacksDlc[id] = lacks; Notify($"{name} lacks DLC the host has ({string.Join(", ", lacks)}): your world cannot be moved to them."); }
        var extra = FlagNames(flags, "dlc-peer-extra");
        if (extra.Count > 0) Notify($"{name} has DLC your game does not ({string.Join(", ", extra)}); it stays out of what you share.");
    }

    /// <summary>The room's mode: the weakest negotiated mode among the players (presence, partial or shared).</summary>
    private string RoomWord()
    {
        if (_peerModes.Count == 0) return "presence";
        if (_peerModes.Values.Any(m => m == "presence")) return "presence";
        return _peerModes.Values.Any(m => m == "partial") ? "partial" : "shared";
    }

    /// <summary>Plain words, never "shared" for a room that only shows peers.</summary>
    private string RoomSentence() => RoomSentenceBase() + (_outcomes.Active ? " Fights and loot are shared with the friends in your world." : "");

    private string RoomSentenceBase() => RoomWord() switch
    {
        "shared" => "Room: shared simulation.",
        "partial" => "Room: partly shared (not every authority capability is verified" + (_roomMissing.Length > 0 ? ": " + _roomMissing.Replace(",", ", ") : "") + ").",
        _ => "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active" + (_roomMissing.Length > 0 ? " (" + _roomMissing.Replace(",", ", ") + ")" : "") + ".",
    };

    public string RoomMode { get { lock (_gate) return RoomWord(); } }
    public string RoomNote { get { lock (_gate) return RoomSentence(); } }

    public Status GetStatus()
    {
        lock (_gate)
        {
            long now = _now();
            bool responding = _gameKnown && now - _lastGameSignalMs < GameSilentMs;
            var players = _peers.Values.OrderBy(p => p.Id).Select(p =>
            {
                double? d = null;
                if (p.State is { } s && _local is { } l) d = Math.Round(Math.Sqrt(Math.Pow(s.X - l.X, 2) + Math.Pow(s.Y - l.Y, 2) + Math.Pow(s.Z - l.Z, 2)), 1);
                return new PeerStatus(p.Id, p.Name, p.Role, p.InWorld && p.State is not null, RailsRules.ChoiceName(p.Choice), d);
            }).ToList();

            string section = IsHost ? _story.Active?.Code ?? "" : _joiner.Open ? (_joiner.Period ?? "") : "";
            string why = IsHost ? _story.Active?.Why ?? "" : _hostWhy;
            var story = new StoryStatus(section, IsHost ? (_story.Active?.Tier.ToString().ToLowerInvariant() ?? "open") : _hostTier.ToString().ToLowerInvariant(), why,
                IsHost ? _roster.Period ?? "" : _joiner.Period ?? "", RailsRules.ChoiceName(_joiner.Choice), _joiner.Asking,
                _roster.Count(RailsChoice.Join), _roster.Count(RailsChoice.Free), _roster.Count(RailsChoice.Pending), _questChanges);

            string message;
            if (_refused.Length > 0) message = "The relay refused you: " + _refused.Split('|').Last();
            else if (!_game.ConsoleConnected) message = "Waiting for the game. Start it from the launcher.";
            else if (!_gameKnown) message = "The game is running but the co-op mod did not answer. Is the mod installed?";
            else if (!_inWorld) message = "In the main menu or loading. Load your save.";
            else if (!_relay.Connected) message = "Not connected to a relay.";
            else message = $"Connected as {_o.Role}: {_peers.Count + 1} player(s). {RoomSentence()}";

            return new Status(typeof(Session).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0", _o.Role, _game.ConsoleConnected, _gameKnown,
                responding, _gameModVersion, _inWorld, _gameFps, _gameErrors, _relay.Connected, _refused, _myId, _hostId, _serverName, _rttMs, players, story, message, RoomWord(), RoomSentence());
        }
    }

    public IReadOnlyList<PeerInfo> Peers { get { lock (_gate) return _peers.Values.ToList(); } }

    /// <summary>The player typed something in the launcher: it goes to the others and is echoed in the game.</summary>
    public void Say(string text)
    {
        lock (_gate)
        {
            text = Safe.Clean(text, 200);
            if (text.Length == 0) return;
            if (_myId != 0) _relay.Send(MessageType.Chat, text);
            _game.Send("CHAT|You|" + text);
        }
    }

    public void SetPref(RailsPref p) { lock (_gate) _pref = p; }
}
