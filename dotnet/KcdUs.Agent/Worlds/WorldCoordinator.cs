// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Channels;
using KcdUs.Agent.Ui;
using KcdUs.Wire;

namespace KcdUs.Agent.Worlds;

/// <summary>
/// Shared worlds (docs/SHARED-WORLDS.md). Each player holds their OWN copy of the world (a playline folder) and plays it alone or with friends; the mod
/// never merges two saves. When two players meet again, each side compares how far its copy has got and the one that is behind receives the other's
/// save (kept as a backup, never deleted) and loads it. Personal native core/inventory snapshots are staged before loading;
/// world-owned state stays in the destination. One worker serializes disk operations; native readback acknowledges acceptance.
/// </summary>
public sealed class WorldCoordinator : IDisposable
{
    public const int ReceiveStallMs = 20_000;
    public const int SaveWaitMs = 25_000;
    public const int LoadingGraceMs = 120_000;

    private readonly AgentConfig _cfg;
    private readonly IGameLink _game;
    private readonly Func<Session> _session;
    private readonly SaveStore _store;
    private readonly CharacterCheckpoint _characters;
    private readonly WorldRegistry _reg;
    private readonly string? _regPath;
    private readonly Action<string> _log;
    private readonly Action<MenuUi.Title> _title;
    private readonly Func<DateTime> _utc;
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    private WorldTransfer.Receiver? _rx;
    private int _rxFrom;
    private long _rxLastMs;
    private bool _expectLoad;
    private bool _loadStarted;
    private int _requestedFrom;
    private string _requestedWorld = "";
    private bool _requestedMine;
    private long _requestStartedMs;
    private readonly Dictionary<string, (int Total, string?[] Parts)> _cardIn = new();
    private TaskCompletionSource<string>? _cardWait;
    private string _cardWaitId = "";
    private TaskCompletionSource<bool>? _saveWait;
    private int _announcedFor = -1;         // my relay id when the stamp was last announced
    private long _lastCardMs, _lastBindCheckMs, _lastTickMs;
    private bool _inWorldFlag;
    /// <summary>The player is in the open world: from the game's READY lines, or from the session (which also sees the position samples, so it is right after an agent restart).</summary>
    private bool InWorld => _inWorldFlag || _session().InWorld;
    private (int From, WorldStamp Stamp)? _behind;
    private int _sending;

    public WorldRegistry Registry => _reg;

    public WorldCoordinator(AgentConfig cfg, IGameLink game, Func<Session> session, SaveStore store, WorldRegistry reg, string? regPath, Action<string> log,
        Action<MenuUi.Title> title, Func<DateTime>? utcNow = null)
    {
        _cfg = cfg; _game = game; _session = session; _store = store; _reg = reg; _regPath = regPath; _log = log; _title = title; _utc = utcNow ?? (() => DateTime.UtcNow);
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
            catch (Exception e) { _log("world: " + e.Message); Say("World operation stopped: " + e.Message); }
        }
    }

    private void Enqueue(Func<Task> job) => _work.Writer.TryWrite(job);
    private void Persist() { try { _reg.Save(_regPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log("warning: the world list could not be saved"); } }
    private void Say(string t) => _session().Notify(t);
    private ResolvePolicy Policy => WorldResolve.ParsePolicy(_cfg.ResolvePolicy) ?? ResolvePolicy.Furthest;
    private bool MineMode => string.Equals(_cfg.HenryMode, "mine", StringComparison.OrdinalIgnoreCase);

    // ================================================================ the menu's buttons

    public void Menu(string verb, string arg)
    {
        switch (verb, arg)
        {
            case ("world", "save"): Enqueue(SaveAndShareAsync); break;
            case ("world", "play"): Enqueue(PlayAsync); break;
            case ("world", "join"): Enqueue(RequestAsync); break;
            case ("world", "new"): Enqueue(NewWorldAsync); break;
            case ("henry", "home"): Enqueue(HenryHomeAsync); break;
            case ("henry", "host" or "mine"):
                _cfg.HenryMode = arg; PersistConfig();
                _title(arg == "mine" ? MenuUi.Title.HenryMine : MenuUi.Title.HenryHost);
                break;
            case ("resolve", _) when WorldResolve.ParsePolicy(arg) is { } p:
                _cfg.ResolvePolicy = WorldResolve.PolicyName(p); PersistConfig();
                _title(p == ResolvePolicy.Host ? MenuUi.Title.ResolveHost : p == ResolvePolicy.Newest ? MenuUi.Title.ResolveNewest : MenuUi.Title.ResolveFurthest);
                Enqueue(AnnounceAsync);   // the answer may change
                break;
        }
    }

    private void PersistConfig() { try { _cfg.Save(SavePath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    public string? SavePath { get; set; }

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
        _inWorldFlag = inWorld;
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
        if (_reg.PendingLoad is not { } pending) { _expectLoad = false; await AnnounceAsync(); return; }
        try
        {
            // The UI event precedes the end of native loading. SaveViaResting
            // silently does nothing in that short interval despite returning.
            await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
            // A generic READY/UI notification is not acceptance. A native save must
            // come from the installed playline and retain the staged personal state.
            var saved = await SaveNowAsync(pending.Playline).ConfigureAwait(false)
                ?? throw new IOException("The engine did not save the loaded world; acceptance remains pending.");
            if (pending.CharacterSha256.Length > 0 && !ExactTraitsSave.SameProgressionAndInventory(
                _characters.Read(pending.CharacterSha256), ExactTraitsSave.CaptureCharacter(File.ReadAllBytes(saved.Path))))
                throw new InvalidDataException("Native character readback differs; acceptance remains pending.");
            if (!pending.Home)
            {
                var w = _reg.Upsert(pending.WorldId, pending.Name);
                if (w.Playline >= 0 && w.Playline != pending.Playline && !w.Slot) w.HomePlayline = w.Playline;
                w.Playline = pending.Playline; w.Slot = true; Remember(w, saved); _reg.Active = w.Id;
                // The readback save verifies acceptance; it is not a new shared
                // checkpoint. Preserve the sender's stamp to prevent resync loops.
                w.Hours = pending.Hours; w.SavedUnix = pending.SavedUnix;
                w.VerificationSaveSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(saved.Path))).ToLowerInvariant();
            }
            else _reg.Active = _reg.Worlds.FirstOrDefault(w => w.Playline == pending.Playline)?.Id ?? "";
            _reg.PendingLoad = null;
            _reg.Save(_regPath);
            _expectLoad = false; _loadStarted = false; _behind = null;
            if (pending.From > 0) _session().SendEvent($"wdone|{pending.From}|{pending.TransferId}|1");
            Say("The shared world loaded and passed native save readback.");
            await AnnounceAsync();
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        { _reg.PendingLoad = pending; _log("world: " + e.Message); Say(e.Message + " Your character snapshot and previous saves are preserved."); }
    }

    // ================================================================ saving and sharing

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
        if (saved is null) throw new IOException("Henry could not be saved. Finish the prologue/cutscene before moving him.");
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

    /// <summary>"Save the world for everyone": save, make this game the shared world if there is none yet, and tell the friends how far it has got.</summary>
    private async Task SaveAndShareAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad) { Say("Finish the pending world load before publishing a checkpoint."); return; }
        var f = await SaveNowAsync().ConfigureAwait(false);
        if (f is null) { Say("The game would not save just now (a cutscene or the prologue). Try again in a moment."); _title(MenuUi.Title.WorldBusy); return; }
        var w = _reg.ActiveWorld;
        if (w is null)
        {
            w = _reg.Upsert(WorldRegistry.NewId(), _cfg.WorldName);
            w.Playline = f.Playline; _reg.Active = w.Id;
            Say($"This game is now your shared world \"{w.Name}\".");
        }
        else if (w.Playline < 0) w.Playline = f.Playline;
        else if (w.Playline != f.Playline)
        {
            Say("This is not your shared world. Use Game world > Play my shared world to load it.");
            _title(MenuUi.Title.NoWorld);
            return;
        }
        Remember(w, f);
        await CaptureCardAsync().ConfigureAwait(false);
        Persist();
        Say("World saved.");
        _title(MenuUi.Title.WorldSaved);
        await AnnounceAsync().ConfigureAwait(false);
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
        if (w is null || w.Playline < 0 || _store.Newest(w.Playline) is null) { Say("There is no shared world to play yet."); _title(MenuUi.Title.NoWorld); return; }
        if (InWorld) await CaptureCardAsync().ConfigureAwait(false);
        LoadPlayline(w.Playline);
    }

    private void LoadPlayline(int folder)
    {
        _expectLoad = true;
        _loadStarted = false;
        _title(MenuUi.Title.WorldLoading);
        _game.Send($"LOAD|0|{folder}");   // the load graph takes the playline's folder number (seen in the retail game)
        _log($"world: loading the newest save of playline {folder}");
    }

    private async Task NewWorldAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad) { Say("Finish the pending world load before starting another world."); return; }
        var s = _session();
        if (s.MyId == 0) { Say("Host or join a game first."); _title(MenuUi.Title.NeedHost); return; }
        if (!s.IsHost) { Say("Only the host starts a new shared world; ask them to."); return; }
        var w = _reg.Upsert(WorldRegistry.NewId(), _cfg.WorldName);
        w.Playline = -1; w.Hours = 0; w.SavedUnix = 0; _reg.Active = w.Id; Persist();
        s.SendEvent($"wnew|{w.Id}|{Safe.Clean(w.Name, 40)}");
        Say("New shared world: you both start a New Game now, with new characters.");
        _title(MenuUi.Title.WorldNew);
        await Task.CompletedTask;
    }

    // ================================================================ talking about it

    private async Task AnnounceAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad) return;
        var s = _session();
        if (s.MyId == 0) return;
        _announcedFor = s.MyId;
        var w = _reg.ActiveWorld;
        if (w is null || w.Playline < 0) return;
        var f = _store.Newest(w.Playline);
        if (f is not null && (w.VerificationSaveSha256.Length == 0 ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Path))).ToLowerInvariant() != w.VerificationSaveSha256)) Remember(w, f);
        s.SendEvent("wstamp|" + w.Stamp.Encode());
        await Task.CompletedTask;
    }

    /// <summary>A shared-world message from the relay (queued; runs on the worker).</summary>
    public void OnPeerEvent(int from, string[] f) => Enqueue(() => HandleEventAsync(from, f));

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
            case "wnew" when f.Length >= 3 && !s.IsHost && from == s.HostId:
                {
                    var w = _reg.Upsert(Safe.Clean(f[1], 40), Safe.Clean(f[2], 40));
                    w.Playline = -1; w.Hours = 0; w.SavedUnix = 0; _reg.Active = w.Id; Persist();
                    Say($"The host is starting a new shared world \"{w.Name}\". Start a New Game now.");
                    _title(MenuUi.Title.WorldNew);
                    break;
                }
            case "wreq" when Mine(1): await ServeAsync(from, f.Length > 2 ? f[2] : "-").ConfigureAwait(false); break;
            case "woffer" when Mine(1) && f.Length >= 8: OnOffer(from, f); break;
            case "wchunk" when Mine(1) && f.Length >= 5 && _rx is not null && from == _rxFrom && _rx.Offer.TransferId == f[2] && int.TryParse(f[3], out int idx):
                _rx.Add(idx, f[4]); _rxLastMs = Environment.TickCount64;
                if (_rx.Complete) await FinishReceiveAsync().ConfigureAwait(false);
                break;
            case "wmiss" when Mine(1) && f.Length >= 4: await ResendAsync(from, f[2], f[3]).ConfigureAwait(false); break;
            case "wdone" when Mine(1) && f.Length >= 4:
                _log($"world: {PeerName(from)} {(f[3] == "1" ? "has your world" : "could not use it: " + (f.Length > 4 ? f[4] : "?"))}");
                if (f[3] != "1")
                {
                    if (_requestedFrom == from) { _requestedFrom = 0; _rx = null; }
                    Say("World transfer refused: " + (f.Length > 4 ? f[4] : "?"));
                }
                break;
        }
    }

    private string PeerName(int id) => _session().Peers.FirstOrDefault(p => p.Id == id)?.Name ?? ("#" + id);

    private void OnStamp(int from, WorldStamp theirs)
    {
        var s = _session();
        var mine = _reg.Find(theirs.WorldId);
        if (mine is null || mine.Playline < 0)
        {
            if (_reg.ActiveWorld is null || s.IsHost == false)
            {
                _behind = (from, theirs);
                Say($"{PeerName(from)} has the shared world \"{theirs.Name}\". Game world > Join the host's world gets you into it.");
                _title(MenuUi.Title.NoWorld);
            }
            return;
        }
        var verdict = WorldResolve.Decide(mine.Stamp, theirs, Policy, s.IsHost);
        switch (verdict)
        {
            case Winner.Same:
                _behind = null;
                _title(MenuUi.Title.InSync);
                break;
            case Winner.Remote:
                _behind = (from, theirs);
                if (!InWorld && _cfg.AutoSync)
                {
                    Say($"{PeerName(from)}'s copy of \"{theirs.Name}\" is further along: taking it.");
                    Enqueue(RequestAsync);
                }
                else Say($"{PeerName(from)}'s copy of \"{theirs.Name}\" is further along ({theirs.Hours:0.0} h against your {mine.Hours:0.0} h). Game world > Join the host's world takes it; yours is kept as a backup.");
                break;
            case Winner.Local:
                _behind = null;   // they will ask for ours if they want it
                break;
        }
    }

    // ================================================================ taking a world

    private const string ContentRefusal = "Your game has other mods than your friend's, so a world or a Henry cannot be moved between you safely. Install the same mods on both, or play without sharing a world.";

    private static string DlcRefusal(IReadOnlyList<string> lacks) =>
        $"Your host's game has DLC you do not have ({string.Join(", ", lacks)}), and their saved world needs it. Get that DLC, or ask your host to play without it.";

    private async Task RequestAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad) { Say("Finish the pending world load before receiving another world."); return; }
        if (_requestedFrom != 0 || _rx is not null) { Say("A world transfer is already pending."); return; }
        var s = _session();
        if (s.MyId == 0) { Say("Join a game first."); _title(MenuUi.Title.NeedHost); return; }
        if (!s.ContentMatches) { Say(ContentRefusal); _title(MenuUi.Title.WorldBusy); return; }
        int target = _behind?.From ?? (s.IsHost ? s.Peers.FirstOrDefault(p => p.Id != s.MyId)?.Id ?? 0 : s.HostId);
        if (!s.IsHost && target == s.HostId && s.DlcLacks.Count > 0) { Say(DlcRefusal(s.DlcLacks)); _title(MenuUi.Title.WorldBusy); return; }
        if (target == 0) { Say("Nobody to take a world from."); _title(MenuUi.Title.NeedHost); return; }
        string id = _behind?.Stamp.WorldId ?? "-";
        var known = id == "-" ? _reg.ActiveWorld : _reg.Find(id);
        if (known is not { Slot: true } && _store.FreePlayline(_reg.SlotsInUse()) is null)
        {
            Say("All five playlines are in use. Delete one you do not need in the game's Load Game screen, then try again: the world needs a free slot (none of yours is touched).");
            _title(MenuUi.Title.WorldBusy);
            return;
        }
        _requestedMine = MineMode;
        if (_requestedMine)
        {
            int? source = InWorld ? await CaptureCharacterAsync().ConfigureAwait(false) : null;
            if (_reg.MyCharacterSha256.Length == 0) { Say("Load and save your Henry first, then join the world with Bring my Henry."); return; }
            _ = _characters.Read(_reg.MyCharacterSha256);
            if (source is { } home && _reg.HomePlayline < 0 && !_reg.SlotsInUse().Contains(home))
            { _reg.HomePlayline = home; _reg.Save(_regPath); }
        }
        else if (InWorld && _reg.HomePlayline < 0 && _reg.HomeLeaseId.Length == 0)
        {
            // Establish home from the engine's fresh save, never another
            // playline's unrelated newest file.
            if (await SaveNowAsync().ConfigureAwait(false) is { } home && !_reg.SlotsInUse().Contains(home.Playline))
            { _reg.HomePlayline = home.Playline; _reg.Save(_regPath); }
        }
        _requestedFrom = target; _requestedWorld = id;
        _requestStartedMs = Environment.TickCount64;
        if (!s.SendEvent($"wreq|{target}|{id}")) { _requestedFrom = 0; Say("Reconnect to the relay and request the world again."); return; }
        _rx = null;
        Say("Asking for the world...");
        _title(MenuUi.Title.WorldAsked);
        _rxLastMs = Environment.TickCount64;
    }

    private async Task ServeAsync(int to, string worldId)
    {
        if (!_session().ContentMatches) { _session().SendEvent($"wdone|{to}|-|0|different mods"); return; }
        if (_session().PeerLacksDlc(to) is { Count: > 0 } lacks) { _session().SendEvent($"wdone|{to}|-|0|you lack DLC the world needs ({string.Join(", ", lacks)})"); return; }
        if (Interlocked.Exchange(ref _sending, 1) == 1) { _session().SendEvent($"wdone|{to}|-|0|busy"); return; }
        try
        {
            var w = (worldId != "-" ? _reg.Find(worldId) : null) ?? _reg.ActiveWorld;
            if (w is null || w.Playline < 0) { _session().SendEvent($"wdone|{to}|-|0|no world here"); return; }
            SaveFile? f = null;
            if (InWorld && _store.Newest(w.Playline) is { } cur) f = await SaveNowAsync().ConfigureAwait(false) is { } fresh && fresh.Playline == w.Playline ? fresh : cur;
            f ??= _store.Newest(w.Playline);
            if (f is null) { _session().SendEvent($"wdone|{to}|-|0|no save"); return; }
            Remember(w, f); Persist();
            var bytes = await File.ReadAllBytesAsync(f.Path).ConfigureAwait(false);
            var offer = WorldTransfer.MakeOffer(Guid.NewGuid().ToString("N")[..6], w.Id, bytes);
            _session().SendEvent($"woffer|{to}|{offer.TransferId}|{offer.WorldId}|{offer.Bytes}|{offer.Chunks}|{offer.Sha256}|{w.Stamp.Encode().Split('|', 2)[1]}|{Safe.Clean(w.Name, 40)}");
            _title(MenuUi.Title.WorldSent);
            Say($"Sending your world ({bytes.Length / 1024 / 1024.0:0.0} MB)...");
            _sentFile = (offer.TransferId, bytes, to);
            foreach (var (i, b64) in WorldTransfer.Pieces(bytes))
            {
                _session().SendEvent($"wchunk|{to}|{offer.TransferId}|{i}|{b64}");
                if (i % 8 == 7) await Task.Delay(40, _cts.Token).ConfigureAwait(false);   // the relay's queue is bounded; do not flood it
            }
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
            if (from != _requestedFrom || _requestedFrom == 0 || _rx is not null
                || (_requestedWorld != "-" && f[3] != _requestedWorld)) return;
            if (f.Length < 10 || !int.TryParse(f[4], out int bytes) || !int.TryParse(f[5], out int chunks)) return;
            _rx = new WorldTransfer.Receiver(new WorldTransfer.Offer(f[2], Safe.Clean(f[3], 40), bytes, chunks, f[6].ToLowerInvariant()));
            _rxFrom = from; _rxLastMs = Environment.TickCount64;
            _offerName = Safe.Clean(f[9], 40);
            _log($"world: receiving \"{_offerName}\" ({bytes / 1024 / 1024.0:0.0} MB)");
            _title(MenuUi.Title.WorldReceiving);
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
            Say("The world arrived damaged. Ask again.");
            return;
        }
        var info = SaveInfo.Read(bytes);
        var w = _reg.Find(rx.Offer.WorldId);
        int playline; bool replace;
        if (w is { Slot: true, Playline: >= 0 }) { playline = w.Playline; replace = true; }
        else
        {
            if (_store.FreePlayline(_reg.SlotsInUse()) is not { } free)
            {
                s.SendEvent($"wdone|{_rxFrom}|{rx.Offer.TransferId}|0|no free playline");
                Say("All five playlines are in use, so the world cannot be put anywhere. Delete one you do not need and ask again.");
                return;
            }
            playline = free; replace = false;
        }
        if (_requestedMine)
        {
            if (InWorld) await CaptureCharacterAsync().ConfigureAwait(false); // Include play during the transfer.
            bytes = ExactTraitsSave.PrepareCharacter(bytes, _characters.Read(_reg.MyCharacterSha256));
        }
        var pending = new PendingWorldLoad(rx.Offer.WorldId, _offerName, playline,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), _requestedMine ? _reg.MyCharacterSha256 : "",
            _rxFrom, rx.Offer.TransferId, info?.Hours ?? 0, info?.SavedUnix ?? 0, "Prepared");
        _reg.PendingLoad = pending; _reg.Save(_regPath);
        _store.InstallWorld(playline, rx.Offer.WorldId, bytes, _utc().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), replace);
        _reg.PendingLoad = pending with { Phase = "Installed" }; _reg.Save(_regPath);
        _requestedFrom = 0;
        Say(_requestedMine ? "World received. Loading your saved Henry." : "World received. Loading it.");
        LoadPlayline(playline);
    }

    // ================================================================ sending a Henry home

    private async Task HenryHomeAsync()
    {
        if (_reg.PendingLoad is not null || _expectLoad || _requestedFrom != 0) { Say("Finish the pending world operation before sending Henry home."); return; }
        if (_reg.HomeLeaseId.Length > 0)
        {
            Say("Your home world is archived. Close the game and restore it with KcdUsAgent --slot-restore " + _reg.HomeLeaseId + " --cloud-sync-paused before sending Henry home.");
            return;
        }
        if (_reg.HomePlayline < 0 || _store.Newest(_reg.HomePlayline) is not { } home)
        {
            Say("No home world is known: it is the game you played before joining a shared world.");
            _title(MenuUi.Title.NoWorld);
            return;
        }
        if (!InWorld) { Say("Load your current Henry before sending him home."); return; }
        await CaptureCharacterAsync().ConfigureAwait(false);
        var prepared = ExactTraitsSave.PrepareCharacter(File.ReadAllBytes(home.Path), _characters.Read(_reg.MyCharacterSha256));
        var pending = new PendingWorldLoad("home-" + Guid.NewGuid().ToString("N"), "Home", home.Playline,
            Convert.ToHexString(SHA256.HashData(prepared)).ToLowerInvariant(), _reg.MyCharacterSha256, 0, "",
            home.Info.Hours, home.Info.SavedUnix, "Prepared", true);
        _reg.PendingLoad = pending; _reg.Save(_regPath);
        _store.InstallWorld(home.Playline, pending.WorldId, prepared, "home", true);
        _reg.PendingLoad = pending with { Phase = "Installed" }; _reg.Save(_regPath);
        _title(MenuUi.Title.HenryHome);
        Say("Taking your Henry home. A copy of your home save is kept first.");
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
        if (s.MyId != 0 && s.MyId != _announcedFor) Enqueue(AnnounceAsync);
        if (_requestedFrom != 0 && (s.MyId == 0 || now - _requestStartedMs > LoadingGraceMs))
        {
            long requestStarted = _requestStartedMs;
            Enqueue(() => {
                if (_requestedFrom != 0 && _requestStartedMs == requestStarted)
                { _requestedFrom = 0; _rx = null; Say("The world request expired. Your saves are unchanged; request it again after reconnecting."); }
                return Task.CompletedTask;
            });
        }
        if (_rx is { } rx && now - _rxLastMs > ReceiveStallMs)
        {
            _rxLastMs = now;
            var miss = string.Join(',', rx.Missing().Take(150));
            s.SendEvent($"wmiss|{_rxFrom}|{rx.Offer.TransferId}|{miss}");
        }
        if (InWorld && now - _lastCardMs > 120_000) { _lastCardMs = now; Enqueue(async () => { await CaptureCardAsync().ConfigureAwait(false); }); }
        if (InWorld && _reg.ActiveWorld is { Playline: < 0 } unbound && now - _lastBindCheckMs > 30_000)
        {
            _lastBindCheckMs = now;
            Enqueue(() => BindAsync(unbound));
        }
    }

    /// <summary>A "new world together" has no playline until the game writes its first save; then that playline is the world.</summary>
    private async Task BindAsync(WorldRecord w)
    {
        if (w.Playline >= 0) return;
        if (_store.NewestSince(_utc().AddMinutes(-30)) is { } f)
        {
            w.Playline = f.Playline; Remember(w, f); Persist();
            _log($"world: \"{w.Name}\" is playline {f.Playline}");
            Say($"Your new shared world \"{w.Name}\" has its first save.");
            await AnnounceAsync().ConfigureAwait(false);
        }
    }
}
