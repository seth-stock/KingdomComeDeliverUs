// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using KcdUs.Agent.Ui;
using KcdUs.Wire;

namespace KcdUs.Agent.Worlds;

/// <summary>
/// Shared worlds (docs/SHARED-WORLDS.md). Each player holds their OWN copy of the world (a playline folder) and plays it alone or with friends; the mod
/// never merges two saves. When two players meet again, each side compares how far its copy has got and the one that is behind receives the other's
/// save (kept as a backup, never deleted) and loads it; each player's own Henry rides along as a card (skills, stats, perks, money, things) and is put
/// back onto whatever world he lands in. All of it runs on one worker, so the steps never race.
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
    private string? _pendingCard;            // put onto the Henry of the world that loads next
    private bool _expectLoad;
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
        _game.Line += OnGameLine;
        _worker = Task.Run(Work);
    }

    public void Dispose()
    {
        _game.Line -= OnGameLine;
        _cts.Cancel();
        _work.Writer.TryComplete();
    }

    private async Task Work()
    {
        await foreach (var job in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { await job().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception e) { _log("world: " + e.Message); }
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
            case "UILOAD" when g.Fields.Length > 1 && g.Fields[1] == "loaded":
                // a load started from a world straight into another one never says READY 0 then 1; the load graph says it is done
                Enqueue(() => OnReadyAsync(true));
                break;
            case "SAVEWORLD":
                _saveWait?.TrySetResult(g.Fields.Length > 2 && g.Fields[2] == "1");   // SAVEWORLD|tag|ok|error
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
        if (_expectLoad)
        {
            _expectLoad = false;
            _log("world: the shared world is loaded");
            if (!string.IsNullOrEmpty(_pendingCard)) { await StampAsync(_pendingCard!).ConfigureAwait(false); _pendingCard = null; }
            Say("The shared world is loaded.");
        }
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

    private async Task StampAsync(string card)
    {
        string id = Guid.NewGuid().ToString("N")[..6];
        const int piece = 600;
        int n = Math.Max(1, (card.Length + piece - 1) / piece);
        for (int i = 0; i < n; i++)
        {
            _game.Send($"CARDSET|{id}|{i}|{n}|{card.Substring(i * piece, Math.Min(piece, card.Length - i * piece))}");
            await Task.Delay(40, _cts.Token).ConfigureAwait(false);
        }
        _log("world: your Henry was put onto the world's");
    }

    // ================================================================ saving and sharing

    /// <summary>Asks the game to save now and waits for the file. Null when nothing was written (the prologue and some scenes refuse to save).</summary>
    public async Task<SaveFile?> SaveNowAsync()
    {
        if (!InWorld) return null;
        var since = _utc().AddSeconds(-1);
        _saveWait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _game.Send("SAVEWORLD|n");
        var end = _utc().AddMilliseconds(SaveWaitMs);
        SaveFile? found = null;
        while (_utc() < end && !_cts.IsCancellationRequested)
        {
            await Task.Delay(500, _cts.Token).ConfigureAwait(false);
            found = _store.NewestSince(since);
            if (found is not null && await StableAsync(found.Path).ConfigureAwait(false)) return _store.NewestSince(since);
        }
        return null;
    }

    private async Task<bool> StableAsync(string path)
    {
        long a = new FileInfo(path).Length;
        await Task.Delay(700, _cts.Token).ConfigureAwait(false);
        return new FileInfo(path).Length == a && SaveInfo.Validate(File.ReadAllBytes(path), out _);
    }

    /// <summary>"Save the world for everyone": save, make this game the shared world if there is none yet, and tell the friends how far it has got.</summary>
    private async Task SaveAndShareAsync()
    {
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
        var w = _reg.ActiveWorld;
        if (w is null || w.Playline < 0 || _store.Newest(w.Playline) is null) { Say("There is no shared world to play yet."); _title(MenuUi.Title.NoWorld); return; }
        if (InWorld) await CaptureCardAsync().ConfigureAwait(false);
        _pendingCard = null;   // your own world: your own Henry is already in it
        LoadPlayline(w.Playline);
    }

    private void LoadPlayline(int folder)
    {
        _expectLoad = true;
        _title(MenuUi.Title.WorldLoading);
        _game.Send($"LOAD|0|{folder}");   // the load graph takes the playline's folder number (seen in the retail game)
        _log($"world: loading the newest save of playline {folder}");
    }

    private async Task NewWorldAsync()
    {
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
        var s = _session();
        if (s.MyId == 0) return;
        _announcedFor = s.MyId;
        var w = _reg.ActiveWorld;
        if (w is null || w.Playline < 0) return;
        var f = _store.Newest(w.Playline);
        if (f is not null) Remember(w, f);
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
            case "wchunk" when Mine(1) && f.Length >= 5 && _rx is not null && _rx.Offer.TransferId == f[2] && int.TryParse(f[3], out int idx):
                _rx.Add(idx, f[4]); _rxLastMs = Environment.TickCount64;
                if (_rx.Complete) await FinishReceiveAsync().ConfigureAwait(false);
                break;
            case "wmiss" when Mine(1) && f.Length >= 4: await ResendAsync(from, f[2], f[3]).ConfigureAwait(false); break;
            case "wdone" when Mine(1) && f.Length >= 4:
                _log($"world: {PeerName(from)} {(f[3] == "1" ? "has your world" : "could not use it: " + (f.Length > 4 ? f[4] : "?"))}");
                if (f[3] != "1") Say("Your friend could not take the world: " + (f.Length > 4 ? f[4] : "?"));
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

    private async Task RequestAsync()
    {
        var s = _session();
        if (s.MyId == 0) { Say("Join a game first."); _title(MenuUi.Title.NeedHost); return; }
        int target = _behind?.From ?? (s.IsHost ? s.Peers.FirstOrDefault(p => p.Id != s.MyId)?.Id ?? 0 : s.HostId);
        if (target == 0) { Say("Nobody to take a world from."); _title(MenuUi.Title.NeedHost); return; }
        // your own Henry is read now, before the world he is in is replaced
        if (InWorld) await CaptureCardAsync().ConfigureAwait(false);
        if (_reg.HomePlayline < 0 && _store.NewestAny() is { } home && !_reg.SlotsInUse().Contains(home.Playline)) { _reg.HomePlayline = home.Playline; Persist(); }
        string id = _behind?.Stamp.WorldId ?? "-";
        var known = id == "-" ? _reg.ActiveWorld : _reg.Find(id);
        if (known is not { Slot: true } && _store.FreePlayline(_reg.SlotsInUse()) is null)
        {
            Say("All five playlines are in use. Delete one you do not need in the game's Load Game screen, then try again: the world needs a free slot (none of yours is touched).");
            _title(MenuUi.Title.WorldBusy);
            return;
        }
        s.SendEvent($"wreq|{target}|{id}");
        _rx = null;
        Say("Asking for the world...");
        _title(MenuUi.Title.WorldAsked);
        _rxLastMs = Environment.TickCount64;
    }

    private async Task ServeAsync(int to, string worldId)
    {
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
            _sentFile = (offer.TransferId, bytes);
            foreach (var (i, b64) in WorldTransfer.Pieces(bytes))
            {
                _session().SendEvent($"wchunk|{to}|{offer.TransferId}|{i}|{b64}");
                if (i % 8 == 7) await Task.Delay(40, _cts.Token).ConfigureAwait(false);   // the relay's queue is bounded; do not flood it
            }
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
    }

    private (string Id, byte[] Bytes)? _sentFile;

    private async Task ResendAsync(int to, string transferId, string list)
    {
        if (_sentFile is not { } sf || sf.Id != transferId) return;
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
        var w = _reg.Upsert(rx.Offer.WorldId, _offerName);
        int playline; bool replace;
        if (w.Slot && w.Playline >= 0) { playline = w.Playline; replace = true; }
        else
        {
            if (_store.FreePlayline(_reg.SlotsInUse()) is not { } free)
            {
                s.SendEvent($"wdone|{_rxFrom}|{rx.Offer.TransferId}|0|no free playline");
                Say("All five playlines are in use, so the world cannot be put anywhere. Delete one you do not need and ask again.");
                return;
            }
            playline = free; replace = false;
            if (w.Playline >= 0) w.HomePlayline = w.Playline;   // the copy that was here is the player's own game: it stays where it is
        }
        _store.InstallWorld(playline, w.Id, bytes, _utc().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), replace);
        w.Playline = playline; w.Slot = true;
        if (info is not null) { w.Hours = info.Hours; w.SavedUnix = info.SavedUnix; }
        _reg.Active = w.Id;
        _behind = null;
        Persist();
        s.SendEvent($"wdone|{_rxFrom}|{rx.Offer.TransferId}|1");
        // the player's own Henry goes into it (or the sender's Henry is what you play, in "host's Henry" mode)
        _pendingCard = MineMode && !string.IsNullOrEmpty(_reg.MyCard) ? _reg.MyCard : null;
        Say(MineMode ? "World received. Loading it with your own Henry." : "World received. Loading it.");
        LoadPlayline(playline);
    }

    // ================================================================ sending a Henry home

    private async Task HenryHomeAsync()
    {
        if (_reg.HomePlayline < 0 || _store.Newest(_reg.HomePlayline) is not { } home)
        {
            Say("No home world is known: it is the game you played before joining a shared world.");
            _title(MenuUi.Title.NoWorld);
            return;
        }
        var card = await CaptureCardAsync().ConfigureAwait(false) ?? _reg.MyCard;
        if (string.IsNullOrEmpty(card)) { Say("Your Henry could not be read. Load a world first."); return; }
        // the home save is copied away before anything is put onto it
        var backup = Path.Combine(_store.BackupRoot, "home", _utc().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backup);
        File.Copy(home.Path, Path.Combine(backup, Path.GetFileName(home.Path)), overwrite: true);
        _pendingCard = card;
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
