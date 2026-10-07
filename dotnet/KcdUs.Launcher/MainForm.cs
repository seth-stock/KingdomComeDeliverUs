// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using KcdUs.Agent;
using KcdUs.Wire;

namespace KcdUs.Launcher;

/// <summary>
/// The launcher: pick Host or Join, start the agent, start the game through Steam, and watch the status. All logic lives in the agent;
/// this window only starts it, reads its /status and sends the player's clicks (join/stay, chat).
/// </summary>
public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly AgentConfig _cfg;
    private Process? _agent;

    private readonly TextBox _gameDir = new() { ReadOnly = true };
    private readonly TextBox _name = new();
    private readonly RadioButton _tab = new() { Text = "Decide in the game (its Multiplayer tab)", Checked = true, AutoSize = true };
    private readonly RadioButton _host = new() { Text = "Host a game", AutoSize = true };
    private readonly RadioButton _join = new() { Text = "Join a friend", AutoSize = true };
    private readonly TextBox _address = new();
    private readonly TextBox _port = new();
    private readonly TextBox _password = new();
    private readonly TextBox _serverName = new();
    private readonly ComboBox _pref = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _start = new() { Text = "1. START CO-OP", Height = 36 };
    private readonly Button _game = new() { Text = "2. START THE GAME", Height = 36, Enabled = false };
    private readonly Button _stop = new() { Text = "Stop", Height = 36, Enabled = false };
    private readonly Label _status = new() { AutoSize = false, Height = 54, Dock = DockStyle.Top, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    private readonly ListView _players = new() { View = View.Details, Height = 100, FullRowSelect = true };
    private readonly Label _story = new() { AutoSize = false, Height = 54, Dock = DockStyle.Top };
    private readonly Button _joinBtn = new() { Text = "Join my host  (F11)", Enabled = false };
    private readonly Button _stayBtn = new() { Text = "Stay in the open world  (F12)", Enabled = false };
    private readonly TextBox _chat = new();
    private readonly Label _address2 = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    private readonly Label _notice = new() { AutoSize = false, Height = 40, Dock = DockStyle.Bottom, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8f) };

    public MainForm()
    {
        Text = $"Kingdom Come: Deliver Us {Release.Current}  (unofficial co-op for Kingdom Come: Deliverance)";
        Width = 760; Height = 700; StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9f);
        _cfg = AgentConfig.Load();
        if (string.IsNullOrEmpty(_cfg.GameDir)) _cfg.GameDir = GameLocator.Find(null) ?? "";
        Build();
        Load += (_, _) => LoadSettings();
        FormClosing += (_, _) => StopAgent();
        _poll.Tick += async (_, _) => await PollAsync();
        _poll.Start();
    }

    private void Build()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Padding = new Padding(12) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void Row(string a, Control ca, string? b = null, Control? cb = null)
        {
            grid.Controls.Add(new Label { Text = a, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) });
            ca.Dock = DockStyle.Fill; grid.Controls.Add(ca);
            grid.Controls.Add(new Label { Text = b ?? "", AutoSize = true, Anchor = AnchorStyles.Left });
            if (cb != null) { cb.Dock = DockStyle.Fill; grid.Controls.Add(cb); } else grid.Controls.Add(new Label());
        }
        Row("Game folder", _gameDir, "Your name", _name);
        var modes = new FlowLayoutPanel { AutoSize = true }; modes.Controls.Add(_tab); modes.Controls.Add(_host); modes.Controls.Add(_join);
        grid.Controls.Add(new Label { Text = "Mode", AutoSize = true, Anchor = AnchorStyles.Left }); grid.Controls.Add(modes);
        grid.Controls.Add(new Label()); grid.Controls.Add(_address2);
        Row("Relay address", _address, "Port", _port);
        Row("Password", _password, "Server name", _serverName);
        _pref.Items.AddRange(new object[] { "Ask me every time", "Always join my host", "Always stay in the open world" });
        Row("When my host is on rails", _pref);
        _password.UseSystemPasswordChar = false;
        _host.CheckedChanged += (_, _) => ApplyMode();
        _join.CheckedChanged += (_, _) => ApplyMode();
        _start.Click += (_, _) => StartAgent();
        _stop.Click += (_, _) => StopAgent();
        _game.Click += (_, _) => StartGame();
        _joinBtn.Click += async (_, _) => await PostAsync("/choose?join");
        _stayBtn.Click += async (_, _) => await PostAsync("/choose?stay");
        _pref.SelectedIndexChanged += async (_, _) => await PostAsync("/pref?" + new[] { "ask", "join", "free" }[Math.Max(0, _pref.SelectedIndex)]);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12, 4, 12, 4) };
        foreach (var b in new[] { _start, _game, _stop }) { b.Width = 200; buttons.Controls.Add(b); }
        _stop.Width = 100;

        _players.Columns.Add("Player", 160); _players.Columns.Add("Role", 70); _players.Columns.Add("In view", 70);
        _players.Columns.Add("Distance", 80); _players.Columns.Add("Story choice", 110);
        var choice = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 4, 12, 4) };
        _joinBtn.Width = 200; _stayBtn.Width = 240; choice.Controls.Add(_joinBtn); choice.Controls.Add(_stayBtn);
        _chat.PlaceholderText = "Type a message to your friends and press Enter";
        _chat.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || _chat.Text.Trim().Length == 0) return;
            e.SuppressKeyPress = true;
            await PostAsync("/say?text=" + Uri.EscapeDataString(_chat.Text));
            _chat.Clear();
        };
        _notice.Text = "Unofficial community mod, not affiliated with Warhorse Studios or Deep Silver. A modified port of Kingdom Come: Together " +
                       "(https://github.com/DeepFriedDepp/KingdomCome-Together), GPL-3.0. Everyone you play with must run the same version.";

        var pad = (Control c) => { c.Margin = new Padding(12, 4, 12, 4); return c; };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        _players.Dock = DockStyle.Top; _chat.Dock = DockStyle.Top;
        body.Controls.Add(_chat); body.Controls.Add(choice); body.Controls.Add(_story); body.Controls.Add(_players); body.Controls.Add(_status);
        Controls.Add(body); Controls.Add(buttons); Controls.Add(grid); Controls.Add(_notice);
        _ = pad;
    }

    private void LoadSettings()
    {
        _gameDir.Text = _cfg.GameDir.Length > 0 ? _cfg.GameDir : "(not found: Kingdom Come: Deliverance must be installed in Steam)";
        _name.Text = _cfg.PlayerName;
        _address.Text = _cfg.RelayHost == "127.0.0.1" && _cfg.Role == "host" ? "" : _cfg.RelayHost;
        _port.Text = _cfg.RelayPort.ToString();
        _password.Text = _cfg.Password;
        _serverName.Text = _cfg.ServerName;
        _pref.SelectedIndex = RailsRules.ParsePref(_cfg.RailsPref) switch { RailsPref.Join => 1, RailsPref.Free => 2, _ => 0 };
        (_cfg.Idle ? _tab : _cfg.Role == "host" ? _host : _join).Checked = true;
        ApplyMode();
        _status.Text = GameLocator.LooksLikeTheGame(_cfg.GameDir) ? "Ready. Press 1 to start the co-op agent." : "Kingdom Come: Deliverance was not found.";
    }

    private void ApplyMode()
    {
        bool host = _host.Checked;
        _address.Enabled = _join.Checked; _serverName.Enabled = host;
        _address2.Text = _tab.Checked ? "In the game: Multiplayer > Host a game / Join a game. Friends use: " + string.Join("  or  ", MyAddresses())
            : host ? "Tell your friends: " + string.Join("  or  ", MyAddresses()) : "Ask your host for their address and port.";
    }

    /// <summary>The addresses a friend could use: a Tailscale 100.x address first, then the LAN ones. Never "localhost".</summary>
    private static List<string> MyAddresses()
    {
        var all = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !ua.Address.ToString().StartsWith("169.254"))
                    all.Add(ua.Address.ToString());
        }
        var ordered = all.OrderBy(a => a.StartsWith("100.") ? 0 : 1).Take(3).Select(a => a + ":" + Proto.DefaultPort).ToList();
        if (ordered.Count == 0) ordered.Add("(no network found)");
        return ordered;
    }

    private void SaveSettings()
    {
        _cfg.PlayerName = Safe.Name(_name.Text);
        _cfg.Idle = _tab.Checked;
        if (!_tab.Checked)
        {
            _cfg.Role = _host.Checked ? "host" : "guest";
            _cfg.Serve = _host.Checked;
            _cfg.RelayHost = _host.Checked ? "127.0.0.1" : _address.Text.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(_address.Text)) _cfg.RelayHost = _address.Text.Trim();   // the tab joins the address saved here
        _cfg.RelayPort = int.TryParse(_port.Text, out var p) ? p : Proto.DefaultPort;
        _cfg.Password = _password.Text;
        _cfg.ServerName = Safe.Clean(_serverName.Text, 40) is { Length: > 0 } s ? s : "Deliver Us";
        _cfg.RailsPref = new[] { "ask", "join", "free" }[Math.Max(0, _pref.SelectedIndex)];
        _cfg.Save();
    }

    private void StartAgent()
    {
        if (!GameLocator.LooksLikeTheGame(_cfg.GameDir)) { MessageBox.Show("Kingdom Come: Deliverance was not found. Install it in Steam first."); return; }
        if (_join.Checked && string.IsNullOrWhiteSpace(_address.Text))
        {
            MessageBox.Show("Enter your host's address (for example 100.64.12.3). Ask them: it is shown in their launcher.", "Address needed");
            return;
        }
        var bad = _address.Text.Trim().ToLowerInvariant();
        if (_join.Checked && (bad == "localhost" || bad.StartsWith("127.")))
        {
            MessageBox.Show("That address is this computer. Use your host's address (their launcher shows it).", "Not localhost");
            return;
        }
        SaveSettings();
        StopAgent();
        var exe = Path.Combine(AppContext.BaseDirectory, "KcdUsAgent.exe");
        if (!File.Exists(exe)) { MessageBox.Show("KcdUsAgent.exe is missing next to the launcher. Reinstall."); return; }
        try
        {
            _agent = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception e) { MessageBox.Show("Could not start the agent: " + e.Message); return; }
        _start.Enabled = false; _stop.Enabled = true; _game.Enabled = true;
        _status.Text = "Agent starting...";
    }

    private void StopAgent()
    {
        try { if (_agent is { HasExited: false }) { _agent.Kill(entireProcessTree: true); _agent.WaitForExit(2000); } } catch { }
        _agent = null;
        _start.Enabled = true; _stop.Enabled = false;
    }

    private void StartGame()
    {
        try
        {
            EngineGameStart.Start(_cfg.GameDir,Path.Combine(AppContext.BaseDirectory,"KcdUsEngineBridge.dll"));
            _status.Text = "Starting the game. Wait for the main menu, then load your save or the received co-op save.";
        }
        catch(Exception e) { MessageBox.Show("Could not start the game: "+e.Message); }
    }

    private int StatusPort => _cfg.StatusPort;

    private async Task PostAsync(string path)
    {
        try { await _http.PostAsync($"http://127.0.0.1:{StatusPort}{path}", null); } catch { }
    }

    private async Task PollAsync()
    {
        if (_agent is { HasExited: true }) { _agent = null; _start.Enabled = true; _stop.Enabled = false; _status.Text = "The agent stopped. See %LocalAppData%\\KCDUS\\logs\\agent.log"; return; }
        if (_agent is null) return;
        try
        {
            var s = await _http.GetStringAsync($"http://127.0.0.1:{StatusPort}/status");
            using var d = JsonDocument.Parse(s);
            var r = d.RootElement;
            string msg = r.GetProperty("message").GetString() ?? "";
            string modv = r.GetProperty("gameModVersion").GetString() ?? "";
            string head = msg;
            if (modv.Length > 0 && !Release.Same(modv, Release.Current)) head = $"The game's mod is {modv} but this launcher is {Release.Current}: reinstall. " + msg;
            int fps = r.GetProperty("fps").GetInt32(), errs = r.GetProperty("scriptErrors").GetInt32();
            int rtt = r.GetProperty("rttMs").GetInt32();
            _status.Text = head + (r.GetProperty("inWorld").GetBoolean() ? $"\r\nGame {fps} fps, script errors {errs}, relay ping {(rtt < 0 ? "?" : rtt + " ms")}" : "");
            _players.Items.Clear();
            foreach (var p in r.GetProperty("players").EnumerateArray())
                _players.Items.Add(new ListViewItem(new[]
                {
                    p.GetProperty("name").GetString() ?? "", p.GetProperty("role").GetString() ?? "",
                    p.GetProperty("inView").GetBoolean() ? "yes" : "no",
                    p.TryGetProperty("distanceM", out var dm) && dm.ValueKind == JsonValueKind.Number ? dm.GetDouble().ToString("0") + " m" : "",
                    p.GetProperty("choice").GetString() ?? "",
                }));
            var st = r.GetProperty("story");
            string section = st.GetProperty("section").GetString() ?? "", why = st.GetProperty("why").GetString() ?? "", tier = st.GetProperty("tier").GetString() ?? "open";
            bool host = r.GetProperty("role").GetString() == "host";
            if (section.Length == 0) _story.Text = "Story: nobody is on rails. Everyone is free.";
            else if (host) _story.Text = $"You are in: {why} ({tier}).\r\nJoined {st.GetProperty("joined").GetInt32()}, staying {st.GetProperty("staying").GetInt32()}, deciding {st.GetProperty("deciding").GetInt32()}.";
            else _story.Text = $"Your host is in: {why} ({tier}).\r\nYour choice: {st.GetProperty("myChoice").GetString()}.";
            bool asking = st.GetProperty("asking").GetBoolean() || (!host && section.Length > 0);
            _joinBtn.Enabled = _stayBtn.Enabled = !host && asking;
        }
        catch { if (_agent != null) _status.Text = "Waiting for the agent..."; }
    }
}
