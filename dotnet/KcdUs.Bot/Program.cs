// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdUs.Agent;
using KcdUs.Wire;

// A stand-in for a second player, for testing on one machine: joins the relay as a guest, walks a circle around the host (or
// around a fixed point), says hello, and answers the join-or-stay question by its --pref.
//   KcdUsBot [--relay 127.0.0.1:7788] [--name Bot] [--radius 4] [--speed 1.6] [--center x,y,z] [--chat "text"] [--pref join|free]
var o = new Dictionary<string, string>();
for (int i = 0; i < args.Length - 1; i += 2) o[args[i].TrimStart('-')] = args[i + 1];
string rel = o.GetValueOrDefault("relay", "127.0.0.1:" + Proto.DefaultPort);
var hp = rel.Split(':');
string name = Safe.Name(o.GetValueOrDefault("name", "Bot"));
double radius = double.Parse(o.GetValueOrDefault("radius", "4"), CultureInfo.InvariantCulture);
double speed = double.Parse(o.GetValueOrDefault("speed", "1.6"), CultureInfo.InvariantCulture);
double[]? center = o.TryGetValue("center", out var c) ? c.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray() : null;
var pref = o.GetValueOrDefault("pref", "join");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await using var relay = new RelayClient(new RelayEndpoint { Host = hp[0], Port = hp.Length > 1 ? int.Parse(hp[1]) : Proto.DefaultPort, Name = name, Role = "guest", Release = Release.Current });

PlayerState? host = null;
int hostId = 0, myId = 0;
relay.Frame += f =>
{
    var x = f.Fields;
    switch (f.Type)
    {
        case MessageType.Welcome: myId = int.Parse(x[0]); hostId = int.Parse(x[1]); Console.WriteLine($"welcome #{myId}, host #{hostId}"); break;
        case MessageType.PState when int.Parse(x[0]) == hostId: host = PlayerState.TryDecode(x, 1); break;
        case MessageType.PChat: Console.WriteLine($"<{x[1]}> {string.Join(' ', x.Skip(2))}"); break;
        case MessageType.PHostEvent when x.Length > 3 && x[1] == "beat" && x[2] == "enter":
            var period = StorySections.PeriodOf(x[3]);
            Console.WriteLine($"host entered {x[3]} ({(x.Length > 4 ? x[4] : "")}): answering {pref}");
            if (period is not null) relay.Send(MessageType.Event, "choice|" + RailsRules.ChoiceText(period.Id, pref == "join" ? RailsChoice.Join : RailsChoice.Free));
            break;
        case MessageType.Reject: Console.WriteLine("REFUSED: " + f.Text); cts.Cancel(); break;
    }
};
relay.Start(cts.Token);
while (!relay.Connected && !cts.IsCancellationRequested) await Task.Delay(50);
relay.Send(MessageType.Event, "world|1");
if (o.TryGetValue("chat", out var chat)) relay.Send(MessageType.Chat, chat);

double t = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
long last = 0;
while (!cts.IsCancellationRequested)
{
    long now = sw.ElapsedMilliseconds;
    double dt = (now - last) / 1000.0; last = now; t += dt;
    double cx = center?[0] ?? host?.X ?? 0, cy = center?[1] ?? host?.Y ?? 0, cz = center?[2] ?? host?.Z ?? 0;
    if (center is not null || host is not null)
    {
        double w = speed / radius;                      // angular speed for the walking pace
        double a = t * w;
        double x = cx + radius * Math.Cos(a), y = cy + radius * Math.Sin(a);
        double vx = -radius * w * Math.Sin(a), vy = radius * w * Math.Cos(a);
        double yaw = Math.Atan2(vy, vx) - Math.PI / 2;
        relay.Send(MessageType.State, new PlayerState(x, y, cz, yaw, vx, vy, 0, 0, 100, 100, "MotionMovement", host?.WorldTime ?? 36000).Encode());
    }
    if (now / 5000 != (now - (long)(dt * 1000)) / 5000) relay.Send(MessageType.Ping, now.ToString());
    try { await Task.Delay(100, cts.Token); } catch { break; }
}
