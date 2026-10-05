// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using KcdUs.Relay;
using KcdUs.Wire;

// KcdUsRelay [--port 7788] [--name "Deliver Us"] [--password secret] [--max 4]
var o = new RelayOptions();
for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    string? next = i + 1 < args.Length ? args[i + 1] : null;
    switch (a)
    {
        case "--port" or "-p" when next != null: o.Port = int.Parse(next); i++; break;
        case "--name" when next != null: o.ServerName = Safe.Clean(next, 40); i++; break;
        case "--password" when next != null: o.Password = Safe.Clean(next, 40); i++; break;
        case "--max" when next != null: o.MaxPlayers = Math.Clamp(int.Parse(next), 2, 8); i++; break;
        case "--help" or "-h":
            Console.WriteLine("KcdUsRelay [--port 7788] [--name \"Deliver Us\"] [--password secret] [--max 4]");
            return 0;
    }
}

Console.WriteLine($"Kingdom Come: Deliver Us relay {Release.Current} (protocol {Proto.ProtocolVersion})");
await using var relay = new RelayServer(o, line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"));
try
{
    relay.Start();
}
catch (System.Net.Sockets.SocketException e)
{
    Console.Error.WriteLine($"cannot listen on port {o.Port}: {e.Message} (is another relay running?)");
    return 2;
}

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
return 0;
