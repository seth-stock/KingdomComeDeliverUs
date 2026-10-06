// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using KcdUs.Agent.Ui;
using Xunit;

namespace KcdUs.Tests;

/// <summary>
/// The Multiplayer tab's builder (docs/MENU.md). (synthetic) -- the game's graphs are replaced by a few lines shaped like the real ones;
/// the real thing was checked in the retail game (the tab shows, the page opens, a button reaches Lua, MP_Load loads a save).
/// </summary>
public class MenuUiTests
{
    // the real graph's shape: ... -> Settings(709) -> FAQ(710) -> DLCs(968) -> ...
    private const string Main = @"<?xml version=""1.0"" encoding=""us-ascii""?>
<Graph Group=""MM_PagesMain"" MultiPlayer=""ServerOnly"">
  <Nodes>
    <Node Id=""709"" Class=""UI:Functions:MainMenu:AddButton"" pos=""-600,700,0"" flags=""0"">
      <Inputs instanceID=""-1"" id=""MM_Settings"" containerIndex=""0"" uiText=""@ui_Settings"" actionType="""" tooltip="""" disable=""0"" sound="""" />
    </Node>
    <Node Id=""710"" Class=""UI:Functions:MainMenu:AddButton"" pos=""-600,780,0"" flags=""0"">
      <Inputs instanceID=""-1"" id=""MM_FAQ"" containerIndex=""0"" uiText=""@ui_FAQ"" actionType="""" tooltip="""" disable=""0"" sound="""" />
    </Node>
    <Node Id=""968"" Class=""UI:Functions:MainMenu:AddButton"" pos=""-600,940,0"" flags=""0"">
      <Inputs instanceID=""-1"" id=""MM_DLCS"" containerIndex=""0"" uiText=""@ui_DLCs"" actionType="""" tooltip="""" disable=""0"" sound="""" />
    </Node>
    <Node Id=""971"" Class=""Logic:Any"" pos=""0,0,0"" flags=""0"">
      <Inputs />
    </Node>
  </Nodes>
  <Edges>
    <Edge nodeIn=""710"" nodeOut=""709"" portIn=""Call"" portOut=""OnCall"" enabled=""1"" />
    <Edge nodeIn=""968"" nodeOut=""710"" portIn=""Call"" portOut=""OnCall"" enabled=""1"" />
    <Edge nodeIn=""893"" nodeOut=""971"" portIn=""Get"" portOut=""out"" enabled=""1"" />
  </Edges>
</Graph>
";

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    [Fact]
    public void The_button_goes_between_the_faq_button_and_the_one_after_it()
    {
        var doc = Parse(MenuUi.PatchMenu(Main));
        var nodes = doc.Descendants("Node").ToList();
        var mp = nodes.Single(n => n.Element("Inputs")?.Attribute("id")?.Value == "MM_Multiplayer");
        Assert.Equal("972", (string)mp.Attribute("Id")!);                          // the next free id
        Assert.Equal("Multiplayer", (string)mp.Element("Inputs")!.Attribute("uiText")!);
        var edges = doc.Descendants("Edge").Select(e => ((string)e.Attribute("nodeOut")!, (string)e.Attribute("nodeIn")!, (string)e.Attribute("portOut")!)).ToList();
        Assert.Contains(("710", "972", "OnCall"), edges);   // FAQ hands over to Multiplayer
        Assert.Contains(("972", "968", "OnCall"), edges);   // Multiplayer hands over to DLCs
        Assert.DoesNotContain(("710", "968", "OnCall"), edges);   // and the direct hand-over is gone
        Assert.Equal(3 + 1, edges.Count);                    // the other edges are untouched, two replace one
    }

    [Fact]
    public void A_graph_that_is_not_shaped_as_expected_is_refused_not_guessed_at()
    {
        Assert.Throws<InvalidDataException>(() => MenuUi.PatchMenu(Main.Replace("MM_FAQ", "MM_Other")));          // no anchor button
        Assert.Throws<InvalidDataException>(() => MenuUi.PatchMenu(Main.Replace("<Edge nodeIn=\"968\" nodeOut=\"710\" portIn=\"Call\" portOut=\"OnCall\" enabled=\"1\" />", "")));   // the anchor hands nothing over
        Assert.Throws<InvalidDataException>(() => MenuUi.PatchMenu(MenuUi.PatchMenu(Main)));                     // never patched twice
    }

    [Fact]
    public void Every_page_is_well_formed_and_every_edge_names_nodes_that_exist()
    {
        foreach (var (name, xml) in MenuUi.Pages())
        {
            var doc = Parse(xml);
            var ids = doc.Descendants("Node").Select(n => (string)n.Attribute("Id")!).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());                       // unique ids
            foreach (var e in doc.Descendants("Edge"))
            {
                Assert.Contains((string)e.Attribute("nodeIn")!, ids);
                Assert.Contains((string)e.Attribute("nodeOut")!, ids);
            }
            Assert.True(name.StartsWith(MenuUi.ActionsDir) && name.EndsWith(".xml"), name);
            Assert.True(Encoding.ASCII.GetByteCount(xml) == xml.Length, name + " is not pure ASCII");   // the game's graphs are us-ascii
        }
    }

    [Fact]
    public void Each_button_press_reaches_the_mod_with_its_verb_and_the_page_buttons_open_pages_that_exist()
    {
        var pages = MenuUi.Pages();
        var scripts = pages.Values.SelectMany(x => Parse(x).Descendants("Node"))
            .Where(n => (string)n.Attribute("Class")! == "System:ExecuteScript").Select(n => (string)n.Element("Inputs")!.Attribute("Script")!).ToList();
        foreach (var verb in new[] { "page", "status", "host", "join", "leave", "settings" })
            Assert.Contains($"KCDUS_Menu('{verb}','')", scripts);
        foreach (var arg in new[] { "ask", "join", "free" }) Assert.Contains($"KCDUS_Menu('pref','{arg}')", scripts);
        foreach (var arg in new[] { "f11f12", "f9f10", "off" }) Assert.Contains($"KCDUS_Menu('keys','{arg}')", scripts);
        foreach (var arg in new[] { "play", "join", "new", "save" }) Assert.Contains($"KCDUS_Menu('world','{arg}')", scripts);
        foreach (var arg in new[] { "host", "mine", "home" }) Assert.Contains($"KCDUS_Menu('henry','{arg}')", scripts);
        foreach (var arg in new[] { "furthest", "host", "newest" }) Assert.Contains($"KCDUS_Menu('resolve','{arg}')", scripts);

        var opened = pages.Values.SelectMany(x => Parse(x).Descendants("Node")).Where(n => (string)n.Attribute("Class")! == "UI:Action:Control")
            .Select(n => (string)n.Element("Inputs")!.Attribute("uiActions_UIAction")!).ToList();
        Assert.Contains(MenuUi.PageStory, opened);
        Assert.Contains(MenuUi.PageKeys, opened);
        Assert.Contains(MenuUi.PageWorld, opened);
        Assert.Contains(MenuUi.PageHenry, opened);
        Assert.Contains(MenuUi.PageResolve, opened);
        foreach (var o in opened) Assert.True(pages.ContainsKey(MenuUi.ActionsDir + o + ".xml"), o);
    }

    [Fact]
    public void The_button_id_is_the_name_of_its_page_because_the_game_starts_the_action_of_that_name()
    {
        Assert.True(MenuUi.Pages().ContainsKey(MenuUi.ActionsDir + "MM_Multiplayer.xml"));   // the main-menu button's id (PatchMenu) names this page
        Assert.Contains("id=\"MM_Multiplayer\"", MenuUi.PatchMenu(Main));
    }

    [Fact]
    public void The_load_graph_reads_the_two_globals_and_hides_the_menus_when_the_game_has_loaded()
    {
        var doc = Parse(MenuUi.Pages()[MenuUi.GraphLoad]);
        var cls = doc.Descendants("Node").Select(n => (string)n.Attribute("Class")!).ToList();
        Assert.Contains("UI:Functions:SaveLoad:LoadSavedGame", cls);
        Assert.Contains("UI:Events:SaveLoad:OnGameLoaded", cls);
        Assert.Contains("UI:Functions:MenuEvents:DisplayMainMenu", cls);
        Assert.Contains("UI:Functions:MenuEvents:DisplayIngameMenu", cls);
        var names = doc.Descendants("Node").Where(n => (string)n.Attribute("Class")! == "Variables:GlobalVariable").Select(n => (string)n.Element("Inputs")!.Attribute("Name")!).ToList();
        Assert.Equal(new[] { "KCDUS_LoadSaveId", "KCDUS_LoadPlayLine" }, names);   // what menu.lua sets
    }

    [Fact]
    public void The_pak_is_built_from_a_game_pak_and_carries_the_patched_graph_and_every_page()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kcdus-ui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string game = Path.Combine(dir, "GameData.pak");
            using (var z = ZipFile.Open(game, ZipArchiveMode.Create))
            {
                foreach (var (n, t) in new[] { (MenuUi.MainMenu, Main), (MenuUi.IngameMenu, Main), ("Libs/Other/untouched.xml", "<x/>") })
                {
                    using var w = new StreamWriter(z.CreateEntry(n).Open(), Encoding.ASCII); w.Write(t);
                }
            }
            string outPak = Path.Combine(dir, "Mods", "kcdus", "Data", MenuUi.PakName);
            var r = MenuUi.Build(game, outPak);
            Assert.True(r.Ok, r.Message);
            using var zin = ZipFile.OpenRead(outPak);
            var names = zin.Entries.Select(e => e.FullName).ToHashSet();
            Assert.Contains(MenuUi.MainMenu, names);
            Assert.Contains(MenuUi.IngameMenu, names);
            Assert.Contains(MenuUi.ActionsDir + "MM_Multiplayer.xml", names);
            Assert.DoesNotContain("Libs/Other/untouched.xml", names);               // only what we change
            using var rd = new StreamReader(zin.GetEntry(MenuUi.MainMenu)!.Open());
            Assert.Contains("MM_Multiplayer", rd.ReadToEnd());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_missing_pak_or_a_changed_game_is_an_answer_not_an_exception()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kcdus-ui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var r = MenuUi.Build(Path.Combine(dir, "nope.pak"), Path.Combine(dir, "out.pak"));
            Assert.False(r.Ok);
            Assert.Contains("no Multiplayer tab", r.Message);
            string game = Path.Combine(dir, "GameData.pak");
            using (var z = ZipFile.Open(game, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry(MenuUi.MainMenu).Open())) w.Write("<Graph><Nodes/><Edges/></Graph>");
            var r2 = MenuUi.Build(game, Path.Combine(dir, "out.pak"));
            Assert.False(r2.Ok);
            Assert.False(File.Exists(Path.Combine(dir, "out.pak")));               // nothing half-written
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Every_title_code_has_a_graph_with_its_text_written_in()
    {
        // a flow-graph variable holds only a number, so the agent sends a code and each code has a graph of its own (menu.lua MENUTEXT starts MP_Title<code>)
        var pages = MenuUi.Pages();
        foreach (var t in Enum.GetValues<MenuUi.Title>())
        {
            Assert.True(pages.ContainsKey(MenuUi.ActionsDir + "MP_Title" + (int)t + ".xml"), t.ToString());
            var head = Parse(pages[MenuUi.TitleGraphName(t)]).Descendants("Node").Single(n => (string)n.Attribute("Class")! == "UI:Functions:MainMenu:SetTitleBox");
            Assert.Equal(MenuUi.TitleText(t), (string)head.Element("Inputs")!.Attribute("header")!);
            Assert.NotEqual("Multiplayer", MenuUi.TitleText(t));
        }
    }

    [Fact]
    public void The_pause_menu_may_be_missing_without_losing_the_main_menu_tab()
    {
        var files = MenuUi.BuildFiles(n => n == MenuUi.MainMenu ? Main : null);
        Assert.Contains(MenuUi.MainMenu, files.Keys);
        Assert.DoesNotContain(MenuUi.IngameMenu, files.Keys);
    }
}
