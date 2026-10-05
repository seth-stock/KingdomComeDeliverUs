// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using KcdUs.Agent;

namespace KcdUs.Tests;

public class QuestCatalogTests
{
    [Fact]
    public void The_catalog_holds_every_quest_of_the_game()
    {
        Assert.Equal(287, QuestCatalog.All.Length);
        Assert.Equal(QuestCatalog.All.Length, QuestCatalog.All.Select(q => q.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_main_story_is_the_games_twenty_nine_quests_in_order()
    {
        var main = StorySections.Main;
        Assert.Equal(30, main.Count); // 29 titles, and Rocketeer shares Siege's place
        Assert.Equal("q_skalitz", main[0].Code);
        Assert.Equal("Unexpected Visit", main[0].Title);
        Assert.Equal("q_epilogue", main[^1].Code);
        var titles = main.Select(m => m.Title).ToHashSet();
        // the wiki's twenty-nine, plus Rocketeer (a main-type quest inside Siege)
        string[] expected = { "Unexpected Visit", "Run!", "Homecoming", "Awakening", "Train Hard, Fight Easy", "Keeping the Peace", "The Prey",
            "The Hunt Begins", "Ginger in a Pickle", "Mysterious Ways", "On the Scent", "My Friend Timmy", "Nest of Vipers", "Baptism of Fire",
            "Questions and Answers", "All that Glisters", "If You Can't Beat 'em", "Poverty, Chastity and Obedience", "A Needle in a Haystack",
            "The Die is Cast", "Payback", "Out of the Frying Pan", "Night Raid", "Siege", "Rocketeer", "Cold Steel, Hot Blood", "Family Values",
            "An Oath is an Oath", "Vengeance", "Epilogue" };
        Assert.True(titles.SetEquals(expected), "missing: " + string.Join(", ", expected.Except(titles)) + " extra: " + string.Join(", ", titles.Except(expected)));
    }

    [Fact]
    public void Main_orders_never_go_backwards_and_start_at_one()
    {
        var o = StorySections.Main.Select(m => m.Order).ToArray();
        Assert.Equal(1, o[0]);
        for (int i = 1; i < o.Length; i++) Assert.True(o[i] >= o[i - 1], $"{StorySections.Main[i].Code}");
    }

    [Fact]
    public void Every_locked_quest_belongs_to_a_period_named_by_one_of_its_own_members()
    {
        foreach (var s in StorySections.Locked)
        {
            var p = StorySections.PeriodOf(s.Code);
            Assert.NotNull(p);
            Assert.Contains(p!.Id, p.Codes);
            Assert.False(string.IsNullOrWhiteSpace(p.Why), s.Code);
        }
        Assert.True(StorySections.Periods.Count > 40);
    }

    [Fact]
    public void An_open_side_quest_has_no_section_and_an_open_main_quest_does()
    {
        Assert.Null(StorySections.ByCode("q_debtor"));         // Scavenger: open
        Assert.Null(StorySections.ByCode("test_petra3"));      // a developer test
        Assert.Null(StorySections.ByCode("q_tutorials"));      // a system graph
        var m = StorySections.ByCode("q_samopesh");            // My Friend Timmy: open, but the story is "at" it
        Assert.NotNull(m);
        Assert.False(m!.Locked);
        Assert.True(m.IsMain);
    }

    [Fact]
    public void The_two_flashbacks_chains_share_one_question_each()
    {
        var theresa = StorySections.PeriodOf("q_theresa_nails")!;
        Assert.Equal("q_theresa", theresa.Id);
        Assert.Contains("q_theresa_escape", theresa.Codes);
        Assert.Contains("q_theresa_return", theresa.Codes);
        Assert.Same(theresa, StorySections.PeriodOf("q_theresa_village"));
        Assert.All(theresa.Codes, c => Assert.Equal(StoryTier.Rails, StorySections.ByCode(c)!.Tier));
        var bob = StorySections.PeriodOf("q_rides_battle")!;
        Assert.Equal("q_rides", bob.Id);
        Assert.Same(bob, StorySections.PeriodOf("q_rides_feast"));
    }

    [Fact]
    public void A_dlc_chain_marked_main_in_the_data_is_not_part_of_the_story_order()
    {
        var t = StorySections.ByCode("q_theresa_nails")!;
        Assert.False(t.IsMain);
        Assert.Equal(StoryTier.Rails, t.Tier);
        Assert.Equal("A Woman's Lot", t.Dlc);
    }
}

public class StoryLockTests
{
    private static long Sec(double s) => (long)(s * 1000);

    [Fact]
    public void A_main_rails_quest_is_entered_by_its_first_change()
    {
        var l = new StoryLock();
        var t = l.Note("q_escapeToTalmberk", "o1", false, 0);
        Assert.Single(t);
        Assert.Equal(StoryLock.Kind.Enter, t[0].Kind);
        Assert.Equal("q_escapeToTalmberk", l.Active!.Code);
    }

    [Fact]
    public void A_mixed_quest_needs_a_burst_of_three_distinct_objectives()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("q_skalitz", "o1", false, Sec(0)));
        Assert.Empty(l.Note("q_skalitz", "o1", false, Sec(1)));   // the same one again is not a second
        Assert.Empty(l.Note("q_skalitz", "o2", false, Sec(2)));
        var t = l.Note("q_skalitz", "o3", false, Sec(3));
        Assert.Equal(StoryLock.Kind.Enter, Assert.Single(t).Kind);
    }

    [Fact]
    public void A_slow_trickle_is_not_a_burst()
    {
        var l = new StoryLock();
        l.Note("q_skalitz", "o1", false, Sec(0));
        l.Note("q_skalitz", "o2", false, Sec(50));
        Assert.Empty(l.Note("q_skalitz", "o3", false, Sec(100)));   // the first has fallen out of the 90 s window
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_later_main_quest_ends_the_earlier_section_and_the_earlier_one_cannot_come_back()
    {
        var l = new StoryLock();
        for (int i = 0; i < 3; i++) l.Note("q_skalitz", "o" + i, false, Sec(i));
        Assert.Equal("q_skalitz", l.Active!.Code);
        var t = l.Note("q_escapeToTalmberk", "o1", false, Sec(10));
        Assert.Equal(2, t.Count);
        Assert.Equal(StoryLock.Kind.Leave, t[0].Kind);
        Assert.Equal("moved on", t[0].Reason);
        Assert.Equal(StoryLock.Kind.Enter, t[1].Kind);
        // the earlier quest's background changes keep arriving: nothing
        for (int i = 10; i < 20; i++) Assert.Empty(l.Note("q_skalitz", "o" + i, false, Sec(20 + i)));
        Assert.Equal("q_escapeToTalmberk", l.Active!.Code);
    }

    [Fact]
    public void A_completed_quest_leaves_and_does_not_re_enter()
    {
        var l = new StoryLock();
        l.Note("q_pribBattle", "o1", false, 0);
        var t = l.Note("q_pribBattle", "end", true, Sec(60));
        Assert.Equal("completed", Assert.Single(t).Reason);
        Assert.Null(l.Active);
        Assert.Empty(l.Note("q_pribBattle", "o9", false, Sec(70)));   // cleanup after the end
    }

    [Fact]
    public void A_section_the_host_does_nothing_in_is_given_up_after_its_idle_time()
    {
        var l = new StoryLock();
        l.Note("q_pribBattle", "o1", false, 0);                       // a main rails quest: 40 minutes
        Assert.Null(l.Tick(Sec(39 * 60)));
        var t = l.Tick(Sec(41 * 60));
        Assert.Equal("idle", t!.Value.Reason);
        Assert.Null(l.Active);
    }

    [Fact]
    public void New_objectives_keep_a_section_alive_but_the_same_one_again_does_not()
    {
        var l = new StoryLock();
        l.Note("q_pribBattle", "o1", false, 0);
        for (int i = 1; i <= 38; i++) l.Note("q_pribBattle", "o" + (i % 3 + 100), false, Sec(i * 60));   // three states cycling
        Assert.NotNull(l.Tick(Sec(44 * 60)));    // the last NEW state was at minute 3: forty minutes later it is over
        var l2 = new StoryLock();
        l2.Note("q_pribBattle", "o1", false, 0);
        for (int i = 1; i <= 38; i++) l2.Note("q_pribBattle", "new" + i, false, Sec(i * 60));        // really progressing
        Assert.Null(l2.Tick(Sec(39 * 60)));
    }

    [Fact]
    public void A_side_quest_on_rails_needs_two_objectives_and_is_not_entered_while_the_world_is_locked()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("q_visitInBaths", "o1", false, 0));
        Assert.Equal(StoryLock.Kind.Enter, Assert.Single(l.Note("q_visitInBaths", "o2", false, Sec(1))).Kind);

        var l2 = new StoryLock();
        l2.Note("q_conquest", "o1", false, 0);                          // the castle assault: a main rails section
        Assert.Empty(l2.Note("q_visitInBaths", "o1", false, Sec(40)));
        Assert.Empty(l2.Note("q_visitInBaths", "o2", false, Sec(41)));
        Assert.Equal("q_conquest", l2.Active!.Code);
    }

    [Fact]
    public void A_side_quest_that_just_ended_is_not_re_entered_by_its_own_cleanup()
    {
        var l = new StoryLock();
        l.Note("q_visitInBaths", "o1", false, 0);
        l.Note("q_visitInBaths", "o2", false, Sec(1));
        l.Note("q_visitInBaths", "end", true, Sec(60));
        Assert.Null(l.Active);
        l.Note("q_visitInBaths", "c1", false, Sec(80));
        Assert.Empty(l.Note("q_visitInBaths", "c2", false, Sec(85)));
        Assert.Null(l.Active);
        l.Note("q_visitInBaths", "d1", false, Sec(200));                // after the cooldown it is a new bout
        Assert.NotEmpty(l.Note("q_visitInBaths", "d2", false, Sec(201)));
    }

    [Fact]
    public void An_open_quest_never_enters_anything()
    {
        var l = new StoryLock();
        for (int i = 0; i < 10; i++) Assert.Empty(l.Note("q_debtor", "o" + i, false, Sec(i)));
        Assert.Null(l.Active);
    }

    [Fact]
    public void The_flashback_chain_is_entered_by_two_objectives_and_its_next_quest_stays_in_the_period()
    {
        var l = new StoryLock();
        l.Note("q_theresa_nails", "o1", false, 0);
        var enter = l.Note("q_theresa_nails", "o2", false, Sec(1));
        Assert.Equal("q_theresa", StorySections.PeriodOf(enter[0].Section!.Code)!.Id);
        l.Note("q_theresa_nails", "end", true, Sec(600));
        l.Note("q_theresa_escape", "o1", false, Sec(601));
        var next = l.Note("q_theresa_escape", "o2", false, Sec(602));
        Assert.Equal(StoryLock.Kind.Enter, next[0].Kind);
        Assert.Same(StorySections.PeriodOf("q_theresa_nails"), StorySections.PeriodOf("q_theresa_escape"));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var l = new StoryLock();
        l.Note("q_pribBattle", "o1", false, 0);
        l.Note("q_pribBattle", "end", true, Sec(1));
        l.Reset();
        Assert.Equal(StoryLock.Kind.Enter, Assert.Single(l.Note("q_pribBattle", "o1", false, Sec(2))).Kind);
    }

    [Fact]
    public void The_tether_is_the_tighter_of_the_hosts_distance_and_the_rails_distance()
    {
        Assert.Equal((650f, 700f), StoryLock.Tether(650f, 700f, false, 120f));
        var (warn, pull) = StoryLock.Tether(650f, 700f, true, 120f);
        Assert.Equal(120f, pull);
        Assert.Equal(90f, warn);
    }

    [Fact]
    public void Every_gated_quest_of_the_game_can_be_entered_by_enough_changes_and_only_by_them()
    {
        // The whole plan against the state machine: feed each locked quest enough distinct objectives and it must enter its own section.
        foreach (var s in StorySections.Locked)
        {
            var l = new StoryLock();
            StoryLock.Transition? entered = null;
            for (int i = 0; i < 4 && entered is null; i++)
                foreach (var t in l.Note(s.Code, "x" + i, false, Sec(i)))
                    if (t.Kind == StoryLock.Kind.Enter) entered = t;
            Assert.True(entered is not null, $"{s.Code} never entered");
            Assert.Equal(s.Code, entered!.Value.Section!.Code);
        }
    }
}

public class RailsTests
{
    private static long Sec(double s) => (long)(s * 1000);

    [Fact]
    public void A_new_period_asks_and_the_same_period_does_not_ask_again()
    {
        var j = new RailsJoiner();
        var step = j.OnEnter("q_escapeToTalmberk", 0, RailsPref.Ask);
        Assert.Equal(RailsJoiner.Act.Ask, step.Act);
        Assert.Equal("q_skalitz", step.Period!.Id);
        Assert.True(j.Asking);
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("q_returnToSkalitz", Sec(5), RailsPref.Ask).Act);   // same period: no second question
    }

    [Fact]
    public void A_standing_answer_decides_without_asking()
    {
        var j = new RailsJoiner();
        var s = j.OnEnter("q_pribBattle", 0, RailsPref.Free);
        Assert.Equal(RailsJoiner.Act.Auto, s.Act);
        Assert.Equal(RailsChoice.Free, s.Choice);
        Assert.True(j.IsFree);
        var j2 = new RailsJoiner();
        Assert.Equal(RailsChoice.Join, j2.OnEnter("q_pribBattle", 0, RailsPref.Join).Choice);
    }

    [Fact]
    public void No_answer_counts_as_joining_after_the_backstop()
    {
        var j = new RailsJoiner();
        j.OnEnter("q_pribBattle", 0, RailsPref.Ask);
        Assert.Equal(RailsJoiner.Act.None, j.Tick(Sec(30)).Act);
        var s = j.Tick(Sec(46));
        Assert.Equal(RailsJoiner.Act.Backstop, s.Act);
        Assert.Equal(RailsChoice.Join, s.Choice);
    }

    [Fact]
    public void An_answer_is_repeated_while_the_period_lasts_and_the_period_ends_after_its_grace()
    {
        var j = new RailsJoiner();
        j.OnEnter("q_pribBattle", 0, RailsPref.Ask);
        Assert.True(j.Decide(RailsChoice.Free, Sec(3)));
        Assert.Equal(RailsJoiner.Act.None, j.Tick(Sec(10)).Act);
        Assert.Equal(RailsJoiner.Act.Resend, j.Tick(Sec(24)).Act);
        j.OnLeave("q_pribBattle", Sec(60));
        Assert.NotEqual(RailsJoiner.Act.Over, j.Tick(Sec(62)).Act);   // inside the grace: still the period (the answer may be repeated)
        var over = j.Tick(Sec(69));
        Assert.Equal(RailsJoiner.Act.Over, over.Act);
        Assert.Equal(RailsChoice.Free, over.Choice);
        Assert.False(j.Open);
    }

    [Fact]
    public void The_next_section_of_the_same_period_within_the_grace_keeps_the_answer()
    {
        var j = new RailsJoiner();
        j.OnEnter("q_pribyslav", 0, RailsPref.Ask);
        j.Decide(RailsChoice.Free, Sec(2));
        j.OnLeave("q_pribyslav", Sec(100));
        j.OnEnter("q_pribBattle", Sec(103), RailsPref.Ask);      // the battle follows the scouting
        Assert.Equal(RailsChoice.Free, j.Choice);
        Assert.NotEqual(RailsJoiner.Act.Over, j.Tick(Sec(115)).Act);
        Assert.True(j.Open);
    }

    [Fact]
    public void The_host_exempts_a_friend_until_they_answer_or_their_time_runs_out()
    {
        var r = new RailsRoster();
        Assert.True(r.Start("q_pribyslav"));
        Assert.False(r.Start("q_pribyslav"));
        var friends = new byte[] { 2, 3 };
        r.Tick(0, friends);
        Assert.True(r.Exempt(2) && r.Exempt(3));                 // asked, not answered: nothing may move them
        Assert.Equal(RailsRoster.Outcome.Joined, r.Choose(2, "q_pribyslav", RailsChoice.Join, Sec(5)));
        Assert.Equal(RailsRoster.Outcome.Freed, r.Choose(3, "q_pribyslav", RailsChoice.Free, Sec(6)));
        Assert.Equal(RailsRoster.Outcome.Unchanged, r.Choose(3, "q_pribyslav", RailsChoice.Free, Sec(7)));
        Assert.False(r.Exempt(2));
        Assert.True(r.Exempt(3));
        Assert.Equal(RailsRoster.Outcome.Ignored, r.Choose(2, "q_other", RailsChoice.Join, Sec(8)));
    }

    [Fact]
    public void A_friend_who_never_answers_is_taken_as_joined_after_the_hosts_grace()
    {
        var r = new RailsRoster();
        r.Start("q_pribyslav");
        r.Tick(0, new byte[] { 2 });
        Assert.Empty(r.Tick(Sec(60), new byte[] { 2 }));
        Assert.Equal(new byte[] { 2 }, r.Tick(Sec(76), new byte[] { 2 }));
        Assert.False(r.Exempt(2));
    }

    [Fact]
    public void Outside_a_period_nobody_is_exempt_and_a_late_answer_is_not_news()
    {
        var r = new RailsRoster();
        Assert.False(r.Exempt(5));
        Assert.Equal(RailsRoster.Outcome.NoPeriod, r.Choose(5, "q_x", RailsChoice.Free, 0));
    }

    [Fact]
    public void The_host_lets_the_period_go_after_the_same_grace_the_friend_uses()
    {
        var r = new RailsRoster();
        r.Start("q_pribyslav");
        r.EndSoon(Sec(100));
        r.Tick(Sec(105), new byte[] { 2 });
        Assert.Equal("q_pribyslav", r.Period);
        r.Tick(Sec(109), new byte[] { 2 });
        Assert.Null(r.Period);
    }

    [Fact]
    public void An_answer_has_a_wire_form_both_sides_agree_on()
    {
        Assert.Equal("q_pribBattle free", RailsRules.ChoiceText("q_pribBattle", RailsChoice.Free));
        Assert.True(RailsRules.TryParseChoice("q_pribyslav join", out var p, out var c));
        Assert.Equal("q_pribyslav", p.Id);
        Assert.Equal(RailsChoice.Join, c);
        Assert.False(RailsRules.TryParseChoice("q_debtor join", out _, out _));          // an open quest has no period
        Assert.False(RailsRules.TryParseChoice("q_pribyslav maybe", out _, out _));
        Assert.Equal(RailsPref.Free, RailsRules.ParsePref("stay"));
    }
}
