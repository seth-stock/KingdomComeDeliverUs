// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;

namespace KcdUs.Wire;

/// <summary>Text that crosses a wire or a game console: nothing in it can be read as a separator, a quote or a control.</summary>
public static class Safe
{
    /// <summary>The same rule as KCDUS.clean in the game's Lua: control characters, quote, backslash, '|' and '~' become a space.</summary>
    public static string Clean(string? s, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, maxLength));
        foreach (var ch in s)
        {
            if (sb.Length >= maxLength) break;
            sb.Append(char.IsControl(ch) || ch is '"' or '\\' or '|' or '~' ? ' ' : ch);
        }
        return sb.ToString().Trim();
    }

    public static string Name(string? s) => Clean(s, 24) is { Length: > 0 } n ? n : "Henry";
}

/// <summary>Where a player is and what they are doing: what one 10 Hz sample carries.</summary>
public sealed record PlayerState(
    double X, double Y, double Z, double Yaw,
    double Vx, double Vy, double Vz,
    int Flags, int Health, int Stamina, string Anim, double WorldTime)
{
    public const int FlagWeaponDrawn = 1, FlagMounted = 2, FlagDialog = 4, FlagDead = 8, FlagDanger = 16;

    public bool WeaponDrawn => (Flags & FlagWeaponDrawn) != 0;
    public bool Mounted => (Flags & FlagMounted) != 0;
    public bool InDialog => (Flags & FlagDialog) != 0;
    public bool Dead => (Flags & FlagDead) != 0;
    public bool InDanger => (Flags & FlagDanger) != 0;

    private static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

    /// <summary>x,y,z|yaw|vx,vy,vz|flags|hp|stam|anim|worldTime  (the game's ST record without its first two fields).</summary>
    public string Encode() =>
        $"{F(X, "0.00")},{F(Y, "0.00")},{F(Z, "0.00")}|{F(Yaw, "0.000")}|{F(Vx, "0.00")},{F(Vy, "0.00")},{F(Vz, "0.00")}|{Flags}|{Health}|{Stamina}|{Safe.Clean(Anim, 40)}|{F(WorldTime, "0")}";

    public static PlayerState? TryDecode(string[] f, int start = 0)
    {
        if (f.Length < start + 7) return null;
        if (!Vec(f[start], out var p) || !Vec(f[start + 2], out var v)) return null;
        if (!D(f[start + 1], out var yaw) || !I(f[start + 3], out var flags) || !I(f[start + 4], out var hp) || !I(f[start + 5], out var st))
            return null;
        D(f.Length > start + 7 ? f[start + 7] : "0", out var wt);
        return new PlayerState(p[0], p[1], p[2], yaw, v[0], v[1], v[2], flags, hp, st, f[start + 6], wt);
    }

    private static bool D(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    private static bool I(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    private static bool Vec(string s, out double[] v)
    {
        v = new double[3];
        var p = s.Split(',');
        return p.Length == 3 && D(p[0], out v[0]) && D(p[1], out v[1]) && D(p[2], out v[2]);
    }
}

/// <summary>The build string two machines must share exactly (Major.Minor.Patch; anything after is ignored).</summary>
public static class Release
{
    public static string Current => typeof(Release).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "0.0.0";
        var core = s.Trim().Split('+', '-')[0];
        var parts = core.Split('.');
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out _)) return core;
        return string.Join('.', parts.Take(3));
    }

    public static bool Same(string? a, string? b) => Normalize(a) == Normalize(b);
}
