// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using System.Text;
using System.Text.Json;

namespace KcdUs.Agent;

/// <summary>
/// The agent's local HTTP face, for the launcher (127.0.0.1 only):
///   GET  /status        everything the launcher shows (Session.Status as JSON)
///   POST /say?text=..   chat from the launcher
///   POST /choose?join|stay   the join-or-stay answer
///   POST /pref?ask|join|free the standing answer
///   POST /quit          stop the agent
/// </summary>
public sealed class StatusServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly HttpListener _http = new();
    private readonly Func<Session> _session;
    private readonly Func<Session.Status>? _status;
    private readonly AgentHost? _host;
    private readonly Action _quit;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int Port { get; }

    public StatusServer(Session session, int port, Action quit) : this(() => session, port, quit, null) { }

    /// <summary>With a host the session can change (the Multiplayer tab hosts, joins and leaves), and the settings page is served.</summary>
    public StatusServer(Func<Session> session, int port, Action quit, AgentHost? host)
    {
        _session = session; Port = port; _quit = quit; _host = host;
        _status = host is null ? null : host.GetStatus;
        _http.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _http.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await _http.GetContextAsync().ConfigureAwait(false); }
            catch { return; }
            try { Handle(c); }
            catch { try { c.Response.StatusCode = 500; c.Response.Close(); } catch { } }
        }
    }

    private void Handle(HttpListenerContext c)
    {
        var path = c.Request.Url?.AbsolutePath ?? "/";
        string body = "{}";
        int code = 200;
        if (path == "/settings" && _host != null) { HandleSettings(c, _host); return; }
        switch (path)
        {
            case "/status":
                body = JsonSerializer.Serialize(_status?.Invoke() ?? _session().GetStatus(), Json);
                break;
            case "/say" when c.Request.HttpMethod == "POST":
                _session().Say(c.Request.QueryString["text"] ?? "");
                body = "{\"ok\":true}";
                break;
            case "/choose" when c.Request.HttpMethod == "POST":
                body = JsonSerializer.Serialize(new { ok = _session().Choose(c.Request.QueryString.ToString() == "join" ? KcdUs.Agent.RailsChoice.Join : KcdUs.Agent.RailsChoice.Free, "launcher") });
                break;
            case "/pref" when c.Request.HttpMethod == "POST":
                if (RailsRules.ParsePref(c.Request.QueryString.ToString()) is { } p) _session().SetPref(p);
                body = "{\"ok\":true}";
                break;
            case "/quit" when c.Request.HttpMethod == "POST":
                body = "{\"ok\":true}";
                Task.Run(_quit);
                break;
            default:
                code = 404;
                body = "{\"error\":\"not found\"}";
                break;
        }
        var b = Encoding.UTF8.GetBytes(body);
        c.Response.StatusCode = code;
        c.Response.ContentType = "application/json";
        c.Response.ContentLength64 = b.Length;
        c.Response.OutputStream.Write(b, 0, b.Length);
        c.Response.Close();
    }

    // ================================================================ the settings page (the Multiplayer tab's "Settings in your browser")

    private static string E(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>GET shows the form; POST applies it. Both need the token only the agent's own link carries, so a web page cannot change them.</summary>
    private void HandleSettings(HttpListenerContext c, AgentHost host)
    {
        string note = "";
        string token = c.Request.QueryString["t"] ?? "";
        if (c.Request.HttpMethod == "POST")
        {
            using var r = new StreamReader(c.Request.InputStream, Encoding.UTF8);
            var form = System.Web.HttpUtility.ParseQueryString(r.ReadToEnd());
            token = form["t"] ?? token;
            if (token == host.SettingsToken)
            {
                var changed = host.ApplySettings(form);
                note = changed.Length == 0 ? "Nothing changed." : "Saved: " + changed + ". Host or join again from the Multiplayer tab to use a new address, port or password.";
            }
        }
        bool ok = token == host.SettingsToken;
        string html = ok ? Page(host.Config, host, token, note) : "<!doctype html><meta charset=utf-8><title>Multiplayer</title><p>Open this page from the Multiplayer tab in the game (Settings in your browser).";
        var b = Encoding.UTF8.GetBytes(html);
        c.Response.StatusCode = ok ? 200 : 403;
        c.Response.ContentType = "text/html; charset=utf-8";
        c.Response.Headers["X-Frame-Options"] = "DENY";
        c.Response.ContentLength64 = b.Length;
        c.Response.OutputStream.Write(b, 0, b.Length);
        c.Response.Close();
    }

    private static string Sel(string cur, string v, string label) => $"<option value=\"{v}\"{(cur == v ? " selected" : "")}>{E(label)}</option>";

    private static string Page(AgentConfig cfg, AgentHost host, string token, string note) => $@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Deliver Us: Multiplayer</title>
<style>
:root {{ --bg:#f6f1e7; --fg:#2b2218; --card:#fffaf0; --line:#cdbf9f; --accent:#7a4b12; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg:#1b1610; --fg:#efe6d2; --card:#262017; --line:#4a3f2c; --accent:#e0a85a; }} }}
body {{ background:var(--bg); color:var(--fg); font:16px/1.5 Georgia,serif; margin:0; padding:24px 16px; }}
main {{ max-width:34rem; margin:0 auto; }}
h1 {{ font-size:1.5rem; margin:0 0 4px; }} p.sub {{ margin:0 0 20px; opacity:.8; }}
form {{ background:var(--card); border:1px solid var(--line); border-radius:8px; padding:16px; }}
label {{ display:block; margin:12px 0 4px; font-weight:bold; }} small {{ display:block; opacity:.75; font-weight:normal; }}
input,select {{ width:100%; box-sizing:border-box; padding:8px; font:inherit; background:var(--bg); color:var(--fg); border:1px solid var(--line); border-radius:4px; }}
button {{ margin-top:18px; padding:10px 18px; font:inherit; background:var(--accent); color:var(--bg); border:0; border-radius:4px; cursor:pointer; }}
.note {{ background:var(--card); border-left:4px solid var(--accent); padding:8px 12px; margin:0 0 16px; }}
</style></head><body><main>
<h1>Multiplayer</h1><p class=""sub"">Kingdom Come: Deliver Us co-op. {E(host.Describe())}</p>
{(note.Length > 0 ? $"<p class=\"note\">{E(note)}</p>" : "")}
<form method=""post"" action=""/settings"">
<input type=""hidden"" name=""t"" value=""{E(token)}"">
<label>Your name<small>What your friends see over your head.</small><input name=""name"" value=""{E(cfg.PlayerName)}"" maxlength=""24""></label>
<label>Host's address<small>To join a game: the host's address and port, like 100.64.1.2:{cfg.RelayPort}.</small><input name=""relay"" value=""{E(cfg.RelayHost == "127.0.0.1" ? "" : cfg.RelayHost + ":" + cfg.RelayPort)}"" placeholder=""100.64.1.2:{cfg.RelayPort}""></label>
<label>Password<small>The same word on both sides (empty for none).</small><input name=""password"" value=""{E(cfg.Password)}"" maxlength=""40""></label>
<label>Your game's name<small>To host: the name friends see.</small><input name=""servername"" value=""{E(cfg.ServerName)}"" maxlength=""40""></label>
<label>Port to host on<small>Friends connect to this port on your computer.</small><input name=""port"" value=""{cfg.RelayPort}"" inputmode=""numeric""></label>
<label>When the host's story goes on rails<select name=""pref"">{Sel(cfg.RailsPref, "ask", "Ask me each time")}{Sel(cfg.RailsPref, "join", "Always join my host")}{Sel(cfg.RailsPref, "free", "Always stay in the open world")}</select></label>
<label>Keys for join / stay<select name=""keys"">{Sel(cfg.KeyPreset, "f11f12", "F11 join, F12 stay")}{Sel(cfg.KeyPreset, "f9f10", "F9 join, F10 stay")}{Sel(cfg.KeyPreset, "off", "No keys (use the menu)")}</select></label>
<button type=""submit"">Save</button>
</form></main></body></html>";

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        try { _http.Stop(); _http.Close(); } catch { }
        if (_loop != null) { try { await _loop.ConfigureAwait(false); } catch { } }
    }
}
