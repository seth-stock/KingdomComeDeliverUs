// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Agent;

/// <summary>
/// Where this agent keeps its own files (identity, journals, logs, world list, backups). Normally LocalAppData\KCDUS. KCDUS_DATA_DIR moves all
/// of them, so two agents can run side by side on one computer (the local two-player rig: two private games, two agents, one relay).
/// </summary>
public static class AgentPaths
{
    /// <summary>The game's log file name: kcd.log, or (KCDUS_GAME_LOG) kcd(1).log for the second of two games on one computer.</summary>
    public static string GameLog
    {
        get
        {
            string? over = Environment.GetEnvironmentVariable("KCDUS_GAME_LOG");
            return over is "kcd(1).log" or "kcd(2).log" ? over : "kcd.log";
        }
    }

    public static string Data
    {
        get
        {
            string? over = Environment.GetEnvironmentVariable("KCDUS_DATA_DIR");
            return !string.IsNullOrWhiteSpace(over) && Path.IsPathRooted(over)
                ? over
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDUS");
        }
    }
}
