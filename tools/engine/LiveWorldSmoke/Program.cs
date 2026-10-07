// SPDX-License-Identifier: GPL-3.0-only
// One real disposable game plus a synthetic sender, using the production join path.
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using KcdUs.Agent;
using KcdUs.Agent.Worlds;
using KcdUs.Wire;

using var record = JsonDocument.Parse(File.ReadAllText(args[0]));
var s = record.RootElement;
string marker = s.GetProperty("profile").GetString()!, work = s.GetProperty("work").GetString()!;
string root = s.GetProperty("saveRoot").GetString()!, gameRoot = s.GetProperty("gameRoot").GetString()!;
if (!Regex.IsMatch(marker, "^KCDUS-engine-[0-9a-f]{32}$") || Path.GetFileName(work) != marker
    || Path.GetFullPath(root) != Path.Combine(Path.GetFullPath(work), "saved-games")
    || Path.GetFullPath(gameRoot) != Path.Combine(Path.GetFullPath(work), "game-root"))
    throw new InvalidOperationException("Only a verified harness-owned private profile is permitted.");
string log;
using (var logStream = new FileStream(Path.Combine(gameRoot, "kcd.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
using (var reader = new StreamReader(logStream)) log = reader.ReadToEnd().Replace('\\', '/').ToLowerInvariant();
if (!log.Contains("user folder is '" + Path.Combine(root, "KingdomCome").Replace('\\', '/').ToLowerInvariant() + "'"))
    throw new InvalidOperationException("Engine has not confirmed this disposable folder.");
void CheckRealSaves()
{
    string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "kingdomcome", "saves");
    var actual = Directory.GetFiles(real, "*.whs", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(real, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant());
    var expected = s.GetProperty("realSaveHashes");
    if (actual.Count != expected.EnumerateObject().Count() || expected.EnumerateObject().Any(p => !actual.TryGetValue(p.Name, out var h) || h != p.Value.GetString()))
        throw new IOException("Real saves changed; preserve evidence and stop this experiment.");
}
CheckRealSaves();
void CheckOwner()
{
    int pid = s.GetProperty("pid").GetInt32();
    if (pid <= 0) throw new InvalidOperationException("No owned game is active.");
    var start = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
    start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command");
    start.ArgumentList.Add($"$p=Get-CimInstance Win32_Process -Filter 'ProcessId={pid}'; $owners=@(Get-NetTCPConnection -State Listen -LocalPort 4600 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess); if ($p -and $p.CommandLine.Contains('{marker}') -and $owners.Count -eq 1 -and $owners[0] -eq {pid}) {{ 'owned' }}");
    using var process = Process.Start(start)!;
    string reply = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
    if (reply != "owned") throw new InvalidOperationException("Disposable process/remote-console ownership no longer matches.");
}
CheckOwner();
int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
int relay = FreePort(); string worldId = Guid.NewGuid().ToString("N");
string guestReg = Path.Combine(work, "smoke-worlds.json");
if (File.Exists(guestReg) && WorldRegistry.Load(guestReg).PendingLoad is { } retry) worldId = retry.WorldId;
string hostRoot = Path.Combine(work, "smoke-host");
var hostStore = new SaveStore(Path.Combine(hostRoot, "saves"), Path.Combine(hostRoot, "backups"));
Directory.CreateDirectory(hostStore.PlaylineDir(0));
var destination = File.ReadAllBytes(args[1]);
File.WriteAllBytes(Path.Combine(hostStore.PlaylineDir(0), "world.whs"), destination);
var registry = new WorldRegistry { Active = worldId }; var w = registry.Upsert(worldId, "Native smoke world");
w.Playline = 0; var info = SaveInfo.Read(destination)!; w.Hours = info.Hours; w.SavedUnix = info.SavedUnix;
string hostReg = Path.Combine(hostRoot, "worlds.json"); registry.Save(hostReg);
using var ct = new CancellationTokenSource(TimeSpan.FromMinutes(4));
await using var senderGame = new SenderGame();
await using var actualGame = new ConsoleGameLink(gameRoot, log: l => Console.WriteLine("console: " + l));
await using var host = new AgentHost(new AgentConfig { Idle = true, RelayPort = relay, PlayerName = "Sender", Hotkeys = false }, senderGame, Release.Current, FreePort(), l => Console.WriteLine("host: " + l))
{ Store = hostStore, WorldListPath = hostReg, SavePath = Path.Combine(hostRoot, "config.json"), AllowLoopbackJoin = true };
var guestStore = new SaveStore(Path.Combine(root, "KingdomCome", "saves"), Path.Combine(work, "smoke-backups"));
await using var guest = new AgentHost(new AgentConfig { Idle = true, RelayPort = relay, PlayerName = "Native guest", HenryMode = "mine", AutoSync = false, Hotkeys = false }, actualGame, Release.Current, FreePort(), l => Console.WriteLine("guest: " + l))
{ Store = guestStore, WorldListPath = guestReg, SavePath = Path.Combine(work, "smoke-config.json"), AllowLoopbackJoin = true };
await host.StartAsync(ct.Token); await guest.StartAsync(ct.Token); actualGame.Start(ct.Token);
var pump = Task.Run(async () => { while (!ct.IsCancellationRequested) { host.Tick(); guest.Tick(); await Task.Delay(100, ct.Token); } }, ct.Token);
async Task Until(Func<bool> condition, string message)
{
    while (!condition()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(100, ct.Token); }
    Console.WriteLine("PASS: " + message);
}
try
{
    await host.HandleAsync("host", ""); await Until(() => host.Session.MyId != 0, "synthetic sender connected");
    await guest.HandleAsync("join", ""); await Until(() => guest.Session.MyId != 0 && guest.Session.InWorld, "native guest connected in a loaded disposable world");
    await guest.HandleAsync("world", guest.World!.Registry.PendingLoad is not null ? "play" : "join");
    await Until(() => guest.World!.Registry.Active == worldId && guest.World.Registry.PendingLoad is null, "production join/install/load/native-save acknowledgement");
    var saved = guestStore.Newest(guest.World!.Registry.ActiveWorld!.Playline)!;
    var expected = new CharacterCheckpoint(guestStore.BackupRoot).Read(guest.World.Registry.MyCharacterSha256);
    if (!ExactTraitsSave.SameProgressionAndInventory(expected, ExactTraitsSave.CaptureCharacter(File.ReadAllBytes(saved.Path))))
        throw new InvalidDataException("Native saved character differs.");
    Console.WriteLine("PASS: exact progression/perks/inventory native readback; real saves unchanged");
    CheckRealSaves();
    CheckOwner();
}
finally { ct.Cancel(); try { await pump; } catch (OperationCanceledException) { } }

sealed class SenderGame : IGameLink
{
    public event Action<GameLine>? Line;
    public event Action<bool>? ConsoleStateChanged { add { } remove { } }
    public bool ConsoleConnected => false;
    public void Send(string record) { }
    public void SendLatest(string key, string record) { }
    public void Start(CancellationToken ct) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
