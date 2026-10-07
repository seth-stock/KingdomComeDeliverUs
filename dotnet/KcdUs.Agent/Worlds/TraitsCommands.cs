// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KcdUs.Agent.Worlds;

public static class TraitsCommands
{
    public static int Run(string[] args)
    {
        try
        {
            string Arg(string key)
            {
                int at = Array.IndexOf(args, key);
                if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("--")) throw new ArgumentException($"Missing {key}.");
                return Path.GetFullPath(args[at + 1]);
            }
            string character = Arg("--character-save"), world = Arg("--world-save"), output = Arg("--output");
            string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "kingdomcome", "saves");
            if (output.StartsWith(Path.GetFullPath(saves) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Prepare outside the live saves directory; engine validation is required before installation.");
            if (File.Exists(output)) throw new IOException("Output already exists; refusing overwrite.");
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(output)!); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Stage output through a normal directory, not a junction or symbolic link.");
            byte[] Read(string path)
            {
                using var stream = File.OpenRead(path);
                if (stream.Length > ExactTraitsSave.MaxFileBytes) throw new InvalidDataException("Save exceeds bounds.");
                var data = new byte[checked((int)stream.Length)]; stream.ReadExactly(data); return data;
            }
            var traits = ExactTraitsSave.Capture(Read(character));
            var staged = ExactTraitsSave.Prepare(Read(world), traits);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(staged); file.Flush(true);
            }
            Console.WriteLine($"Prepared progression save: {output}");
            Console.WriteLine($"SHA256: {Convert.ToHexString(SHA256.HashData(staged))}");
            Console.WriteLine("Stat/skill XP and perk state prepared. Inventory/equipment remain from the world save. Engine acceptance is unproven.");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or OverflowException)
        {
            Console.Error.WriteLine(e.Message); return 2;
        }
    }
}
