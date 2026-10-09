// SPDX-License-Identifier: GPL-3.0-only
// The shared pause (docs/CAPABILITIES.md, "Pausing"). When one player opens the game's pause menu, the others' worlds are held for as long as that menu is open.
// The game half (mod/kcdus/lua/pause.lua) slows the world to t_scale 0.001 and lets go BY ITSELF after a frame-counted lease; this half asks again every two
// seconds while a friend's menu is open, so a crashed agent, a lost link or a friend who never comes back can never leave a world frozen for good.
//
// Only the pause MENU counts (a friend's inventory, a dialogue or a loading screen never hold anyone).
//
// The option, as in Kingdom Come: Together (KCD2):
//   * shared (the default): my ESC menu pauses my game, as in the unmodded game, and holds my friends' worlds too; a friend's menu holds mine;
//   * off: while a friend is in the world with me, my ESC menu opens WITHOUT pausing my game (the engine adapter's gate, PauseGate.cs) and my inventory does
//     not slow time; nobody's menu holds anybody. Without the adapter (Linux, or a game started without the launcher) my own menu still pauses my game.
namespace KcdUs.Agent;

public sealed class PauseCoordinator
{
    public const int LinkTimeoutMs = 6000;
    public const long MaxHoldMs = 15 * 60 * 1000;
    public const int HeartbeatMs = 2000;
    /// <summary>The game counts frames, not seconds (its clocks stop with the world). 12 s of frames at the current frame rate, never fewer than 240 or more than 6000.</summary>
    public static int LeaseFrames(int fps) => Math.Clamp((fps <= 0 ? 60 : fps) * 12, 240, 6000);

    private readonly Func<long> _now;
    private readonly Action<string> _toGame;
    private readonly Action<string> _toPeers;
    private readonly Action<string> _notify;
    private readonly Action<string> _log;
    private readonly Func<int, string> _nameOf;
    private readonly object _gate = new();
    private readonly Dictionary<int, (long Since, long Seen)> _peerMenus = new();
    private readonly IPauseGate? _ownGate;
    private bool _menuOpen, _frozen, _levers, _gateMissingLogged;
    private long _lastBeatMs, _lastAskMs, _lastGateMs, _lastOwnSentMs;

    public PauseCoordinator(Func<long> now, Action<string> toGame, Action<string> toPeers, Action<string> notify, Action<string> log, Func<int, string> nameOf,
        IPauseGate? ownGate = null)
    {
        _now = now; _toGame = toGame; _toPeers = toPeers; _notify = notify; _log = log; _nameOf = nameOf; _ownGate = ownGate;
    }

    /// <summary>The ESC menu of THIS player does not pause this game right now (option off, a friend in the world, the adapter's gate is on).</summary>
    public bool OwnMenuRuns { get { lock (_gate) return _levers; } }
    /// <summary>The engine adapter's gate is loaded in the game (otherwise "off" cannot stop this player's own menu from pausing).</summary>
    public bool OwnGateAvailable => _ownGate?.Available == true;

    /// <summary>The player's choice. Off: nobody's menu holds anybody's world, and this player's menu is not announced.</summary>
    public bool Shared { get; private set; } = true;
    public bool Frozen { get { lock (_gate) return _frozen; } }

    public void SetShared(bool shared)
    {
        lock (_gate)
        {
            Shared = shared;
            if (!shared) { ReleaseLocked("option off"); if (_menuOpen) _toPeers("pause|0"); }
            else if (_menuOpen) { _lastBeatMs = 0; }
        }
    }

    /// <summary>This player's own pause menu opened or closed (the game said so).</summary>
    public void LocalMenu(bool open)
    {
        lock (_gate)
        {
            if (_menuOpen == open) return;
            _menuOpen = open;
            if (!Shared) return;
            _toPeers(open ? "pause|1" : "pause|0");
            _lastBeatMs = _now();
        }
    }

    /// <summary>A friend's pause menu opened (or is still open: a heartbeat) or closed.</summary>
    public void PeerMenu(int id, bool open)
    {
        lock (_gate)
        {
            long now = _now();
            if (!open) { _peerMenus.Remove(id); return; }
            _peerMenus[id] = _peerMenus.TryGetValue(id, out var e) ? (e.Since, now) : (now, now);
        }
    }

    public void PeerLeft(int id) { lock (_gate) { _peerMenus.Remove(id); } }

    /// <summary>The player left the world, or the relay went away: nothing may hold this world any more.</summary>
    public void Reset(string why)
    {
        lock (_gate) { _peerMenus.Clear(); ReleaseLocked(why); _menuOpen = false; SetLeversLocked(false, _now()); }
    }

    // the levers: my own ESC menu does not pause my game (KCD2 says "levers" too). A heartbeat: the adapter lets go by itself ten seconds after the last one.
    private void SetLeversLocked(bool on, long now)
    {
        if (on && _ownGate != null && now - _lastGateMs >= 1000) { _lastGateMs = now; _ownGate.Apply(true); }
        if (on && on == _levers && now - _lastOwnSentMs >= 10000) { _lastOwnSentMs = now; _toGame("OWNPAUSE|0"); }   // again now and then: a loaded world forgets it
        if (on == _levers) return;
        _lastOwnSentMs = now;
        _levers = on;
        if (!on) { _ownGate?.Apply(false); _lastGateMs = 0; }
        _toGame(on ? "OWNPAUSE|0" : "OWNPAUSE|1");               // pause.lua: the inventory does not slow time while the levers are on
        if (on && _ownGate?.Available != true)
        {
            if (!_gateMissingLogged) _log("pause off: the engine adapter's gate is not in this game (Linux, or not started from the launcher): this player's own ESC menu still pauses their game");
            _gateMissingLogged = true;
        }
        else _log(on ? "pause off: this player's ESC menu no longer pauses their game while a friend is in the world" : "own ESC menu pauses as usual again");
    }

    /// <summary>Who is holding this world right now (the friends whose menu is open and who still count).</summary>
    public IReadOnlyList<int> Holders()
    {
        lock (_gate) return HoldersLocked(_now());
    }

    private List<int> HoldersLocked(long now) =>
        _peerMenus.Where(kv => now - kv.Value.Seen <= LinkTimeoutMs && now - kv.Value.Since <= MaxHoldMs).Select(kv => kv.Key).OrderBy(i => i).ToList();

    private void ReleaseLocked(string why)
    {
        if (!_frozen) return;
        _frozen = false;
        _toGame("FREEZE|0");
        _log("shared pause: released (" + why + ")");
    }

    /// <summary>About ten times a second.</summary>
    public void Tick(bool inWorld, bool connected, int fps, int friendsInWorld = 0)
    {
        lock (_gate)
        {
            long now = _now();
            SetLeversLocked(!Shared && connected && inWorld && friendsInWorld > 0, now);
            if (Shared && connected && _menuOpen && now - _lastBeatMs >= HeartbeatMs) { _lastBeatMs = now; _toPeers("pause|1"); }   // the friends' proof that this link is alive
            var holders = HoldersLocked(now);
            bool want = Shared && connected && inWorld && holders.Count > 0;
            if (want)
            {
                if (!_frozen)
                {
                    _frozen = true;
                    string who = string.Join(", ", holders.Select(_nameOf));
                    _notify($"{who} paused: your game is held until they are back.");
                    _log("shared pause: held for " + who);
                    _lastAskMs = 0;
                }
                if (now - _lastAskMs >= HeartbeatMs) { _lastAskMs = now; _toGame($"FREEZE|1|{LeaseFrames(fps)}"); }
            }
            else if (_frozen)
            {
                ReleaseLocked(!Shared ? "option off" : !connected ? "the relay went away" : !inWorld ? "this player left the world" : holders.Count == 0 ? "the friend's menu closed or their link went quiet" : "no longer wanted");
                if (Shared && connected && inWorld) _notify("The game runs again.");
            }
        }
    }
}
