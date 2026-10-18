// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Channels;
using Coop.Contract;
using KcdUs.Wire;

namespace KcdUs.Agent.Worlds;

//// <summary>
/// Shared worlds (docs/SHARED-WORLDS.md). The HOST's game is the shared world: whatever game the host is playing (a long-running one, or a New Game
/// started a minute ago) is what a friend gets when they press Join. Each player still holds their OWN copy (a playline folder) and the mod never
/// merges two saves: a joining player receives the host's save, freshly written, into a playline of their own (what was there is backed up, never
/// deleted) and loads it. The first time, the joining player plays a copy of the host's Henry; when they come back to the same world they keep
/// their own Henry from their last save of it, or bring the Henry of the game they have loaded ("Join with my own Henry"). Personal native
/// core/inventory snapshots are staged before loading; world-owned state stays the host's. One worker serializes disk operations; native readback
/// acknowledges acceptance.
/// </summary>
public sealed class WorldCoordinator : IDisposable
{
    public const int ReceiveStallMs = 20_000;
    public const int SaveWaitMs = 25_000;
    public const int LoadingGraceMs = 120_000;
    /// <summary>How long a joining player waits for the host's answer before asking again.</summary>
    public const int OfferWaitMs = 30_000;
    /// <summary>The pause between two requests while the host cannot send yet (in the main menu, or the game will not save this moment).</summary>
    public const int AskRetryMs = 15_000;
    /// <summary>A transfer that has not moved for this long is dropped and asked for again.</summary>
    public const int ReceiveGiveUpMs = 90_000;
    /// <summary>How long a second press of Join counts as "yes, use that playline of mine".</summary>
    public const int ConsentMs = 120_000;

    private readonly AgentConfig _cfg;
    private readonly IGameLink _game;
    private readonly Func<Session> _session;
    private readonly SaveStore _store;
    private readonly CharacterCheckpoint _characters;
    private readonly WorldRegistry _reg;
    private readonly string? _regPath;
    private readonly Action<string> _log;
    private readonly Action<string> _status;
    private readonly Func<DateTime> _utc;
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    private WorldTransfer.Receiver? _rx;
    private int _rxFrom;
    private long _rxLastMs;
    private int _rxShownPct = -1;
    private bool _expectLoad;
    private bool _loadStarted;
    private int _requestedFrom;
    private bool _requestedMine;
    private long _requestStartedMs;
    private readonly Dictionary<string, (int Total, string?[] Parts)> _cardIn = new();
    private TaskCompletionSource<string>? _cardWait;
    private string _cardWaitId = "";
    private TaskCompletionSource<bool>? _saveWait;
    private int _announcedFor = -1;         // my relay id when the stamp was last announced
    private string _announcedRoster = "";
    private long _lastCardMs, _lastBindCheckMs, _lastTickMs, _lastFollowMs;
    private bool _inWorldFlag;
    private long _readyAtMs;
    /// <summary>Right after a load the engine's save call returns without writing (seen: a 25 s wait for nothing); the host waits this long first.</summary>
    public const int SettleAfterLoadMs = 6000;
    /// <summary>The player is in the open world: from the game's READY lines, or from the session (which also sees the position samples, so it is right after an agent restart).</summary>
    private bool InWorld => _inWorldFlag || _session().InWorld;

    // the joining player's side: Join was pressed and the host's game has not loaded yet
    private volatile bool _wantJoin;
    private bool _wantMine;
    private long _nextAskMs;
    private int _asks;
    private (int Playline, long AtMs)? _consentOffer;   // all five playlines are full: this one may be used if Join is pressed again
    private int _consentGiven = -1;
    // the host's side: saves written since the world was last bound show which game the host is playing now
    private DateTime _followSinceUtc = DateTime.MinValue;

    /// <summary>The id of the shared world this player has loaded (the registry's active world, once it is bound to a playline); "" when none.</summary>
    public string ActiveWorldId => _reg.ActiveWorld is { Playline: >= 0 } w ? w.Id : "";
    /// <summary>The line the Multiplayer page shows about the world ("" when there is nothing to say).</summary>
    public string Status { get; private set; } = "";
    /// <summary>Join was pressed and the host's game is not loaded yet.</summary>
    public bool Joining => _wantJoin;
    private int _sending;

    public WorldRegistry Registry => _reg;

    public WorldCoordinator(AgentConfig cfg, IGameLink game, Func<Session> session, SaveStore store, WorldRegistry reg, string? regPath, Action<string> log,
        Action<string> status, Func<DateTime>? utcNow = null)
    {
        _cfg = cfg; _game = game; _session = session; _store = store; _reg = reg; _regPath = regPath; _log = log; _status = status; _utc = utcNow ?? (() => DateTime.UtcNow);
        _characters = new CharacterCheckpoint(store.BackupRoot);
        _store.RecoverPendingInstalls();
        if (_reg.PendingLoad is { Phase: "Prepared" } pending)
        {
            string installed = Path.Combine(_store.PlaylineDir(pending.Playline), "world.whs");
            bool committed = File.Exists(installed) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installed))).ToLowerInvariant() == pending.SaveSha256;
            _reg.PendingLoad = committed ? pending with { Phase = "Installed" } : null;
            _reg.Save(_regPath);
        }
        _game.Line += OnGameLine;
        _worker = Task.Run(Work);
    }

    public void Dispose()
    {
        _game.Line -= OnGameLine;
        _cts.Cancel();
        _work.Writer.TryComplete();
    }
    public Task Completion => _worker;

    private async Task Work()
    {
        await foreach (var job in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_cts.IsCancellationRequested) break;
            try { await job().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception e) { _log("world: " + e.Message); Alert("Stopped: read the message", "World operation stopped: " + e.Message); }
        }
    }

    private void Enqueue(Func<Task> job) => _work.Writer.TryWrite(job);
    private void Persist() { try { _reg.Save(_regPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log("warning: the world list could not be saved"); } }
    /// <summary>A message on screen in the world (and in the log: a player can only report what the log shows).</summary>
    private void Say(string t) { _session().Notify(t); _log("world: " + t); }
    /// <summary>The page's status line: SHORT, the title box shows about 40 characters (seen in the game).</summary>
    private void SetStatus(string t) { Status = t; _status(t); _log("world: " + t); }
    /// <summary>The status line, and the longer words on screen in the world.</summary>
    private void Show(string status, string? detail = null) { SetStatus(status); _session().Notify(detail ?? status); }
    /// <summary>Something the player must read: a box with OK in the Multiplayer page (the centred line in the world), and the status line.</summary>
    private void Alert(string status, string detail) { SetStatus(status); _session().Alert(detail); _log("world: " + detail); }
    private bool MineMode => string.Equals(_cfg.HenryMode, "mine", StringComparison.OrdinalIgnoreCase);
    private string HostName => PeerName(_session().HostId);

    // ================================================================ the menu's buttons

    public void Menu(string verb, string arg)
    {
        switch (verb, arg)
        {
            case ("world", "save"): Enqueue(SaveAsync); break;
            case ("world", "play"): Enqueue(PlayAsync); break;
            case ("world", "join"): Join(MineMode); break;
            case ("world", "joinmine"): Join(true); break;
            case ("world", "new"):
                Alert("New game together", _session().IsHost
                    ? "New game together: start a New Game from the main menu. Your friend presses Join my friend's game and gets it as soon as it has begun."
                    : "New game together: your host starts a New Game, and you press Join my friend's game.");
                break;
            case ("henry", "home"): Enqueue(HenryHomeAsync); break;
            case ("henry", "host" or "mine"):
                _cfg.HenryMode = arg; PersistConfig();
                SetStatus(arg == "mine" ? "Join brings your own Henry" : "Join: host's Henry, then your own");
                break;
            case ("resolve", _) when WorldResolve.ParsePolicy(arg) is { } p:
                _cfg.ResolvePolicy = WorldResolve.PolicyName(p); PersistConfig();
                SetStatus("The host's game is the one played");
                break;
        }
    }

    private void PersistConfig() { try { _cfg.Save(SavePath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    public string? SavePath { get; set; }

    /// <summary>
    /// Join: get the host's game and load it. Asks as soon as the session is up, and again until it is loaded (the host may still be in the main
    /// menu, or the game may refuse to save for a moment). <paramref name="mine"/> brings the Henry of the game loaded here.
    /// </summary>
    public void Join(bool mine) => Enqueue(() =>
    {
        if (_consentOffer is { } c && Environment.TickCount64 - c.AtMs < ConsentMs) { _consentGiven = c.Playline; _consentOffer = null; }
        _wantJoin = true; _wantMine = mine; _nextAskMs = 0; _asks = 0;
        if (_rx is null && !_expectLoad && _reg.PendingLoad is null) _requestedFrom = 0;   // a request left over from before never blocks a new one
        var s = _session();
        if (_rx is not null) SetStatus($"Receiving {PeerName(_rxFrom)}'s game...");
        else if (s.MyId == 0) SetStatus("Connecting to the host...");
        else SetStatus($"Asking {HostName} for the game...");
        return Task.CompletedTask;
    });

    /// <summary>The session was replaced (host, join, leave): whatever was asked of the old one is over.</summary>
    public void SessionChanged() => Enqueue(() =>
    {
        _wantJoin = false; _requestedFrom = 0; _rx = null; _announcedFor = -1; _announcedRoster = "";
        Status = "";
        return Task.CompletedTask;
    });

    // ================================================================ the game's side

    private void OnGameLine(GameLine g)
    {
        switch (g.Kind)
        {
            case "READY":
                bool w = g.Fields.Length > 1 && g.Fields[1] == "1";
                Enqueue(() => OnReadyAsync(w));
                break;
            case "LOADING" when g.Fields.Length > 3 && g.Fields[3] == "1":
                if (_expectLoad) _loadStarted = true;
                break;
            case "UILOAD" when g.Fields.Length > 1 && g.Fields[1] == "loaded":
                Enqueue(LoadedAsync);
                break;
            case "SAVEWORLD":
                if (g.Fields.Length > 2 && g.Fields[1] == _saveWaitId)
                    _saveWait?.TrySetResult(g.Fields[2] == "1");
                break;
            case "CARD" when g.Fields.Length >= 5:
                OnCardPiece(g.Fields[1], g.Fields[2], g.Fields[3], g.Fields[4]);
                break;
        }
    }

    private void OnCardPiece(string id, string idx, string total, string chunk)
    {
        if (!int.TryParse(idx, out int i) || !int.TryParse(total, out int n) || n <= 0 || n > 400 || i < 0 || i >= n) return;
        lock (_cardIn)
        {
            if (!_cardIn.TryGetValue(id, out var e) || e.Total != n) _cardIn[id] = e = (n, new string?[n]);
            e.Parts[i] = chunk;
            if (e.Parts.All(p => p is not null))
            {
                _cardIn.Remove(id);
                if (_cardWaitId == id) _cardWait?.TrySetResult(string.Concat(e.Parts));
            }
        }
    }

    private async Task OnReadyAsync(bool inWorld)
    {
        bool was = _inWorldFlag;
        _inWorldFlag = inWorld;
        if (inWorld && !was) _readyAtMs = Environment.TickCount64;
        var s = _session();
        // the host tells waiting friends when a game is running (a New Game too): they ask for it then, not 15 s later
        if (s.IsHost && s.MyId != 0 && was != inWorld) s.SendEvent("whost|" + (inWorld ? "game" : "menu"));
        if (!inWorld) return;
        if (_expectLoad || _reg.PendingLoad is not null) return;
        _lastBindCheckMs = 0;
        await AnnounceAsync().ConfigureAwait(false);
    }

    /// <summary>Asks the game for the Henry in it now (null if it does not answer).</summary>
    public async Task<string?> CaptureCardAsync(int timeoutMs = 8000)
    {
        if (!InWorld) return null;
        string id = Guid.NewGuid().ToString("N")[..6];
        _cardWait = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cardWaitId = id;
        _game.Send("CARDGET|" + id);
        var done = await Task.WhenAny(_cardWait.Task, Task.Delay(timeoutMs, _cts.Token)).ConfigureAwait(false);
        if (done != _cardWait.Task) { _log("world: the game did not answer for the Henry card"); return null; }
        string card = _cardWait.Task.Result;
        _reg.MyCard = card; _lastCardMs = Environment.TickCount64; Persist();
        return card;
    }

    private async Task LoadedAsync()
    {
        if (!_expectLoad || !_loadStarted) return;
        _inWorldFlag = true;
        if (_reg.PendingLoad is not { } pending) { _expectLoad = false; SetStatus("Loaded."); await AnnounceAsync(); return; }
        try
        {
            // The UI event precedes the end of native loading. SaveViaResting
            // silently does nothing in that short interval despite returning.
            await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
            // A generic READY/UI notification is not acceptance. A native save must
            // come from the installed playline and retain the staged personal state.
            var saved = await SaveNowAsync(pending.Playline).ConfigureAwait(false);
            if (saved is null)
            {
                await Task.Delay(4000, _cts.Token).ConfigureAwait(false);   // still settling after the load (seen): once more
                saved = await SaveNowAsync(pending.Playline).ConfigureAwait(false)
                    ?? throw new IOException("The engine did not save the loaded world; acceptance remains pending.");
            }
            if (pending.CharacterSha256.Length > 0 && !ExactTraitsSave.SameProgressionAndInventory(
                _characters.Read(pending.CharacterSha256), ExactTraitsSave.CaptureCharacter(File.ReadAllBytes(saved.Path))))
                throw new InvalidDataException("Native character readback differs; acceptance remains pending.");
            if (!pending.Home)
            {
                var w = _reg.Upsert(pending.WorldId, pending.Name);
                if (w.Playline >= 0 && w.Playline != pending.Playline && !w.Slot) w.HomePlayline = w.Playline;
                // the slot belongs to this world now: no other record may claim it
                foreach (var other in _reg.Worlds.Where(o => o != w && o.Playline == pending.Playline)) other.Playline = -1;
                w.Playline = pending.Playline; w.Slot = true; Remember(w, saved); _reg.Active = w.Id;
                // The readback save verifies acceptance; it is not a new shared
                // checkpoint. Preserve the sender's stamp to prevent resync loops.
                w.Hours = pending.Hours; w.SavedUnix = pending.SavedUnix;
                w.VerificationSaveSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(saved.Path))).ToLowerInvariant();
            }
            else _reg.Active = _reg.Worlds.FirstOrDefault(w => w.Playline == pending.Playline)?.Id ?? "";
            _reg.PendingLoad = null;
            _reg.Save(_regPath);
            _expectLoad = false; _loadStarted = false; _wantJoin = false; _consentGiven = -1;
            if (pending.From > 0) _session().SendEvent($"wdone|{pending.From}|{pending.TransferId}|1");
            Show(pending.Home ? "Your Henry is home" : $"In {PeerName(pending.From)}'s game", pending.Home ? "Your Henry is home." : $"You are in {PeerName(pending.From)}'s game. Have fun!");
            await AnnounceAsync();
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        { _reg.PendingLoad = pending; _log("world: " + e.Message); Alert("Load not confirmed", e.Message + " Your character snapshot and previous saves are preserved."); }
    }

    // ================================================================ saving

    /// <summary>Asks the game to save now and waits for the file. Null when nothing was written (the prologue and some scenes refuse to save).</summary>
    private string _saveWaitId = "";
    private bool _lastSaveAccepted;
    public async Task<SaveFile?> SaveNowAsync(int? expectedPlayline = null, bool character = false)
    {
        if (!InWorld) return null;
        _lastSaveAccepted = false;
        var before = _store.FileVersions();
        _saveWait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _saveWaitId = Guid.NewGuid().ToString("N");
        _game.Send("SAVEWORLD|" + _saveWaitId + (character ? "|character" : ""));
        var end = _utc().AddMilliseconds(SaveWaitMs);
        while (_utc() < end && !_cts.IsCancellationRequested)
        {
            await Task.Delay(500, _cts.Token).ConfigureAwait(false);
            if (_saveWait.Task.IsCompletedSuccessfully && !_saveWait.Task.Result) return null;
            if (!_saveWait.Task.IsCompletedSuccessfully) continue;
            _lastSaveAccepted = true;
            var changed = _store.ChangedFiles(before);
            if (changed.Count > 1) throw new IOException("Multiple saves changed during capture; refusing to guess Henry's playline.");
            if (changed.Count == 1)
            {
                var found = changed[0];
                if (expectedPlayline is { } slot && found.Playline != slot)
                    throw new IOException("The engine saved a different playline; refusing character/world acknowledgement.");
                if (await StableAsync(found.Path).ConfigureAwait(false)) return found;
            }
        }
        return null;
    }

    private async Task<int> CaptureCharacterAsync()
    {
        var saved = await SaveNowAsync(character: true).ConfigureAwait(false);
        if (saved is null && _lastSaveAccepted && InWorld)
        {
            // Immediately after a load, the native resting-save call can return
            // successfully without writing. Never use an old save: retry once.
            Say("The game has not written Henry's save yet. Retrying after loading settles.");
            await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
            saved = await SaveNowAsync(character: true).ConfigureAwait(false);
        }
        if (saved is null) throw new IOException("Henry could not be saved. Put weapons away and finish any fight, ride, dialogue or cutscene, then press Join again.");
        _reg.MyCharacterSha256 = _characters.Capture(await File.ReadAllBytesAsync(saved.Path).ConfigureAwait(false));
        _reg.Save(_regPath); // A failed durable personal snapshot cancels the replacement.
        return saved.Playline;
    }

    private async Task<bool> StableAsync(string path)
    {
        var a = File.ReadAllBytes(path);
        await Task.Delay(700, _cts.Token).ConfigureAwait(false);
        var b = File.ReadAllBytes(path);
        return a.AsSpan().SequenceEqual(b) && SaveInfo.Validate(b, out _);
    }

    /// <summary>
    /// "Save now": the game saves. The host's save is also what the shared world is from then on (the game the host plays IS the shared world),
    /// and friends hear how far it has got. A friend's save keeps their own copy, and their Henry for the next time they join.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad) { Show("Wait for the load to finish"); return; }
        if (!InWorld) { Show("Load a game first, then save"); return; }
        var f = await SaveNowAsync().ConfigureAwait(false);
        if (f is null) { Show("The game cannot save just now", "The game would not save just now (a cutscene, a fight, a dialogue or riding). Try again in a moment."); return; }
        var s = _session();
        if (s.IsHost || _reg.ActiveWorld is null)
        {
            var w = BindHostGame(f);
            await CaptureCardAsync().ConfigureAwait(false);
            Persist();
            Show("Saved", s.IsHost && s.Peers.Any(p => p.Id != s.MyId) ? "Saved. Friends who press Join get your game as it is now." : "Saved.");
        }
        else
        {
            if (_reg.ActiveWorld is { } w && w.Playline == f.Playline) Remember(w, f);
            await CaptureCardAsync().ConfigureAwait(false);
            Persist();
            Show("Saved", "Saved. Your Henry is kept for the next time you join this world.");
        }
        await AnnounceAsync().ConfigureAwait(false);
    }

    /// <summary>The host's game (the playline the newest save went to) is the shared world: its record, made the active one (a new record for a new game).</summary>
    private WorldRecord BindHostGame(SaveFile f)
    {
        var w = _reg.Worlds.Where(x => x.Playline == f.Playline).OrderByDescending(x => x.Id == _reg.Active).FirstOrDefault();
        if (w is null)
        {
            w = _reg.Upsert(WorldRegistry.NewId(), _cfg.WorldName);
            w.Playline = f.Playline;
            _log($"world: playline {f.Playline} is the shared world \"{w.Name}\"");
        }
        Remember(w, f);
        _reg.Active = w.Id;
        _followSinceUtc = f.ModifiedUtc;
        return w;
    }

    private void Remember(WorldRecord w, SaveFile f) { w.Hours = f.Info.Hours; w.SavedUnix = f.Info.SavedUnix; }

    // ================================================================ playing it

    private async Task PlayAsync()
    {
        if (_reg.PendingLoad is { } pending)
        {
            if (pending.Phase != "Installed" || !File.Exists(Path.Combine(_store.PlaylineDir(pending.Playline), "world.whs"))
                || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_store.PlaylineDir(pending.Playline), "world.whs")))).ToLowerInvariant() != pending.SaveSha256)
                throw new IOException("Pending world installation needs recovery; previous saves and snapshot remain preserved.");
            LoadPlayline(pending.Playline); return;
        }
        var w = _reg.ActiveWorld;
        if (w is null || w.Playline < 0 || _store.Newest(w.Playline) is null) { Alert("No shared game here yet", "You have no shared game here yet. Press Join my friend's game to get your host's game."); return; }
        if (InWorld) await CaptureCardAsync().ConfigureAwait(false);
        LoadPlayline(w.Playline);
    }

    private void LoadPlayline(int folder)
    {
        _expectLoad = true;
        _loadStarted = false;
        SetStatus("Loading the shared game...");
        _game.Send($"LOAD|0|{folder}");   // the load graph takes the playline's folder number (seen in the retail game)
        _log($"world: loading the newest save of playline {folder}");
    }

    // ================================================================ talking about it

    private async Task AnnounceAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad) return;
        var s = _session();
        if (s.MyId == 0) return;
        _announcedFor = s.MyId;
        _announcedRoster = string.Join(',', s.Peers.Select(p => p.Id).OrderBy(id => id));
        if (s.IsHost) s.SendEvent("whost|" + (InWorld ? "game" : "menu"));
        var w = _reg.ActiveWorld;
        if (w is null || w.Playline < 0) return;
        var f = _store.Newest(w.Playline);
        if (f is not null && (w.VerificationSaveSha256.Length == 0 ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Path))).ToLowerInvariant() != w.VerificationSaveSha256)) Remember(w, f);
        s.SendEvent("wstamp|" + w.Stamp.Encode());
        await Task.CompletedTask;
    }

    /// <summary>A shared-world message from the relay (queued; runs on the worker).</summary>
    public void OnPeerEvent(int from, string[] f)
    {
        if (f.Length > 0 && f[0] is "wreq" or "woffer" or "wdone" or "wmiss" or "wnew" or "whost") _log($"world: {f[0]} from #{from} ({string.Join('|', f.Skip(1).Take(3))})");
        Enqueue(() => HandleEventAsync(from, f));
    }

    private async Task HandleEventAsync(int from, string[] f)
    {
        var s = _session();
        int me = s.MyId;
        // messages addressed to somebody else are not for this machine
        bool Mine(int at) => f.Length > at && int.TryParse(f[at], out int to) && to == me;
        switch (f[0])
        {
            case "wstamp":
                if (WorldStamp.Decode(f, 1) is { } st) OnStamp(from, st);
                break;
            case "whost" when f.Length >= 2 && !s.IsHost && from == s.HostId:
                if (_wantJoin && _requestedFrom == 0 && _rx is null && f[1] == "game") _nextAskMs = 0;   // the host's game is running now: ask at once
                else if (_wantJoin && f[1] == "menu" && _rx is null) SetStatus(WaitingFor(HostName));
                break;
            case "wreq" when Mine(1): await ServeAsync(from).ConfigureAwait(false); break;
            case "wreq": _log($"world: a request from #{from} for #{(f.Length > 1 ? f[1] : "?")} is not for me (#{me})"); break;
            case "woffer" when Mine(1) && f.Length >= 8: OnOffer(from, f); break;
            case "wchunk" when Mine(1) && f.Length >= 5 && _rx is not null && from == _rxFrom && _rx.Offer.TransferId == f[2] && int.TryParse(f[3], out int idx):
                if (_rx.Add(idx, f[4])) _rxLastMs = Environment.TickCount64;
                ShowProgress();
                if (_rx.Complete) await FinishReceiveAsync().ConfigureAwait(false);
                break;
            case "wmiss" when Mine(1) && f.Length >= 4: await ResendAsync(from, f[2], f[3]).ConfigureAwait(false); break;
            case "wdone" when Mine(1) && f.Length >= 4:
                if (f[3] == "1") { Show($"{PeerName(from)} joined your game", $"{PeerName(from)} is in your game now."); break; }
                OnRefused(from, f.Length > 4 ? f[4] : "?");
                break;
        }
    }

    private string PeerName(int id) => _session().Peers.FirstOrDefault(p => p.Id == id)?.Name ?? (id == _session().HostId ? "the host" : "#" + id);

    /// <summary>The host could not send its game. Some reasons pass by themselves (the host is in the menu, the game is busy): Join keeps asking.</summary>
    private void OnRefused(int from, string why)
    {
        if (_requestedFrom == from) { _requestedFrom = 0; _rx = null; }
        string who = PeerName(from);
        switch (why)
        {
            case "menu":
                _nextAskMs = Environment.TickCount64 + AskRetryMs;   // the host's "whost|game" asks again at once
                if (Status != WaitingFor(who)) SetStatus(WaitingFor(who));
                break;
            case "wait" or "busy":
                _nextAskMs = Environment.TickCount64 + AskRetryMs;
                SetStatus(why == "busy" ? $"{who} is busy: you are next" : $"{who}'s game can't save yet: retrying");
                break;
            case "different mods":
                _wantJoin = false; Alert("Different mods: cannot join", ContentRefusal); break;
            default:
                _wantJoin = false;
                if (why.StartsWith("you lack DLC", StringComparison.Ordinal))
                {
                    var names = why[(why.IndexOf('(') + 1)..].TrimEnd(')').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    Alert("Missing DLC: cannot join", DlcRefusal(names));
                }
                else Alert($"{who} could not send the game", $"{who} could not send their game: {why}.");
                break;
        }
    }

    private void ShowProgress()
    {
        if (_rx is not { } rx) return;
        int pct = rx.Offer.Chunks == 0 ? 100 : rx.Have * 100 / rx.Offer.Chunks;
        if (pct / 10 == _rxShownPct / 10 && _rxShownPct >= 0) return;
        _rxShownPct = pct;
        SetStatus($"Receiving: {pct}% of {rx.Offer.Bytes / 1024 / 1024.0:0.0} MB");
    }

    private void OnStamp(int from, WorldStamp theirs)
    {
        var s = _session();
        if (s.IsHost || from != s.HostId) return;   // the host's game is the one that is played: other copies say nothing about it
        if (_wantJoin)
        {
            if (_requestedFrom == 0 && _rx is null) _nextAskMs = Math.Min(_nextAskMs, Environment.TickCount64 + 1000);
            return;
        }
        var mine = _reg.Find(theirs.WorldId);
        if (mine is { Playline: >= 0 } && _reg.Active == mine.Id && InWorld && WorldResolve.Decide(mine.Stamp, theirs, ResolvePolicy.Host, false) == Winner.Same)
            SetStatus($"In {PeerName(from)}'s game");
    }

    // ================================================================ taking a world

    private static string WaitingFor(string who) => $"Waiting for {who}'s game";

    /// <summary>No playline for the world: the question (Join again says yes) when one of the player's own may be used, otherwise the reason.</summary>
    private void NoRoom(string why)
    {
        // a Yes/No box was tried: the game shows it, but never told the page which answer was chosen (seen), so the answer is a second press of Join
        Alert(_consentOffer is not null ? "Full: press Join again to say yes" : "No room for the game", why);
    }

    /// <summary>The answer Yes to <see cref="NoRoom"/>'s question (menu verb "confirm"): the same as pressing Join again.</summary>
    public void Confirm() => Join(_wantMine);

    private const string ContentRefusal = "Your game has other mods than your friend's, so a world or a Henry cannot be moved between you safely. Install the same mods on both, or play without sharing a world.";

    private static string DlcRefusal(IReadOnlyList<string> lacks) =>
        $"Your host's game uses DLC you do not have ({DlcLog.Friendly(lacks)}), and their saves need it. Get it on Steam, or your host turns it off in Steam (Properties > DLC) and starts a game without it.";

    /// <summary>Asks the host for their game (Join's worker, again and again until it has loaded).</summary>
    private async Task RequestAsync()
    {
        if (!_wantJoin) return;
        if (_reg.PendingLoad is not null || _expectLoad || _rx is not null || _requestedFrom != 0) return;
        var s = _session();
        if (s.MyId == 0) { SetStatus("Connecting to the host..."); return; }
        if (s.IsHost) { _wantJoin = false; Alert("You are the host", "You are the host: friends join your game. Join my friend's game is for the friend who joins."); return; }
        if (!s.ContentMatches) { _wantJoin = false; Alert("Different mods: cannot join", ContentRefusal); return; }
        if (s.DlcLacks.Count > 0) { _wantJoin = false; Alert("Missing DLC: cannot join", DlcRefusal(s.DlcLacks)); return; }
        int target = s.HostId;
        if (target == 0) { SetStatus("Connecting to the host..."); return; }
        if (PickSlot(null, out string? why) is null) { _wantJoin = false; NoRoom(why!); return; }
        _requestedMine = _wantMine;
        if (_requestedMine)
        {
            int? source = InWorld && _asks == 0 ? await CaptureCharacterAsync().ConfigureAwait(false) : null;   // once per Join, not at every retry
            if (_reg.MyCharacterSha256.Length == 0) { _wantJoin = false; Alert("Load your own game first", "Load your own game (with your Henry) first, then press Join with my own Henry."); return; }
            _ = _characters.Read(_reg.MyCharacterSha256);
            if (source is { } home && _reg.HomePlayline < 0 && !_reg.SlotsInUse().Contains(home))
            { _reg.HomePlayline = home; _reg.Save(_regPath); }
        }
        else if (_asks == 0 && InWorld && _reg.HomePlayline < 0 && _reg.HomeLeaseId.Length == 0 && _reg.ActiveWorld is not { Slot: true })
        {
            // Establish home from the engine's fresh save, never another
            // playline's unrelated newest file.
            if (await SaveNowAsync().ConfigureAwait(false) is { } home && !_reg.SlotsInUse().Contains(home.Playline))
            { _reg.HomePlayline = home.Playline; _reg.Save(_regPath); }
        }
        if (!_wantJoin) return;
        _requestedFrom = target;
        _requestStartedMs = Environment.TickCount64;
        _asks++;
        if (!s.SendEvent($"wreq|{target}|-")) { _requestedFrom = 0; SetStatus("Connecting to the host..."); return; }
        _rx = null;
        if (_asks == 1 || !Status.StartsWith("Waiting for ", StringComparison.Ordinal)) SetStatus($"Asking {PeerName(target)} for the game...");   // waiting stays waiting: no flicker at each retry
        _rxLastMs = Environment.TickCount64;
    }

    /// <summary>
    /// Where the host's world goes: its own slot from last time, else an empty playline, else the slot of another shared world (backed up first),
    /// else (only when the player said yes by pressing Join twice) the playline of their own that was played longest ago, backed up first.
    /// </summary>
    private int? PickSlot(string? worldId, out string? why)
    {
        why = null;
        if (worldId is not null && _reg.Find(worldId) is { Slot: true, Playline: >= 0 } known) return known.Playline;
        if (_store.FreePlayline(_reg.SlotsInUse()) is { } free) return free;
        var slots = _reg.Worlds.Where(w => w.Slot && w.Playline >= 0 && w.Playline < SaveStore.MaxPlaylines && w.Id != _reg.Active).ToList();
        if (slots.Count > 0) return slots.OrderBy(w => _store.Newest(w.Playline)?.Info.SavedUnix ?? 0).First().Playline;
        if (_reg.ActiveWorld is { Slot: true, Playline: >= 0 } current) return current.Playline;   // the shared world this player has now
        if (_consentGiven is >= 0 and < SaveStore.MaxPlaylines) return _consentGiven;
        var oldest = Enumerable.Range(0, SaveStore.MaxPlaylines).Where(p => p != _reg.HomePlayline || _reg.HomePlayline < 0)
            .Select(p => (P: p, Last: _store.Newest(p))).Where(x => x.Last is not null).OrderBy(x => x.Last!.Info.SavedUnix).FirstOrDefault();
        if (oldest.Last is null) { why = "There is no playline the world could go into."; return null; }
        _consentOffer = (oldest.P, Environment.TickCount64);
        var when = DateTimeOffset.FromUnixTimeSeconds(oldest.Last.Info.SavedUnix).ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        why = $"All five playlines are full. Put the host's game into Playline {oldest.P + 1} (last played {when})? Its saves are moved to the mod's backup folder, not deleted. To say yes, press Join again.";
        return null;
    }

    /// <summary>The host sends the game it is playing now, saved this moment. In the main menu there is no game to send yet: the friend is told to wait.</summary>
    private async Task ServeAsync(int to)
    {
        var s = _session();
        string who = PeerName(to);
        _log($"world: {who} (#{to}) asks for the game");
        void Refuse(string why, string reason) { _log("world: not sent to " + who + ": " + why); s.SendEvent($"wdone|{to}|-|0|{reason}"); }
        if (!s.ContentMatches) { Refuse("different mods", "different mods"); return; }
        if (s.PeerLacksDlc(to) is { Count: > 0 } lacks) { Refuse("DLC", $"you lack DLC the world needs ({string.Join(", ", lacks)})"); return; }
        if (!s.IsHost) { Refuse("not the host", "ask the host"); return; }
        if (!InWorld)
        {
            Refuse("in the main menu", "menu");
            string wait = $"{who} waits: load or start a game";
            if (Status != wait) Show(wait, $"{who} is waiting to join: load your game or start a New Game.");   // once, not at every one of the friend's retries
            return;
        }
        if (Interlocked.Exchange(ref _sending, 1) == 1) { Refuse("already sending", "busy"); return; }
        try
        {
            SetStatus($"Saving your game for {who}...");
            long settle = SettleAfterLoadMs - (Environment.TickCount64 - _readyAtMs);
            if (_readyAtMs != 0 && settle > 0) await Task.Delay((int)settle, _cts.Token).ConfigureAwait(false);
            var f = await SaveNowAsync().ConfigureAwait(false);
            if (f is null)
            {
                Refuse("the game would not save", "wait");
                if (!Status.StartsWith(who + " waits", StringComparison.Ordinal)) Say($"{who} wants to join: your game could not save just now (cutscene, fight, dialogue). It tries again shortly.");
                SetStatus($"{who} waits: game can't save yet");
                return;
            }
            var w = BindHostGame(f); Persist();
            var bytes = await File.ReadAllBytesAsync(f.Path).ConfigureAwait(false);
            var offer = WorldTransfer.MakeOffer(Guid.NewGuid().ToString("N")[..6], w.Id, bytes);
            s.SendEvent($"woffer|{to}|{offer.TransferId}|{offer.WorldId}|{offer.Bytes}|{offer.Chunks}|{offer.Sha256}|{w.Stamp.Encode().Split('|', 2)[1]}|{Safe.Clean(w.Name, 40)}");
            _sentFile = (offer.TransferId, bytes, to);
            int shown = -1;
            foreach (var (i, b64) in WorldTransfer.Pieces(bytes))
            {
                s.SendEvent($"wchunk|{to}|{offer.TransferId}|{i}|{b64}");
                int pct = (i + 1) * 100 / Math.Max(1, offer.Chunks);
                if (pct / 20 != shown / 20) { shown = pct; SetStatus($"Sending to {who}: {pct}%"); }
                if (i % 8 == 7) await Task.Delay(40, _cts.Token).ConfigureAwait(false);   // the relay's queue is bounded; do not flood it
            }
            SetStatus($"Sent to {who}: they are loading it");
            await AnnounceAsync().ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
    }

    private (string Id, byte[] Bytes, int To)? _sentFile;

    private async Task ResendAsync(int to, string transferId, string list)
    {
        if (_sentFile is not { } sf || sf.Id != transferId || sf.To != to) return;
        var want = list.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var v) ? v : -1).Where(v => v >= 0).Take(400).ToHashSet();
        foreach (var (i, b64) in WorldTransfer.Pieces(sf.Bytes))
        {
            if (!want.Contains(i)) continue;
            _session().SendEvent($"wchunk|{to}|{transferId}|{i}|{b64}");
            if (i % 8 == 7) await Task.Delay(40, _cts.Token).ConfigureAwait(false);
        }
    }

    private void OnOffer(int from, string[] f)
    {
        // woffer|to|xid|worldId|bytes|chunks|sha|hours|unix|name
        try
        {
            if (from != _requestedFrom || _requestedFrom == 0 || _rx is not null) return;
            if (f.Length < 10 || !int.TryParse(f[4], out int bytes) || !int.TryParse(f[5], out int chunks)) return;
            _rx = new WorldTransfer.Receiver(new WorldTransfer.Offer(f[2], Safe.Clean(f[3], 40), bytes, chunks, f[6].ToLowerInvariant()));
            _rxFrom = from; _rxLastMs = Environment.TickCount64; _rxShownPct = -1;
            _offerName = Safe.Clean(f[9], 40);
            _log($"world: receiving \"{_offerName}\" ({bytes / 1024 / 1024.0:0.0} MB)");
            ShowProgress();
        }
        catch (InvalidDataException) { _rx = null; }
    }

    private string _offerName = "";

    private async Task FinishReceiveAsync()
    {
        var rx = _rx; _rx = null;
        if (rx is null) return;
        _requestedFrom = 0;
        if (_reg.PendingLoad is not null || _expectLoad) { Say("A world load is already pending. The additional received world was not installed."); return; }
        var bytes = rx.Assemble();
        var s = _session();
        string why = "";
        if (bytes is null || !SaveInfo.Validate(bytes, out why))
        {
            _log("world: the received save did not check out" + (bytes is null ? " (checksum)" : ": " + why));
            s.SendEvent($"wdone|{_rxFrom}|{rx.Offer.TransferId}|0|damaged in transit");
            SetStatus("Arrived damaged: asking again");
            _nextAskMs = Environment.TickCount64 + 2000;
            return;
        }
        var info = SaveInfo.Read(bytes);
        var w = _reg.Find(rx.Offer.WorldId);
        if (PickSlot(rx.Offer.WorldId, out string? noSlot) is not { } playline)
        {
            s.SendEvent($"wdone|{_rxFrom}|{rx.Offer.TransferId}|0|no free playline");
            _wantJoin = false; NoRoom(noSlot!);
            return;
        }
        bool replace = _store.Newest(playline) is not null || (w is { Slot: true } && w.Playline == playline);
        string characterSha = "";
        if (_requestedMine)
        {
            if (InWorld) await CaptureCharacterAsync().ConfigureAwait(false); // Include play during the transfer.
            bytes = ExactTraitsSave.PrepareCharacter(bytes, _characters.Read(_reg.MyCharacterSha256));
            characterSha = _reg.MyCharacterSha256;
        }
        else if (w is { Slot: true, Playline: >= 0 } && _store.Newest(w.Playline) is { } last)
        {
            // back in a world this player has played before: they keep their own Henry from their last save of it
            var raw = await File.ReadAllBytesAsync(last.Path).ConfigureAwait(false);
            bool played = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() != w.VerificationSaveSha256;
            if (played)
            {
                try
                {
                    var prepared = ExactTraitsSave.PrepareCharacter(bytes, ExactTraitsSave.CaptureCharacter(raw));
                    characterSha = _characters.Capture(raw);
                    bytes = prepared;
                }
                catch (Exception e) when (e is InvalidDataException or IOException)
                {
                    _log("world: own Henry not carried over: " + e.Message);
                    Say("Your Henry from last time could not be carried into the host's newer game, so you play a copy of the host's Henry this time.");
                }
            }
        }
        var pending = new PendingWorldLoad(rx.Offer.WorldId, _offerName, playline,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), characterSha,
            _rxFrom, rx.Offer.TransferId, info?.Hours ?? 0, info?.SavedUnix ?? 0, "Prepared");
        _reg.PendingLoad = pending; _reg.Save(_regPath);
        _store.InstallWorld(playline, rx.Offer.WorldId, bytes, _utc().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), replace);
        if (_reg.HomePlayline == playline) _reg.HomePlayline = -1;
        _reg.PendingLoad = pending with { Phase = "Installed" }; _reg.Save(_regPath);
        _requestedFrom = 0; _consentGiven = -1;
        Say(characterSha.Length > 0 ? $"Got {PeerName(_rxFrom)}'s game. Loading it with your own Henry..." : $"Got {PeerName(_rxFrom)}'s game. Loading it...");
        LoadPlayline(playline);
    }

    // ================================================================ sending a Henry home

    private async Task HenryHomeAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad || _requestedFrom != 0) { Show("Finish the current load first"); return; }
        if (_reg.HomeLeaseId.Length > 0)
        {
            Alert("Home world is archived", "Your home world is archived. Close the game and restore it with KcdUsAgent --slot-restore " + _reg.HomeLeaseId + " --cloud-sync-paused before sending Henry home.");
            return;
        }
        if (_reg.HomePlayline < 0 || _store.Newest(_reg.HomePlayline) is not { } home)
        {
            Alert("No home world known", "No home world is known: it is the game you played before joining a shared world.");
            return;
        }
        if (!InWorld) { Show("Load your Henry first", "Load your current Henry before sending him home."); return; }
        await CaptureCharacterAsync().ConfigureAwait(false);
        var prepared = ExactTraitsSave.PrepareCharacter(File.ReadAllBytes(home.Path), _characters.Read(_reg.MyCharacterSha256));
        var pending = new PendingWorldLoad("home-" + Guid.NewGuid().ToString("N"), "Home", home.Playline,
            Convert.ToHexString(SHA256.HashData(prepared)).ToLowerInvariant(), _reg.MyCharacterSha256, 0, "",
            home.Info.Hours, home.Info.SavedUnix, "Prepared", true);
        _reg.PendingLoad = pending; _reg.Save(_regPath);
        _store.InstallWorld(home.Playline, pending.WorldId, prepared, "home", true);
        _reg.PendingLoad = pending with { Phase = "Installed" }; _reg.Save(_regPath);
        Show("Taking your Henry home...", "Taking your Henry home. A copy of your home save is kept first.");
        LoadPlayline(_reg.HomePlayline);
    }

    // ================================================================ the clock

    /// <summary>About ten times a second, from the agent's loop.</summary>
    public void Tick()
    {
        long now = Environment.TickCount64;
        if (now - _lastTickMs < 1000) return;
        _lastTickMs = now;
        var s = _session();
        if (s.MyId == 0) { _announcedFor = -1; _announcedRoster = ""; }
        else if (s.MyId != _announcedFor || _announcedRoster != string.Join(',', s.Peers.Select(p => p.Id).OrderBy(id => id))) Enqueue(AnnounceAsync);
        // Join: ask until the host's game is here; a request nobody answered is asked again, never left blocking
        if (_wantJoin && s.MyId != 0 && !s.IsHost && s.HostId != 0 && _requestedFrom == 0 && _rx is null && !_expectLoad && _reg.PendingLoad is null && now >= _nextAskMs)
        {
            _nextAskMs = now + AskRetryMs;
            Enqueue(RequestAsync);
        }
        if (_requestedFrom != 0 && _rx is null && (s.MyId == 0 || now - _requestStartedMs > OfferWaitMs))
        {
            long requestStarted = _requestStartedMs;
            Enqueue(() =>
            {
                if (_requestedFrom != 0 && _rx is null && _requestStartedMs == requestStarted)
                {
                    _requestedFrom = 0; _nextAskMs = Environment.TickCount64 + 1000;
                    if (_wantJoin) SetStatus($"{HostName} did not answer: asking again");
                }
                return Task.CompletedTask;
            });
        }
        if (_rx is { } rx && now - _rxLastMs > ReceiveStallMs)
        {
            if (now - _rxLastMs > ReceiveGiveUpMs || s.MyId == 0)
            {
                Enqueue(() =>
                {
                    if (_rx == rx) { _rx = null; _requestedFrom = 0; _nextAskMs = Environment.TickCount64 + 1000; SetStatus("Transfer stopped: asking again"); }
                    return Task.CompletedTask;
                });
            }
            else if (now - _rxLastMs > ReceiveStallMs && (now - _rxLastMs) % ReceiveStallMs < 1100)
            {
                var miss = string.Join(',', rx.Missing().Take(150));
                s.SendEvent($"wmiss|{_rxFrom}|{rx.Offer.TransferId}|{miss}");
            }
        }
        if (InWorld && now - _lastCardMs > 120_000) { _lastCardMs = now; Enqueue(async () => { await CaptureCardAsync().ConfigureAwait(false); }); }
        if (InWorld && _reg.ActiveWorld is { Playline: < 0 } unbound && now - _lastBindCheckMs > 30_000)
        {
            _lastBindCheckMs = now;
            Enqueue(() => BindAsync(unbound));
        }
        // the host plays on: a save the game wrote by itself (sleeping, a story autosave, a load of another game) shows which game is the shared one now
        if (s.IsHost && s.MyId != 0 && InWorld && now - _lastFollowMs > 20_000)
        {
            _lastFollowMs = now;
            Enqueue(FollowHostGameAsync);
        }
    }

    private async Task FollowHostGameAsync()
    {
        if (_expectLoad || _reg.PendingLoad is not null || _sending != 0) return;
        if (_store.NewestSince(_followSinceUtc) is not { } f || f.ModifiedUtc <= _followSinceUtc) return;
        string before = _reg.Active;
        var w = BindHostGame(f); Persist();
        if (w.Id != before) { _log($"world: the host now plays \"{w.Name}\" (playline {f.Playline})"); await AnnounceAsync().ConfigureAwait(false); }
    }

    /// <summary>A "new world together" from an older agent has no playline until the game writes its first save; then that playline is the world.</summary>
    private async Task BindAsync(WorldRecord w)
    {
        if (w.Playline >= 0) return;
        if (_store.NewestSince(_utc().AddMinutes(-30)) is { } f)
        {
            w.Playline = f.Playline; Remember(w, f); Persist();
            _log($"world: \"{w.Name}\" is playline {f.Playline}");
            await AnnounceAsync().ConfigureAwait(false);
        }
    }
}
