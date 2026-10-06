// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
// Modified version of RailsChoice.cs of Kingdom Come: Together (https://github.com/DeepFriedDepp/KingdomCome-Together).
namespace KcdUs.Agent;

/// <summary>What a friend decided about a stretch of the host's story.</summary>
public enum RailsChoice : byte { Pending = 0, Join = 1, Free = 2 }

/// <summary>The friend's standing answer (the launcher setting, or kcdus_join / kcdus_stay): ask every time, always join, or always stay in the open world.</summary>
public enum RailsPref : byte { Ask = 0, Join = 1, Free = 2 }

/// <summary>
/// The choice (docs/quest-gating.md): the rules of "join the host, or stay in the open world", pure. Nothing here touches the game
/// or the wire; Session owns those and RailsTests pins these.
///
/// The idea: while the host is on rails (a locked story period, StorySections) the FRIEND decides whether to go along. Going
/// along means being brought beside the host and, in a rails section, kept within the tether. Staying means the friend is left
/// alone: not brought, not tethered; when the period ends the ordinary rules come back.
/// </summary>
public static class RailsRules
{
    /// <summary>How long the friend's question stays on screen. Nobody answering is a yes: the friends stay together unless one opts out.</summary>
    public const int PromptSeconds = 30;
    /// <summary>The agent's own backstop for a question the game never showed (a loading screen, no plugin): this long after the period began.</summary>
    public const int AskBackstopMs = 45_000;
    /// <summary>
    /// HOST: a friend that has not answered this long after the period began (or after the host first saw them) is taken as joined and
    /// brought. Longer than the worst case of asking: a friend who arrives mid-period hears the host's beat on its next 30 s repeat and
    /// then has 30 s to answer.
    /// </summary>
    public const int HostGraceMs = 75_000;
    /// <summary>FRIEND: the answer is repeated this often while the period lasts, so a lost message or a host that reloaded heals.</summary>
    public const int ResendMs = 20_000;
    /// <summary>FRIEND: a section ended; the next one of the SAME period usually follows within this long. After it, the period is over.</summary>
    public const int PeriodEndGraceMs = 8_000;
    /// <summary>The words of the pull that follows a join.</summary>
    public const string JoinedText = "You joined your host.";

    public static RailsPref? ParsePref(string? s)
    {
        string v = (s ?? "").Trim().ToLowerInvariant();
        return v switch { "ask" => RailsPref.Ask, "join" => RailsPref.Join, "free" or "stay" or "open" => RailsPref.Free, _ => null };
    }

    /// <summary>"join" | "free" (the mod's words) or null.</summary>
    public static RailsChoice? ParseChoiceWord(string? s) => s switch { "join" => RailsChoice.Join, "free" => RailsChoice.Free, _ => null };

    /// <summary>What a preference means, for the player's eyes.</summary>
    public static string PrefText(RailsPref p) => p switch
    {
        RailsPref.Join => "you join your host automatically",
        RailsPref.Free => "you stay in the open world automatically",
        _ => "you are asked each time",
    };

    public static string PrefName(RailsPref p) => p switch { RailsPref.Join => "join", RailsPref.Free => "free", _ => "ask" };
    public static string ChoiceName(RailsChoice c) => c switch { RailsChoice.Join => "join", RailsChoice.Free => "free", _ => "pending" };

    /// <summary>The wire text of an answer: "q_skalitz join" / "q_skalitz free" (any code of the period names it).</summary>
    public static string ChoiceText(string code, RailsChoice c) => code + " " + ChoiceName(c);

    /// <summary>A friend's answer as it arrives: a known section code and join|free, nothing else.</summary>
    public static bool TryParseChoice(string? text, out StoryPeriod period, out RailsChoice choice)
    {
        period = null!;
        choice = RailsChoice.Pending;
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (StorySections.PeriodOf(parts[0]) is not { } p) return false;
        if (ParseChoiceWord(parts[1]) is not { } c) return false;
        period = p;
        choice = c;
        return true;
    }

    /// <summary>The words for the friend's question (the mod has the same, for the screen).</summary>
    public static string Ask(string why) => "Your host entered " + why + ". Join them, or stay in the open world?";
}

/// <summary>
/// HOST: what each friend answered for the story period the host is in now. A friend that has not answered yet, or stays free, is
/// EXEMPT: nothing moves them (no leash, no bring-along); a friend that joined follows as before.
/// </summary>
public sealed class RailsRoster
{
    private string? _period;
    private long _endsAtMs;   // the host's last section ended: the period lasts this long more, so a short gap before the next quest keeps the answers
    private readonly Dictionary<byte, (RailsChoice Choice, long AtMs)> _by = new();

    /// <summary>The period the host is in (or has just left, for the grace), or null.</summary>
    public string? Period => _period;

    /// <summary>True while the host's last section has ended and the grace has not run out.</summary>
    public bool Ending => _endsAtMs != 0;

    /// <summary>The host entered a period. Returns true when it is a NEW one (every friend is asked again); the next section of the same period, even after a short gap, changes nothing.</summary>
    public bool Start(string periodId)
    {
        _endsAtMs = 0;
        if (string.Equals(_period, periodId, StringComparison.Ordinal)) return false;
        _period = periodId;
        _by.Clear();
        return true;
    }

    /// <summary>The session reset or the story lock went off: nobody is exempt any more, at once.</summary>
    public void End() { _period = null; _endsAtMs = 0; _by.Clear(); }

    /// <summary>
    /// The host's last section ended. The friends' side keeps the period for <see cref="RailsRules.PeriodEndGraceMs"/> (the next quest of it
    /// usually follows within seconds), so the host does the same; <see cref="Tick"/> lets go when it runs out.
    /// </summary>
    public void EndSoon(long nowMs) { if (_period is not null && _endsAtMs == 0) _endsAtMs = nowMs + RailsRules.PeriodEndGraceMs; }

    public void Forget(byte joiner) => _by.Remove(joiner);

    /// <summary>Ignored = it names another period than the host's now; NoPeriod = the host is not in one (a late repeat of an answer: not worth a log line).</summary>
    public enum Outcome { Ignored, Joined, Freed, Unchanged, NoPeriod }

    /// <summary>A friend's answer.</summary>
    public Outcome Choose(byte joiner, string periodId, RailsChoice choice, long nowMs)
    {
        if (_period is null) return Outcome.NoPeriod;
        if (!string.Equals(_period, periodId, StringComparison.Ordinal) || choice == RailsChoice.Pending) return Outcome.Ignored;
        if (_by.TryGetValue(joiner, out var cur) && cur.Choice == choice) return Outcome.Unchanged;
        _by[joiner] = (choice, nowMs);
        return choice == RailsChoice.Join ? Outcome.Joined : Outcome.Freed;
    }

    /// <summary>
    /// Once a second, with the friends in the host's world now: makes sure each has an entry (a friend that arrived mid-period is
    /// asked from its own first sight) and returns the ones whose time ran out, now taken as joined: they are owed their bring-along.
    /// </summary>
    public List<byte> Tick(long nowMs, IEnumerable<byte> joiners)
    {
        var expired = new List<byte>();
        if (_period is null) return expired;
        if (_endsAtMs != 0 && nowMs >= _endsAtMs) { End(); return expired; }
        var present = new HashSet<byte>(joiners);
        foreach (byte id in _by.Keys.ToArray()) if (!present.Contains(id)) _by.Remove(id);
        foreach (byte id in present)
        {
            if (!_by.TryGetValue(id, out var e)) { _by[id] = (RailsChoice.Pending, nowMs); continue; }
            if (e.Choice == RailsChoice.Pending && nowMs - e.AtMs >= RailsRules.HostGraceMs)
            {
                _by[id] = (RailsChoice.Join, nowMs);
                expired.Add(id);
            }
        }
        return expired;
    }

    /// <summary>What the host knows of a friend: Pending until it answers. Outside a period, everyone is Join (nobody is exempt).</summary>
    public RailsChoice ChoiceOf(byte joiner) =>
        _period is null ? RailsChoice.Join
        : _by.TryGetValue(joiner, out var e) ? e.Choice
        : RailsChoice.Pending;   // unknown to the roster: not moved without its say

    /// <summary>True = nothing may move this friend (no leash pull, no bring-along): it stays free, or has not answered yet.</summary>
    public bool Exempt(byte joiner) => ChoiceOf(joiner) != RailsChoice.Join;

    /// <summary>Called when the host's period starts: friends already in the world are Pending from that moment.</summary>
    public void SeedPending(IEnumerable<byte> joiners, long nowMs)
    {
        if (_period is null) return;
        foreach (byte id in joiners) if (!_by.ContainsKey(id)) _by[id] = (RailsChoice.Pending, nowMs);
    }

    public int Count(RailsChoice c) => _by.Values.Count(v => v.Choice == c);
}

/// <summary>
/// FRIEND: this player's side of the question, pure. The agent feeds it the host's section beats and the clock and does what it
/// says (ask on screen, tell the host, bring the friend along).
/// </summary>
public sealed class RailsJoiner
{
    public enum Act { None, Ask, Auto, Resend, Over, Backstop }

    /// <summary>What to do and, for Ask/Auto, which choice.</summary>
    public readonly record struct Step(Act Act, RailsChoice Choice = RailsChoice.Pending, StoryPeriod? Period = null, string Why = "");

    private string? _period;
    private RailsChoice _choice = RailsChoice.Pending;
    private long _startMs, _sentMs, _graceEndMs;
    private bool _asked;

    public string? Period => _period;
    public RailsChoice Choice => _choice;
    public bool IsFree => _period is not null && _choice == RailsChoice.Free;
    public bool Open => _period is not null;
    public bool Asking => _period is not null && _choice == RailsChoice.Pending && _asked;

    public void Reset() { _period = null; _choice = RailsChoice.Pending; _graceEndMs = 0; _asked = false; }

    /// <summary>The host's "entered section <paramref name="code"/>" beat (it repeats every 30 s).</summary>
    public Step OnEnter(string code, long nowMs, RailsPref pref)
    {
        if (StorySections.PeriodOf(code) is not { } p) return default;
        _graceEndMs = 0;   // the section after a Leave: the period goes on
        if (string.Equals(_period, p.Id, StringComparison.Ordinal)) return default;
        // a new period
        _period = p.Id;
        _startMs = nowMs;
        _sentMs = 0;
        _asked = false;
        switch (pref)
        {
            case RailsPref.Join: _choice = RailsChoice.Join; _sentMs = nowMs; return new Step(Act.Auto, RailsChoice.Join, p, "your setting");
            case RailsPref.Free: _choice = RailsChoice.Free; _sentMs = nowMs; return new Step(Act.Auto, RailsChoice.Free, p, "your setting");
            default: _choice = RailsChoice.Pending; _asked = true; return new Step(Act.Ask, RailsChoice.Pending, p);
        }
    }

    /// <summary>The friend's answer (F11, F12, a command, the question timing out). False = nothing to answer.</summary>
    public bool Decide(RailsChoice choice, long nowMs)
    {
        if (_period is null || choice == RailsChoice.Pending) return false;
        _choice = choice;
        _asked = false;
        _sentMs = nowMs;
        return true;
    }

    /// <summary>The host's "left section <paramref name="code"/>" beat. The period is over only if no section of it follows within the grace.</summary>
    public void OnLeave(string code, long nowMs)
    {
        if (_period is null || StorySections.PeriodOf(code) is not { } p || !string.Equals(p.Id, _period, StringComparison.Ordinal)) return;
        _graceEndMs = nowMs + RailsRules.PeriodEndGraceMs;
    }

    /// <summary>The host went quiet (90 s without its beat) or a load: the period is over now, no grace.</summary>
    public StoryPeriod? ForceOver()
    {
        var p = _period is null ? null : StorySections.PeriodById(_period);
        Reset();
        return p;
    }

    /// <summary>Once a second: the period's end after its grace, the answer repeated, an unanswered question given up.</summary>
    public Step Tick(long nowMs)
    {
        if (_period is null) return default;
        if (_graceEndMs != 0 && nowMs >= _graceEndMs)
        {
            var p = StorySections.PeriodById(_period);
            var was = _choice;
            Reset();
            return new Step(Act.Over, was, p);
        }
        if (_choice == RailsChoice.Pending && _asked && nowMs - _startMs >= RailsRules.AskBackstopMs)
            return new Step(Act.Backstop, RailsChoice.Join, StorySections.PeriodById(_period), "no answer");
        if (_choice != RailsChoice.Pending && nowMs - _sentMs >= RailsRules.ResendMs)
        {
            _sentMs = nowMs;
            return new Step(Act.Resend, _choice, StorySections.PeriodById(_period));
        }
        return default;
    }
}
