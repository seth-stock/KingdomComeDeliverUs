// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdUs.Agent;

/// <summary>The keys that answer the host's "join or stay" question, as the Multiplayer tab's Keys page offers them.</summary>
public static class KeyPreset
{
    public const string Default = "f11f12";

    /// <summary>(join key, stay key) as virtual-key codes; 0 = no key. An unknown preset is the default.</summary>
    public static (int Join, int Stay) Parse(string? preset) => (preset ?? "").Trim().ToLowerInvariant() switch
    {
        "f9f10" => (0x78, 0x79),
        "off" => (0, 0),
        _ => (0x7A, 0x7B),
    };

    public static bool IsKnown(string? preset) => (preset ?? "").Trim().ToLowerInvariant() is "f11f12" or "f9f10" or "off";

    public static string Name(int vk) => vk is >= 0x70 and <= 0x7B ? "F" + (vk - 0x70 + 1) : "none";

    /// <summary>"Join F11, stay F12", for the player's eyes.</summary>
    public static string Describe(string? preset)
    {
        var (j, s) = Parse(preset);
        return j == 0 ? "no keys: answer from the menu or the console" : $"join {Name(j)}, stay {Name(s)}";
    }
}
