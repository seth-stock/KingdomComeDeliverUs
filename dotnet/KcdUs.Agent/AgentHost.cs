// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Coop.Contract;
using KcdUs.Agent.Worlds;
using KcdUs.Relay;
using KcdUs.Wire;

namespace KcdUs.Agent;

/// <summary>A relay link that goes nowhere: the session an idle agent keeps, so the game's menu answers before the player has chosen host or join.</summary>
public sealed class NullRelay : IRelayLink
{
    public event Action<Frame>? Frame { add { } remove { } }
    public event Action<bool>? ConnectionChanged { add { } remove { } }
    public bool Connected => false;
    public void Send(MessageType type, string text) { }
    public void Start(CancellationToken ct) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// What the game's Multiplayer tab drives (docs/MENU.md). The agent can start doing nothing (idle); a button press that reaches the mod
/// (KCDUS|MENU|verb|arg) becomes a command here: host a game, join one, leave, set a preference or the keys, open the settings page.
/// Each host / join / leave swaps the session and the relay link; the link to the game stays.
/// </summary>
public sealed class AgentHost : IAsyncDisposable
{
    public const string Idle = "idle", HostMode = "host", GuestMode = "guest";

    private readonly IGameLink _game;
    private readonly Action<string> _log;
    private readonly string _release;
    private readonly int _statusPort;
    private readonly Action? _quit;
    private readonly SemaphoreSlim _switch = new(1, 1);
    private readonly object _gate = new();
    private CancellationToken _ct;
    private Session _session;
    private IRelayLink _relay = new NullRelay();
    private RelayServer? _server;
    private string _refusalSaid = "";

    public AgentConfig Config { get; }
    public string Mode { get; private set; } = Idle;
    /// <summary>The keyboard hook the tab's Keys page re-points; null when hotkeys are off or not available.</summary>
    public Hotkeys? Keys { get; set; }
    /// <summary>Answers the page's CSRF check: only a page the agent itself opened can change settings.</summary>
    public string SettingsToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    /// <summary>Called with a URL to show the player (the default opens the browser).</summary>
    public Action<string> OpenUrl { get; set; } = OpenBrowser;

    /// <summary>Where the settings are saved (null: next to the exe, or the player's own folder).</summary>
    public string? SavePath { get; set; }
    /// <summary>Joining this very computer (127.0.0.1) means no address was entered; only tests do it on purpose.</summary>
    public bool AllowLoopbackJoin { get; set; }

    public Session Session { get { lock (_gate) return _session; } }

    /// <summary>Where this player's room identity (a key pair) is kept; null: next to the settings, in the player's own folder.</summary>
    public string? IdentityFile { get; set; }
    /// <summary>Where a hosted room remembers participant keys; null: in the player's own folder.</summary>
    public string? BindingsFile { get; set; }
    /// <summary>The game said its startup engine adapter is in its process (the Lua mod's ADAPTER line).</summary>
    public bool AdapterLoaded { get; private set; }
    private ParticipantBindings.Identity? _identity;
    private HandshakeFactory.Fingerprint? _fingerprint;

    private static string UserFile(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDUS", name);

    private ParticipantBindings.Identity Identity()
    {
        if (_identity is not null) return _identity;
        try { return _identity = ParticipantBindings.Identity.LoadOrCreate(IdentityFile ?? UserFile("participant.key")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log("warning: this player's room identity could not be kept on disk (" + e.Message + "): it lasts for this run only");
            return _identity = ParticipantBindings.Identity.CreateEphemeral();
        }
    }

    /// <summary>The handshake right now: payload hashes measured once, capabilities from what the game reported.</summary>
    public RoomHandshake Handshake()
    {
        _fingerprint ??= HandshakeFactory.Measure(GameDir, Path.Combine(AppContext.BaseDirectory, "KcdUsEngineBridge.dll"));
        if (_fingerprint.Dlc is null && HandshakeFactory.ReadDlc(GameDir) is { } dlc) _fingerprint = _fingerprint with { Dlc = dlc };   // the game logs its DLC when it starts; ask again until it has
        return HandshakeFactory.Build(_release, _fingerprint, AdapterLoaded);
    }

    /// <summary>The game's folder, to find its saves under Proton (Linux).</summary>
    public string? GameDir { get; set; }
    /// <summary>Where the list of shared worlds is kept (tests point it elsewhere).</summary>
    public string? WorldListPath { get; set; }
    /// <summary>Overrides where the game's saves are (tests).</summary>
    public SaveStore? Store { get; set; }
    private WorldCoordinator? _world;
    public WorldCoordinator? World => _world;

    public AgentHost(AgentConfig cfg, IGameLink game, string release, int statusPort, Action<string> log, Action? quit = null)
    {
        Config = cfg; AllowLoopbackJoin = cfg.AllowLoopbackJoin; _game = game; _release = release; _statusPort = statusPort; _log = log; _quit = quit;
        _session = NewSession(GuestMode, _relay);
        _game.Line += OnGameLine;
    }

    public string SettingsUrl => $"http://127.0.0.1:{_statusPort}/settings?t={SettingsToken}";

    private Session NewSession(string role, IRelayLink relay)
    {
        var s = NewSessionCore(role, relay);
        s.ScopeProvider = () => _world?.ActiveWorldId;       // the shared world this player has loaded: friends in the same one share fights and loot
        return s;
    }

    private Session NewSessionCore(string role, IRelayLink relay) => new(new SessionOptions
    {
        Role = role,
        PlayerName = Config.PlayerName,
        Pref = RailsRules.ParsePref(Config.RailsPref) ?? RailsPref.Ask,
        TetherMeters = Config.TetherMeters,
        GameVersion = _release,
        SharedPause = Config.SharedPause,
        SharedOutcomes = Config.SharedOutcomes,
        LootJournalPath = role == GuestMode ? "" : UserFile("loot-journal.jsonl"),
        PauseGate = _pauseGate,
    }, _game, relay, () => Environment.TickCount64, _log);

    // the engine adapter's pause gate (Windows: the launcher starts the game with the adapter); one per agent, the game outlives sessions
    private readonly IPauseGate? _pauseGate = OperatingSystem.IsWindows() ? new BridgePauseGate() : null;

    /// <summary>The first start: what the command line / the settings file ask for. Idle waits for the tab.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _ct = ct;
        ApplyKeys();
        StartWorlds();
        if (Config.Idle) { _log("idle: host or join from the game's Multiplayer tab"); return; }
        await SwitchAsync(Config.Role == "host" ? HostMode : GuestMode).ConfigureAwait(false);
    }

    private void StartWorlds()
    {
        var store = Store;
        if (store is null)
        {
            var root = SaveStore.FindRoot(Config.SavesDir, GameDir);
            if (root is null) { _log("worlds: the game's save folder was not found (set savesDir in the settings); shared worlds are off"); return; }
            store = new SaveStore(root, Config.BackupDir.Length > 0 ? Config.BackupDir : SaveStore.DefaultBackupRoot());
        }
        string? list = WorldListPath ?? (Config.WorldsFile.Length > 0 ? Config.WorldsFile : null);
        try
        {
            _world = new WorldCoordinator(Config, _game, () => Session, store, WorldRegistry.Load(list), list, _log, Title) { SavePath = SavePath };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log("worlds: shared-world operations are disabled until recovery is resolved: " + e.Message);
            _world = null;
        }
        _log("worlds: saves in " + store.Root);
    }

    // ================================================================ the tab's buttons

    private void OnGameLine(GameLine g)
    {
        if (g.Kind == "ADAPTER") { AdapterLoaded = g.Fields.Length > 1 && g.Fields[1] == "1"; return; }
        if (g.Kind != "MENU") return;
        string verb = g.Fields.Length > 1 ? g.Fields[1] : "", arg = g.Fields.Length > 2 ? g.Fields[2] : "";
        _ = Task.Run(async () => { try { await HandleAsync(verb, arg).ConfigureAwait(false); } catch (Exception e) { _log("menu " + verb + ": " + e.Message); } });
    }

    public async Task HandleAsync(string verb, string arg)
    {
        _log($"menu: {verb} {arg}".TrimEnd());
        switch (verb)
        {
            case "page": TitleLine(); break;
            case "status": Session.Notify(Describe()); TitleLine(); break;
            case "host": await SwitchAsync(HostMode).ConfigureAwait(false); break;
            case "join": await SwitchAsync(GuestMode).ConfigureAwait(false); break;
            case "leave": await SwitchAsync(Idle).ConfigureAwait(false); break;
            case "settings":
                OpenUrl(SettingsUrl);
                Session.Notify("Settings: opened in your browser (" + $"http://127.0.0.1:{_statusPort}/settings" + ")");
                Title(Ui.MenuUi.Title.Settings);
                break;
            case "world" or "henry" or "resolve":
                if (_world is null) { Session.Notify("Shared worlds are off: the game's save folder was not found."); Title(Ui.MenuUi.Title.NoWorld); }
                else _world.Menu(verb, arg);
                break;
            case "pref":
                if (RailsRules.ParsePref(arg) is { } p) { Config.RailsPref = arg; Session.SetPref(p); Persist(); Session.Notify("When the host's story goes on rails: " + RailsRules.PrefText(p)); Title(p == RailsPref.Join ? Ui.MenuUi.Title.StoryJoin : p == RailsPref.Free ? Ui.MenuUi.Title.StoryFree : Ui.MenuUi.Title.StoryAsk); }
                break;
            case "shared":
                if (arg is "on" or "off")
                {
                    Config.SharedOutcomes = arg == "on"; Session.Outcomes.SetEnabled(Config.SharedOutcomes); Persist();
                    Session.Notify(Config.SharedOutcomes ? "Shared fights and loot: on (with friends in the same shared world)." : "Shared fights and loot: off. Every copy of the world stays its own.");
                }
                break;
            case "pause":
                if (arg is "shared" or "off")
                {
                    Config.SharedPause = arg == "shared"; Session.Pause.SetShared(Config.SharedPause); Persist();
                    Session.Notify(Config.SharedPause ? "Pausing is shared: any player's pause menu pauses everyone." : "Pausing is off: with a friend here your pause menu does not pause your game, and a friend's never holds it.");
                }
                break;
            case "keys":
                if (KeyPreset.IsKnown(arg)) { Config.KeyPreset = arg.ToLowerInvariant(); ApplyKeys(); Persist(); Session.Notify("Keys: " + KeyPreset.Describe(Config.KeyPreset)); Title(Config.KeyPreset == "f9f10" ? Ui.MenuUi.Title.KeysAlt : Config.KeyPreset == "off" ? Ui.MenuUi.Title.KeysOff : Ui.MenuUi.Title.KeysDefault); }
                break;
        }
    }

    /// <summary>Host / join / leave: a new session and relay link replace the old ones.</summary>
    public async Task SwitchAsync(string mode)
    {
        await _switch.WaitAsync().ConfigureAwait(false);
        try
        {
            if (mode == GuestMode && !AllowLoopbackJoin && Config.RelayHost.Trim() is "" or "127.0.0.1" or "localhost")
            {
                // nothing to join yet: the host's address goes into the settings page
                _log("join: no host address set");
                Session.Notify("Enter your host's address in the Multiplayer settings first.");
                Title(Ui.MenuUi.Title.NeedAddress);
                OpenUrl(SettingsUrl);
                return;
            }
            Session old; IRelayLink oldRelay; RelayServer? oldServer;
            lock (_gate) { old = _session; oldRelay = _relay; oldServer = _server; }
            old.Dispose();
            await oldRelay.DisposeAsync().ConfigureAwait(false);
            if (oldServer != null) await oldServer.DisposeAsync().ConfigureAwait(false);

            bool portBusy = false;
            RelayServer? server = null; IRelayLink relay; string role = mode == HostMode ? HostMode : GuestMode; string relayHost = Config.RelayHost;
            if (mode == Idle) relay = new NullRelay();
            else
            {
                if (mode == HostMode)
                {
                    server = new RelayServer(new RelayOptions { Port = Config.RelayPort, ServerName = Config.ServerName, Password = Config.Password, MaxPlayers = Config.MaxPlayers, Release = _release, AllowUnverifiedPayload = Config.DevAllowUnverifiedPayload, BindingsFile = BindingsFile ?? UserFile("room-bindings.txt") }, l => _log("relay: " + l));
                    try { server.Start(); }
                    catch (Exception e)
                    {
                        _log($"ERROR: the relay cannot listen on port {Config.RelayPort}: {e.Message}");
                        mode = Idle; relay = new NullRelay(); server = null; role = GuestMode;
                        _game.Send("NOTE|Port " + Config.RelayPort + " is in use. Close the other agent, or change the port in the settings.");
                        portBusy = true;
                    }
                    relayHost = "127.0.0.1";
                }
                relay = mode == Idle ? new NullRelay()
                    : new RelayClient(new RelayEndpoint { Host = relayHost, Port = Config.RelayPort, Name = Config.PlayerName, Role = role, Password = Config.Password, Release = _release, HandshakeProvider = Handshake, Identity = Identity() });
            }
            var session = NewSession(role, relay);
            session.WorldEvent += (from, f) => _world?.OnPeerEvent(from, f);
            lock (_gate) { _session = session; _relay = relay; _server = server; Mode = mode; _refusalSaid = ""; }
            relay.Start(_ct);
            string where = mode == HostMode ? $"hosting \"{Config.ServerName}\" on port {Config.RelayPort}" : mode == GuestMode ? $"joining {Config.RelayHost}:{Config.RelayPort}" : "not in a session";
            _log("mode: " + where);
            session.Notify("Multiplayer: " + where + ".");
            if (portBusy) Title(Ui.MenuUi.Title.PortBusy); else TitleLine();
        }
        finally { _switch.Release(); }
    }

    /// <summary>What the line at the top of the game's page says now.</summary>
    public Ui.MenuUi.Title CurrentTitle()
    {
        var s = Session.GetStatus();
        return Mode switch
        {
            Idle => Ui.MenuUi.Title.Idle,
            HostMode => Ui.MenuUi.Title.Hosting,
            _ when s.Refused.Length > 0 => Ui.MenuUi.Title.Refused,
            _ when s.RelayConnected => Ui.MenuUi.Title.Joined,
            _ => Ui.MenuUi.Title.Joining,
        };
    }

    private void Title(Ui.MenuUi.Title t) => _game.Send("MENUTEXT|" + (int)t);
    private void TitleLine() => Title(CurrentTitle());

    /// <summary>One line for the player: what the agent is doing and who is here.</summary>
    public string Describe()
    {
        var s = Session.GetStatus();
        return Mode switch
        {
            Idle => "Multiplayer: not in a session. Use Host a game or Join a game.",
            HostMode => $"Multiplayer: hosting on port {Config.RelayPort}, {s.Players.Count + 1} player(s). {s.RoomNote} Keys: {KeyPreset.Describe(Config.KeyPreset)}.",
            _ when s.Refused.Length > 0 => "The host refused you: " + s.Refused.Split('|').Last(),
            _ when s.RelayConnected => $"Multiplayer: joined {s.ServerName}, {s.Players.Count + 1} player(s). {s.RoomNote} Keys: {KeyPreset.Describe(Config.KeyPreset)}.",
            _ => $"Multiplayer: trying to reach {Config.RelayHost}:{Config.RelayPort}...",
        };
    }

    public void ApplyKeys()
    {
        if (Keys is null) return;
        var (j, st) = KeyPreset.Parse(Config.KeyPreset);
        Keys.JoinVk = j; Keys.StayVk = st;
    }

    private void Persist()
    {
        var where = Config.Save(SavePath);
        if (where is null) _log("warning: the settings could not be saved");
    }

    // ================================================================ the settings page

    /// <summary>The page's form: unknown or empty fields leave the setting as it was. Returns what it changed (for the page's banner).</summary>
    public string ApplySettings(NameValueCollection f)
    {
        var changed = new List<string>();
        string? v;
        if ((v = f["name"]) is { Length: > 0 } && Safe.Name(v) != Config.PlayerName) { Config.PlayerName = Safe.Name(v); changed.Add("name"); }
        if ((v = f["relay"]) is { Length: > 0 })
        {
            var m = System.Text.RegularExpressions.Regex.Match(v.Trim(), @"^(?<h>[^:\s]+)(:(?<p>\d{1,5}))?$");
            if (m.Success)
            {
                Config.RelayHost = m.Groups["h"].Value;
                if (m.Groups["p"].Success && int.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture) is > 0 and < 65536 and var port) Config.RelayPort = port;
                changed.Add("host address");
            }
        }
        if (f["password"] is { } pw && pw != Config.Password) { Config.Password = Safe.Clean(pw, 40); changed.Add("password"); }
        if ((v = f["servername"]) is { Length: > 0 } && Safe.Clean(v, 40) != Config.ServerName) { Config.ServerName = Safe.Clean(v, 40); changed.Add("server name"); }
        if ((v = f["port"]) is { Length: > 0 } && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int rp) && rp is > 0 and < 65536 && rp != Config.RelayPort && Mode != GuestMode)
        { Config.RelayPort = rp; changed.Add("port"); }
        if ((v = f["pref"]) is { Length: > 0 } && RailsRules.ParsePref(v) is { } p) { Config.RailsPref = v; Session.SetPref(p); changed.Add("story answer"); }
        if ((v = f["shared"]) is "on" or "off" && (v == "on") != Config.SharedOutcomes) { Config.SharedOutcomes = v == "on"; Session.Outcomes.SetEnabled(Config.SharedOutcomes); changed.Add("shared fights and loot"); }
        if ((v = f["pause"]) is "shared" or "off" && (v == "shared") != Config.SharedPause) { Config.SharedPause = v == "shared"; Session.Pause.SetShared(Config.SharedPause); changed.Add("pausing"); }
        if ((v = f["keys"]) is { Length: > 0 } && KeyPreset.IsKnown(v)) { Config.KeyPreset = v.ToLowerInvariant(); ApplyKeys(); changed.Add("keys"); }
        if ((v = f["worldname"]) is { Length: > 0 } && Safe.Clean(v, 40) != Config.WorldName) { Config.WorldName = Safe.Clean(v, 40); changed.Add("world name"); }
        if ((v = f["autosync"]) is "1" or "0" && (v == "1") != Config.AutoSync) { Config.AutoSync = v == "1"; changed.Add("automatic world sync"); }
        if ((v = f["henry"]) is "host" or "mine" && !string.Equals(v, Config.HenryMode, StringComparison.OrdinalIgnoreCase)) { Config.HenryMode = v; changed.Add("which Henry"); }
        if ((v = f["resolve"]) is { Length: > 0 } && WorldResolve.ParsePolicy(v) is { } rp0 && WorldResolve.PolicyName(rp0) != Config.ResolvePolicy) { Config.ResolvePolicy = WorldResolve.PolicyName(rp0); changed.Add("reconnect rule"); }
        if ((v = f["savesdir"]) is not null && v.Trim() != Config.SavesDir && (v.Trim().Length == 0 || Directory.Exists(v.Trim()))) { Config.SavesDir = v.Trim(); changed.Add("saves folder (restart the agent)"); }
        if (changed.Count > 0) Persist();
        return changed.Count == 0 ? "" : string.Join(", ", changed);
    }

    // ================================================================ the loop

    /// <summary>About ten times a second.</summary>
    public void Tick()
    {
        Session s; IRelayLink r;
        lock (_gate) { s = _session; r = _relay; }
        s.Tick();
        _world?.Tick();
        if (r is RelayClient { Refused: { Length: > 0 } why } && why != _refusalSaid)
        {
            _refusalSaid = why;
            s.Notify("The host refused you: " + why.Split('|').Last());
        }
    }

    /// <summary>The session's status, with the idle message when nothing is hosted or joined.</summary>
    public Session.Status GetStatus()
    {
        var s = Session.GetStatus();
        return Mode == Idle && s.GameKnown && s.InWorld ? s with { Message = "Not in a session. Use the Multiplayer tab to host or join." } : s;
    }

    public async ValueTask DisposeAsync()
    {
        _game.Line -= OnGameLine;
        _world?.Dispose();
        if (_world is { } world) await world.Completion.ConfigureAwait(false);
        Session s; IRelayLink r; RelayServer? srv;
        lock (_gate) { s = _session; r = _relay; srv = _server; }
        s.Dispose();
        await r.DisposeAsync().ConfigureAwait(false);
        if (srv != null) await srv.DisposeAsync().ConfigureAwait(false);
    }

    public static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
        }
        catch { /* the player has the address from the in-game note */ }
    }
}
