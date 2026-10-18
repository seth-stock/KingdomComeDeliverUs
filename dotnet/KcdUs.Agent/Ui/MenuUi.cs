// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdUs.Agent.Ui;

/// <summary>
/// The "Multiplayer" tab (docs/MENU.md). KCD1's menus are flow graphs (Libs/UI/UIActions/*.xml in GameData.pak): a button is an
/// <c>UI:Functions:MainMenu:AddButton</c> node, and a pressed button starts the UI action named by its id. A mod pak that carries a
/// file of the same path overrides the game's, so the tab is one inserted node plus our own page graphs.
///
/// Nothing of Warhorse's is stored in this repository or shipped in the installer: <see cref="Build"/> reads the player's OWN
/// GameData.pak, inserts the one node into its copies of MM_Main.xml and MM_IngameMenu.xml, and writes the result to the mod's
/// kcdus-ui.pak on the player's machine. If the game has changed shape (an anchor is missing) it refuses and the mod simply has no tab.
/// </summary>
public static class MenuUi
{
    public const string PakName = "kcdus-ui.pak";
    public const string ActionsDir = "Libs/UI/UIActions/";
    public const string MainMenu = ActionsDir + "MM_Main.xml";
    public const string IngameMenu = ActionsDir + "MM_IngameMenu.xml";
    /// <summary>The existing button the new one goes after (and the node it hands over to).</summary>
    public const string AnchorButtonId = "MM_FAQ";

    public sealed record Result(bool Ok, string Message, IReadOnlyDictionary<string, string> Files);

    // ------------------------------------------------------------------ the patch of the game's own graphs

    /// <summary>Inserts the Multiplayer button after the FAQ button. Throws <see cref="InvalidDataException"/> when the graph is not shaped as expected.</summary>
    public static string PatchMenu(string xml)
    {
        string buttonRx = $"<Node Id=\"(\\d+)\" Class=\"UI:Functions:MainMenu:AddButton\"[^>]*>\\s*<Inputs [^>]*\\bid=\"{AnchorButtonId}\"[^>]*/>\\s*</Node>\\s*";
        var m = Regex.Match(xml, buttonRx);
        if (!m.Success) throw new InvalidDataException($"no {AnchorButtonId} button in this menu graph");
        string anchorId = m.Groups[1].Value;
        if (xml.Contains("id=\"MM_Multiplayer\"", StringComparison.Ordinal)) throw new InvalidDataException("this menu graph already has a Multiplayer button");

        var edge = Regex.Match(xml, $"<Edge nodeIn=\"(\\d+)\" nodeOut=\"{anchorId}\" portIn=\"Call\" portOut=\"OnCall\" enabled=\"1\" />");
        if (!edge.Success) throw new InvalidDataException($"the {AnchorButtonId} button does not hand over to another button");
        string nextId = edge.Groups[1].Value;

        int maxId = Regex.Matches(xml, "<Node Id=\"(\\d+)\"").Select(x => int.Parse(x.Groups[1].Value)).DefaultIfEmpty(0).Max();
        string newId = (maxId + 1).ToString();
        string node =
            $"    <Node Id=\"{newId}\" Class=\"UI:Functions:MainMenu:AddButton\" pos=\"-600,860,0\" flags=\"0\">\n" +
            "      <Inputs instanceID=\"-1\" id=\"MM_Multiplayer\" containerIndex=\"0\" uiText=\"Multiplayer\" actionType=\"\" tooltip=\"\" disable=\"0\" sound=\"\" />\n" +
            "    </Node>\n";
        // the node goes right after the anchor's node; the anchor now hands over to it, and it hands over to what followed
        xml = xml.Insert(m.Index + m.Length, node);
        string oldEdge = edge.Value;
        string newEdges =
            $"<Edge nodeIn=\"{newId}\" nodeOut=\"{anchorId}\" portIn=\"Call\" portOut=\"OnCall\" enabled=\"1\" />\n" +
            $"    <Edge nodeIn=\"{nextId}\" nodeOut=\"{newId}\" portIn=\"Call\" portOut=\"OnCall\" enabled=\"1\" />";
        int at = xml.IndexOf(oldEdge, StringComparison.Ordinal);
        return xml.Remove(at, oldEdge.Length).Insert(at, newEdges);
    }

    // ------------------------------------------------------------------ the pages

    private sealed class Graph
    {
        private readonly StringBuilder _nodes = new(), _edges = new();
        private int _next = 1;
        public int Add(string cls, params (string K, string V)[] inputs)
        {
            int id = _next++;
            _nodes.Append($"    <Node Id=\"{id}\" Class=\"{cls}\" pos=\"{id * 40},0,0\" flags=\"0\">\n      <Inputs");
            foreach (var (k, v) in inputs) _nodes.Append($" {k}=\"{Esc(v)}\"");
            _nodes.Append(" />\n    </Node>\n");
            return id;
        }
        public void Wire(int from, string outPort, int to, string inPort) =>
            _edges.Append($"    <Edge nodeIn=\"{to}\" nodeOut=\"{from}\" portIn=\"{inPort}\" portOut=\"{outPort}\" enabled=\"1\" />\n");
        public override string ToString() =>
            "<?xml version=\"1.0\" encoding=\"us-ascii\"?>\n<Graph Group=\"MM_PagesMain\" MultiPlayer=\"ServerOnly\">\n  <Nodes>\n" + _nodes +
            "  </Nodes>\n  <Edges>\n" + _edges + "  </Edges>\n</Graph>\n";
    }

    public static string BackId(string title) => "MP_Back_" + new string(title.Where(char.IsLetterOrDigit).ToArray());

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;");

    /// <summary>A button on a page: what it says and what it does.</summary>
    /// <param name="Script">Lua run when it is pressed (null: it opens <paramref name="Opens"/> instead).</param>
    /// <param name="Opens">The UI action (page) it opens.</param>
    public sealed record Button(string Id, string Label, string? Script = null, string? Opens = null, string Tooltip = "");

    /// <summary>One page of buttons with a Back button. Pressing an action button runs its Lua; a page button opens that page and the page is rebuilt when it closes.</summary>
    public static string Page(string title, IReadOnlyList<Button> buttons, string? onOpen = null)
        => PageCore(title, buttons, onOpen ?? "KCDUS_PageOpened('sub')");

    private static string PageCore(string title, IReadOnlyList<Button> buttons, string onOpen)
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "1"));
        int clear = g.Add("UI:Functions:MainMenu:ClearAll", ("instanceID", "-1"), ("useLastSelect", "0"));
        int container = g.Add("UI:Functions:MainMenu:SetContainer", ("instanceID", "-1"), ("containerIndex", "0"), ("ButtonXPos", "1500"), ("ButtonYPos", "400"), ("MaxButtons", "10"));
        int head = g.Add("UI:Functions:MainMenu:SetTitleBox", ("instanceID", "-1"), ("header", title));
        g.Wire(start, "StartAction", clear, "Call");
        g.Wire(clear, "OnCall", container, "Call");
        g.Wire(clear, "OnCall", head, "Call");
        {
            // the page says it opened (the main page tells the agent, which answers with the status line for the title)
            int opened = g.Add("System:ExecuteScript", ("Script", onOpen));
            g.Wire(clear, "OnCall", opened, "Call");
        }
        int prev = container;
        foreach (var b in buttons)
        {
            int add = g.Add("UI:Functions:MainMenu:AddButton", ("instanceID", "-1"), ("id", b.Id), ("containerIndex", "0"), ("uiText", b.Label), ("actionType", ""), ("tooltip", b.Tooltip), ("disable", "0"), ("sound", ""));
            g.Wire(prev, "OnCall", add, "Call");
            prev = add;
            int ev = g.Add("UI:Events:MainMenu:OnButton", ("instanceID", "-1"), ("Port", "0"), ("Idx", b.Id));
            if (b.Opens is not null)
            {
                int open = g.Add("UI:Action:Control", ("uiActions_UIAction", b.Opens), ("Strict", "0"), ("Args", ""));
                g.Wire(ev, "onEvent", open, "Start");
                g.Wire(open, "OnEnd", clear, "Call");   // the sub-page closed: build this page again
            }
            else
            {
                int run = g.Add("System:ExecuteScript", ("Script", b.Script ?? ""));
                g.Wire(ev, "onEvent", run, "Call");
            }
        }
        // each page's Back has an id of its own: with one shared id every open page heard the press, so Back on a sub-page closed the whole tab (seen)
        string backId = BackId(title);
        int back = g.Add("UI:Functions:MainMenu:AddButton", ("instanceID", "-1"), ("id", backId), ("containerIndex", "1"), ("uiText", "@ui_back"), ("actionType", "back"), ("tooltip", ""), ("disable", "0"), ("sound", ""));
        g.Wire(prev, "OnCall", back, "Call");
        int backEv = g.Add("UI:Events:MainMenu:OnButton", ("instanceID", "-1"), ("Port", "0"), ("Idx", backId));
        int end = g.Add("UI:Action:End", ("UseAsState", "1"), ("Args", ""));
        g.Wire(backEv, "onEvent", end, "EndAction");
        int closed = g.Add("System:ExecuteScript", ("Script", "KCDUS_PageClosed()"));
        g.Wire(backEv, "onEvent", closed, "Call");
        return g.ToString();
    }

    public const string PageMain = "MM_Multiplayer";
    public const string PageKeys = "MP_Keys";
    public const string PagePause = "MP_Pause";
    public const string PageShared = "MP_Shared";
    public const string PageStory = "MP_Story";
    public const string PageWorld = "MP_World";
    public const string PageHenry = "MP_Henry";
    public const string PageResolve = "MP_Resolve";
    public const string PageMore = "MP_More";
    /// <summary>The graph that puts any text into the title box: Lua starts it with the text as its one argument (the Start node's Args port, as the game's help screen does).</summary>
    public const string GraphText = ActionsDir + "MP_Text.xml";
    /// <summary>A box with the text it is started with and OK (the game's own confirmation box, AddConfirmation, as its Quit question).</summary>
    public const string GraphMsg = ActionsDir + "MP_Msg.xml";
    /// <summary>The box shows about six lines (~250 characters, seen in the game); a longer text is cut.</summary>
    public const int BoxMax = 250;

    /// <summary>A line for MP_Text: what the console and the record format cannot carry is replaced, and it is kept to what the title box shows.</summary>
    public static string TitleLineText(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(c is '|' or '~' or '"' or '\\' or '^' or ';' or < ' ' ? ' ' : c);
        string t = sb.ToString().Trim();
        return t.Length <= TitleMax ? t : t[..(TitleMax - 3)].TrimEnd() + "...";
    }
    /// <summary>The title box shows one line of about 40 characters (seen in the game at 1280x720); a longer line is cut.</summary>
    public const int TitleMax = 44;
    /// <summary>The line under the page's heading. A flow-graph global holds only a number (a string comes back as 0), so the agent sends a code
    /// and each code has a graph of its own with the text written in: MP_Title0 ... (menu.lua's MENUTEXT starts <c>MP_Title&lt;code&gt;</c>).</summary>
    public enum Title { Idle = 0, Hosting = 1, Joining = 2, Joined = 3, Refused = 4, NeedAddress = 5, Settings = 6, StoryAsk = 7, StoryJoin = 8, StoryFree = 9, KeysDefault = 10, KeysAlt = 11, KeysOff = 12, PortBusy = 13,
        NoWorld = 14, WorldAsked = 15, WorldReceiving = 16, WorldLoading = 17, WorldSaved = 18, WorldNew = 19, HenryHost = 20, HenryMine = 21,
        ResolveFurthest = 22, ResolveHost = 23, ResolveNewest = 24, NeedHost = 25, HenryHome = 26, InSync = 27, WorldBusy = 28, WorldSent = 29 }

    public static string TitleText(Title t) => t switch
    {
        Title.Idle => "Not in a session",
        Title.Hosting => "Hosting a game",
        Title.Joining => "Connecting to the host...",
        Title.Joined => "Connected to the host",
        Title.Refused => "The host refused you",
        Title.NeedAddress => "First set the host's address (Settings)",
        Title.Settings => "Settings: opened in your browser",
        Title.StoryAsk => "Story: you are asked each time",
        Title.StoryJoin => "Story: you always join the host",
        Title.StoryFree => "Story: you always stay in the open",
        Title.KeysDefault => "Keys: join F11, stay F12",
        Title.KeysAlt => "Keys: join F9, stay F10",
        Title.KeysOff => "Keys: none (answer in the menu)",
        Title.PortBusy => "Port in use: change it in Settings",
        Title.NoWorld => "No shared game here yet",
        Title.WorldAsked => "Asking the host for the game...",
        Title.WorldReceiving => "Receiving the host's game...",
        Title.WorldLoading => "Loading the shared game...",
        Title.WorldSaved => "Saved",
        Title.WorldNew => "Host: start a New Game",
        Title.HenryHost => "Join: host's Henry, then your own",
        Title.HenryMine => "Join brings your own Henry",
        Title.ResolveFurthest => "The host's game is the one played",
        Title.ResolveHost => "The host's game is the one played",
        Title.ResolveNewest => "The host's game is the one played",
        Title.NeedHost => "Join a host first",
        Title.HenryHome => "Your Henry is going home",
        Title.InSync => "Your copy matches the host's",
        Title.WorldBusy => "Busy: wait a moment",
        Title.WorldSent => "Sending your game...",
        _ => "Multiplayer",
    };

    public static string TitleGraphName(Title t) => ActionsDir + "MP_Title" + (int)t + ".xml";
    public const string GraphLoad = ActionsDir + "MP_Load.xml";
    public const string GraphList = ActionsDir + "MP_ListSaves.xml";
    public const string GraphMenuWatch = ActionsDir + "MP_MenuWatch.xml";

    private static string Menu(string verb, string arg = "") => $"KCDUS_Menu('{verb}','{arg}')";

    /// <summary>The pages of the tab. Same list in every build, so the agent knows what each verb means (Session.OnMenu).</summary>
    public static IReadOnlyDictionary<string, string> Pages() => new Dictionary<string, string>
    {
        [ActionsDir + PageMain + ".xml"] = Page("Multiplayer", new Button[]
        {
            new("MP_Host", "Host: share my game", Menu("host"), Tooltip: "Load your game or start a New Game (before or after pressing this). Friends who press Join get your game and play in it with you"),
            new("MP_Join", "Join my friend's game", Menu("join"), Tooltip: "Connects to the host in your settings, gets the game they are playing and loads it. The first time you play a copy of their Henry; after that your own Henry from last time"),
            new("MP_JoinMine", "Join with my own Henry", Menu("joinmine"), Tooltip: "Load your own game first: the Henry in it (stats, skills, perks, inventory) goes into the host's game"),
            new("MP_Save", "Save now", Menu("save"), Tooltip: "Saves your game. The host's game, as it is when a friend presses Join, is what that friend gets"),
            new("MP_Leave", "Leave the session", Menu("leave"), Tooltip: "Disconnect. Your game goes on alone"),
            new("MP_Web", "Settings: host address, name", Menu("settings"), Tooltip: "Opens in your browser: the host's address (for Join), your name, password, ports"),
            new("MP_MorePage", "More options", Opens: PageMore, Tooltip: "How to play together, who is here, story, pausing, shared fights and loot, your Henry's way home"),
        }, onOpen: Menu("page")),
        [ActionsDir + PageMore + ".xml"] = Page("More options", new Button[]
        {
            new("MP_Help", "How to play together", Menu("help"), Tooltip: "The steps, for the host and the friend"),
            new("MP_Status", "Who is here", Menu("status"), Tooltip: "Who is connected, and what is shared"),
            new("MP_StoryPage", "Story: join or stay", Opens: PageStory, Tooltip: "Answer the host's story question, or give a standing answer"),
            new("MP_KeysPage", "Keys", Opens: PageKeys, Tooltip: "The keys that answer the host's story question"),
            new("MP_PausePage", "Pausing, fights and loot", Opens: PagePause, Tooltip: "Whether a pause menu pauses everyone, and whether enemies and items are shared with friends in the same game"),
            new("MP_WorldPlay", "Load my copy of the shared game", Menu("world", "play"), Tooltip: "Load your own copy of the last shared game, to play it alone"),
            new("MP_HenryHome", "Send my Henry home", Menu("henry", "home"), Tooltip: "Back into the game you played before joining, with what he gained here"),
        }),
        [ActionsDir + PageStory + ".xml"] = Page("Story: join or stay", new Button[]
        {
            new("MP_AnswerJoin", "Join my host now", Menu("answer", "join"), Tooltip: "Your host's story asked: you go to them and follow their quest"),
            new("MP_AnswerStay", "Stay in the open world", Menu("answer", "stay"), Tooltip: "Your host's story asked: you stay where you are (Join my host now joins later)"),
            new("MP_StoryAsk", "Always ask me", Menu("pref", "ask"), Tooltip: "Each time the host's story goes on rails you are asked"),
            new("MP_StoryJoin", "Always join the host", Menu("pref", "join")),
            new("MP_StoryFree", "Always stay in the open world", Menu("pref", "free")),
        }),
        [ActionsDir + PageKeys + ".xml"] = Page("Keys", new Button[]
        {
            new("MP_KeysDefault", "F11 join, F12 stay (default)", Menu("keys", "f11f12")),
            new("MP_KeysAlt", "F9 join, F10 stay", Menu("keys", "f9f10")),
            new("MP_KeysOff", "No keys", Menu("keys", "off"), Tooltip: "Answer in More options > Story: join or stay"),
        }),
        [ActionsDir + PagePause + ".xml"] = Page("Pausing, fights and loot", new Button[]
        {
            new("MP_PauseShared", "Pause: any pause menu pauses everyone", Menu("pause", "shared"), Tooltip: "Your menu pauses your game as usual; while a friend's pause menu is open your game is held too"),
            new("MP_PauseOff", "Pause: nobody is paused by a menu", Menu("pause", "off"), Tooltip: "With a friend in the world your own pause menu does not pause your game (Windows), and a friend's never holds you"),
            new("MP_SharedOn", "Shared fights and loot: on", Menu("shared", "on"), Tooltip: "Each enemy is run by the nearest player's game and follows it in the others; both your blows count; the host says whose an item is"),
            new("MP_SharedOff", "Shared fights and loot: off", Menu("shared", "off"), Tooltip: "Your copy of the world stays your own"),
        }),
        [GraphLoad] = LoadGraph(),
        [GraphText] = TextGraph(),
        [GraphMsg] = DialogGraph("MP_MSG", "OK", "", null),
        [GraphList] = ListGraph(),
        [GraphMenuWatch] = MenuWatchGraph(),
    }.Concat(Enum.GetValues<Title>().Select(t => new KeyValuePair<string, string>(TitleGraphName(t), TitleGraph(t)))).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Puts the text it is started with into the open page's title box: <c>UIAction.StartAction('MP_Text', {text})</c>.</summary>
    private static string TextGraph()
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "0"));
        int head = g.Add("UI:Functions:MainMenu:SetTitleBox", ("instanceID", "-1"), ("header", ""));
        g.Wire(start, "Args", head, "header");
        g.Wire(start, "StartAction", head, "Call");   // no End node (see TitleGraph)
        return g.ToString();
    }

    /// <summary>The game's confirmation box (AddConfirmation, the node of its own Quit question) with the text the action is started with.</summary>
    private static string DialogGraph(string id, string answer0, string answer1, string? onYes)
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "0"));
        int box = g.Add("UI:Functions:MainMenu:AddConfirmation", ("instanceID", "-1"), ("confirmationId", id), ("uiQuestion", ""), ("uiAnswer0", answer0), ("uiAnswer1", answer1),
            ("defaultSelect0", "1"), ("tooltip0", ""), ("tooltip1", ""), ("actionBackOnAnswer", "0"), ("sound0", ""), ("sound1", ""));
        g.Wire(start, "Args", box, "uiQuestion");
        g.Wire(start, "StartAction", box, "Call");
        if (onYes is not null)
        {
            int yes = g.Add("UI:Events:MainMenu:OnConfirm", ("instanceID", "-1"), ("Port", "0"), ("Idx", id));
            int run = g.Add("System:ExecuteScript", ("Script", onYes));
            g.Wire(yes, "onEvent", run, "Call");
        }
        return g.ToString();   // no End node (see TitleGraph)
    }

    /// <summary>Puts one fixed line into the open page's title box.</summary>
    private static string TitleGraph(Title t)
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "0"));
        int head = g.Add("UI:Functions:MainMenu:SetTitleBox", ("instanceID", "-1"), ("header", TitleText(t)));
        g.Wire(start, "StartAction", head, "Call");   // no End node: an End pops the page that is open (seen live: the tab closed)
        return g.ToString();
    }

    /// <summary>
    /// Loads a save: Variables KCDUS_LoadSaveId (the save's position in its playline's list, newest = 0) and KCDUS_LoadPlayLine are set by Lua
    /// (menu.lua), then <c>UIAction.StartAction('MP_Load', {})</c>. The game's own Load page uses the same node; on OnGameLoaded it hides the
    /// menus, which this does too. Proven in the retail game, from the main menu and from inside a world.
    /// </summary>
    private static string LoadGraph()
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "0"));
        int upd = g.Add("UI:Functions:SaveLoad:UpdateSavedGames");   // read the save folders again: a world that was just put there is only found after this
        int sid = g.Add("Variables:GlobalVariable", ("Name", "KCDUS_LoadSaveId"), ("Value", "0"));
        int pl = g.Add("Variables:GlobalVariable", ("Name", "KCDUS_LoadPlayLine"), ("Value", "0"));
        int load = g.Add("UI:Functions:SaveLoad:LoadSavedGame", ("SaveId", "0"), ("PlayLine", "0"));
        int said = g.Add("System:ExecuteScript", ("Script", "System.LogAlways('KCDUS|UILOAD|requested')"));
        int loaded = g.Add("UI:Events:SaveLoad:OnGameLoaded", ("Port", "-1"), ("Idx", ""));
        int cond = g.Add("Logic:Condition", ("Condition", "0"));
        int hideMain = g.Add("UI:Functions:MenuEvents:DisplayMainMenu", ("Display", "0"));
        int hideIn = g.Add("UI:Functions:MenuEvents:DisplayIngameMenu", ("Display", "0"));
        int done = g.Add("System:ExecuteScript", ("Script", "System.LogAlways('KCDUS|UILOAD|loaded')"));
        int end = g.Add("UI:Action:End", ("UseAsState", "0"), ("Args", ""));
        g.Wire(start, "StartAction", upd, "send");
        g.Wire(upd, "OnEvent", sid, "Get");
        g.Wire(sid, "CurValue", load, "SaveId");
        g.Wire(sid, "CurValue", pl, "Get");
        g.Wire(pl, "CurValue", load, "PlayLine");
        g.Wire(pl, "CurValue", load, "send");
        g.Wire(pl, "CurValue", said, "Call");
        g.Wire(loaded, "Result", cond, "Condition");
        g.Wire(loaded, "onEvent", cond, "In");
        g.Wire(cond, "OnTrue", hideMain, "send");
        g.Wire(cond, "OnTrue", hideIn, "send");
        g.Wire(cond, "OnTrue", done, "Call");
        g.Wire(hideIn, "OnEvent", end, "EndAction");
        return g.ToString();
    }

    /// <summary>
    /// Watches the game's own pause (ESC) menu: tells Lua when it opens and closes (KCDUS_MenuEvent(1|0)); started once per loaded world from Lua
    /// (<c>UIAction.StartAction('MP_MenuWatch', {})</c>). It has no End node, so it stays. Proved in the retail game: the events fire, and the menu pauses the
    /// world natively (world time stands, frames go on); the UI's ResumeGame node does not undo that, so this graph never tries.
    /// </summary>
    private static string MenuWatchGraph()
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "0"));
        int onStart = g.Add("UI:Events:MenuEvents:OnStartIngameMenu", ("Port", "-1"), ("Idx", ""));
        int onStop = g.Add("UI:Events:MenuEvents:OnStopIngameMenu", ("Port", "-1"), ("Idx", ""));
        int open = g.Add("System:ExecuteScript", ("Script", "KCDUS_MenuEvent(1)"));
        int close = g.Add("System:ExecuteScript", ("Script", "KCDUS_MenuEvent(0)"));
        int ready = g.Add("System:ExecuteScript", ("Script", "KCDUS_MenuEvent(2)"));
        g.Wire(start, "StartAction", ready, "Call");
        g.Wire(onStart, "onEvent", open, "Call");
        g.Wire(onStop, "onEvent", close, "Call");
        return g.ToString();
    }

    /// <summary>Lists a playline's saves into kcd.log as <c>[flow-log]</c> pairs: the list index, then the save's description (type|id|quest|objective|place|unixtime|date|hours|).</summary>
    private static string ListGraph()
    {
        var g = new Graph();
        int start = g.Add("UI:Action:Start", ("UseAsState", "0"));
        int upd = g.Add("UI:Functions:SaveLoad:UpdateSavedGames");
        int pl = g.Add("Variables:GlobalVariable", ("Name", "KCDUS_LoadPlayLine"), ("Value", "0"));
        int get = g.Add("UI:Functions:SaveLoad:GetSavedGames", ("FilterMask", "47"), ("PlayLine", "0"));
        int item = g.Add("UI:Events:SaveLoad:OnGetSavedGamesItem", ("Port", "-1"), ("Idx", ""));
        int logId = g.Add("Debug:Log", ("message", ""));
        int logName = g.Add("Debug:Log", ("message", ""));
        g.Wire(start, "StartAction", upd, "send");
        g.Wire(upd, "OnEvent", pl, "Get");
        g.Wire(pl, "CurValue", get, "PlayLine");
        g.Wire(pl, "CurValue", get, "send");
        g.Wire(item, "SaveId", logId, "message");
        g.Wire(item, "onEvent", logId, "input");
        g.Wire(item, "Name", logName, "message");
        g.Wire(item, "onEvent", logName, "input");
        return g.ToString();
    }

    // ------------------------------------------------------------------ building the pak

    /// <summary>All files of the UI pak, given a way to read the game's own graphs. Pure: the tests feed it text.</summary>
    public static IReadOnlyDictionary<string, string> BuildFiles(Func<string, string?> readGameFile)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string? main = readGameFile(MainMenu);
        if (main is null) throw new InvalidDataException("this game has no " + MainMenu);
        files[MainMenu] = PatchMenu(main);
        // the pause menu is optional: a missing or unexpected graph only means no tab while playing
        string? ingame = readGameFile(IngameMenu);
        if (ingame is not null)
        {
            try { files[IngameMenu] = PatchMenu(ingame); }
            catch (InvalidDataException) { /* the main menu still has the tab */ }
        }
        foreach (var (k, v) in Pages()) files[k] = v;
        files[StampFile] = Stamp;
        return files;
    }

    /// <summary>Bump when the pages change, so an agent that is newer than the pak on disk rebuilds it.</summary>
    public const int Revision = 14;
    public const string StampFile = "kcdus-ui-version.txt";
    public static string Stamp => KcdUs.Wire.Release.Current + "/ui" + Revision;

    /// <summary>Is the pak on disk the one this agent would build, and not older than the game's own menu files (a game update replaces GameData.pak)?</summary>
    public static bool IsCurrent(string gameDataPak, string uiPak)
    {
        try
        {
            if (!File.Exists(uiPak) || File.GetLastWriteTimeUtc(gameDataPak) > File.GetLastWriteTimeUtc(uiPak)) return false;
            using var z = ZipFile.OpenRead(uiPak);
            var e = z.GetEntry(StampFile);
            if (e is null) return false;
            using var r = new StreamReader(e.Open(), Encoding.ASCII);
            return r.ReadToEnd().Trim() == Stamp;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Reads the player's GameData.pak and writes <paramref name="outPak"/>. Never throws: the answer says why there is no tab.</summary>
    public static Result Build(string gameDataPak, string outPak)
    {
        try
        {
            using var zin = ZipFile.OpenRead(gameDataPak);
            string? Read(string name)
            {
                var e = zin.Entries.FirstOrDefault(x => string.Equals(x.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));
                if (e is null) return null;
                using var r = new StreamReader(e.Open(), Encoding.ASCII);
                return r.ReadToEnd();
            }
            var files = BuildFiles(Read);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPak))!);
            string tmp = outPak + ".part";
            using (var fs = File.Create(tmp))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in files.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var e = z.CreateEntry(name, CompressionLevel.NoCompression);
                    e.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var w = e.Open();
                    var bytes = Encoding.ASCII.GetBytes(text);
                    w.Write(bytes, 0, bytes.Length);
                }
            }
            File.Move(tmp, outPak, overwrite: true);
            return new Result(true, $"the Multiplayer tab is in {Path.GetFileName(outPak)} ({files.Count} files)", files);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new Result(false, "no Multiplayer tab: " + e.Message, new Dictionary<string, string>());
        }
    }
}
