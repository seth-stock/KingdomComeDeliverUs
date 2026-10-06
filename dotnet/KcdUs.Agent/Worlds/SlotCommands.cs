// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Agent.Worlds;

public static class SlotCommands
{
    public static bool Requested(string[] args) => args.Any(a => a is "--slot-list" or "--slot-archive" or "--slot-restore" or "--slot-recover");
    public static int Run(string[] args, AgentConfig cfg)
    {
        try
        {
            string root = SaveStore.FindRoot(cfg.SavesDir, GameLocator.Find(cfg.GameDir)) ?? throw new InvalidOperationException("Save directory not found. Configure savesDir first.");
            string archive = cfg.BackupDir.Length > 0 ? cfg.BackupDir : SaveStore.DefaultBackupRoot();
            var manager = new SlotLeaseManager(root, archive);
            string? registryPath = cfg.WorldsFile.Length > 0 ? cfg.WorldsFile : null;
            var registry = WorldRegistry.Load(registryPath);
            void Bind(SlotLeaseManager.Lease lease) { SlotBindings.Archive(registry, lease); registry.Save(registryPath); }
            if (args.Contains("--slot-list"))
            {
                foreach (var l in manager.List()) Console.WriteLine($"{l.Id} | Playline {l.Playline + 1} | {(l.Restored ? "restored" : "archived")}");
                return 0;
            }
            if (!args.Contains("--cloud-sync-paused")) throw new InvalidOperationException("Pause Steam Cloud synchronization before slot switching; add --cloud-sync-paused to acknowledge this prerequisite.");
            if (args.Contains("--slot-recover"))
            {
                manager.Recover();
                foreach (var lease in manager.List().Where(l => l.Restored)) SlotBindings.Restore(registry, lease);
                registry.Save(registryPath);
                Console.WriteLine("Slot transactions recovered; all displaced trees remain archived."); return 0;
            }
            string Value(string name)
            { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing value for " + name); }
            if (args.Contains("--slot-archive"))
            {
                if (!int.TryParse(Value("--slot-archive"), out int n) || n is < 1 or > 5) throw new ArgumentException("Use the displayed Playline number, 1 through 5.");
                var lease = manager.Archive(n - 1, Bind);
                Console.WriteLine($"Playline {n} is now free. Original files are verified and archived. Restore with --slot-restore {lease.Id} --cloud-sync-paused (game closed).");
                return 0;
            }
            string restoreId = Value("--slot-restore");
            manager.Restore(restoreId, Bind);
            SlotBindings.Restore(registry, manager.List().Single(l => l.Id == restoreId));
            registry.Save(registryPath);
            Console.WriteLine("Original playline restored; outgoing co-op saves remain in the slot-switch archive."); return 0;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        { Console.Error.WriteLine("Slot operation stopped: " + e.Message); return 2; }
    }
}
