// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using MoonSharp.Interpreter;

namespace KcdUs.Tests;

/// <summary>
/// The mod's real Lua files run under MoonSharp against a stubbed engine (LuaStubs.lua). The game's own Lua is 5.1 and
/// MoonSharp is 5.2: the mod keeps to the part both share (no goto, no integer division, no bit operators).
/// </summary>
internal sealed class LuaMod
{
    public readonly Script L = new(CoreModules.Preset_Default);
    public readonly string ModDir = FindModDir();

    public LuaMod(bool boot = true)
    {
        L.Globals["__loadMod"] = (Func<string, bool>)LoadMod;
        L.DoString(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LuaStubs.lua")), codeFriendlyName: "LuaStubs");
        if (boot) Boot();
    }

    public void Boot() => LoadMod("Scripts/Mods/kcdus.lua");

    private bool LoadMod(string path)
    {
        // Scripts/Mods/kcdus.lua -> <mod>/kcdus.lua ; Scripts/Mods/kcdus/x.lua -> <mod>/x.lua
        string file = path.StartsWith("Scripts/Mods/kcdus/") ? path["Scripts/Mods/kcdus/".Length..] : Path.GetFileName(path);
        string full = Path.Combine(ModDir, file);
        if (!File.Exists(full)) throw new FileNotFoundException(path);
        L.DoString(File.ReadAllText(full), codeFriendlyName: file);
        return true;
    }

    private static string FindModDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var p = Path.Combine(d.FullName, "mod", "kcdus", "lua");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("mod/kcdus/lua not found above " + AppContext.BaseDirectory);
    }

    public DynValue Do(string lua) => L.DoString(lua);
    public double Num(string lua) => L.DoString("return " + lua).CastToNumber() ?? double.NaN;
    public string Str(string lua) => L.DoString("return tostring(" + lua + ")").String;
    public bool Bool(string lua) => L.DoString("return " + lua).CastToBool();

    public void Advance(double seconds) => L.DoString($"__advance({seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)})");

    /// <summary>Everything the mod wrote to the log so far.</summary>
    public List<string> Log()
    {
        var t = L.DoString("return __log").Table;
        return t.Values.Select(v => v.String).ToList();
    }

    public List<string> Lines(string prefix) => Log().Where(l => l.StartsWith(prefix)).ToList();

    public void ClearLog() => L.DoString("__log = {}");

    /// <summary>What the agent does: one console line, kcdus "<line>".</summary>
    public void Send(string line) => L.DoString($"KCDUS_In([==[{line}]==])");

    public void InWorld(bool on = true) => L.DoString($"__world.started = {(on ? "true" : "false")}");
}
