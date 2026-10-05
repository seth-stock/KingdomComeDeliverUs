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
    private readonly Session _session;
    private readonly Action _quit;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int Port { get; }

    public StatusServer(Session session, int port, Action quit)
    {
        _session = session; Port = port; _quit = quit;
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
        switch (path)
        {
            case "/status":
                body = JsonSerializer.Serialize(_session.GetStatus(), Json);
                break;
            case "/say" when c.Request.HttpMethod == "POST":
                _session.Say(c.Request.QueryString["text"] ?? "");
                body = "{\"ok\":true}";
                break;
            case "/choose" when c.Request.HttpMethod == "POST":
                body = JsonSerializer.Serialize(new { ok = _session.Choose(c.Request.QueryString.ToString() == "join" ? KcdUs.Agent.RailsChoice.Join : KcdUs.Agent.RailsChoice.Free, "launcher") });
                break;
            case "/pref" when c.Request.HttpMethod == "POST":
                if (RailsRules.ParsePref(c.Request.QueryString.ToString()) is { } p) _session.SetPref(p);
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

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        try { _http.Stop(); _http.Close(); } catch { }
        if (_loop != null) { try { await _loop.ConfigureAwait(false); } catch { } }
    }
}
