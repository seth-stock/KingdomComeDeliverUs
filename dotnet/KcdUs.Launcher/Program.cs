// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdUs.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // one launcher at a time
        using var mutex = new Mutex(true, @"Local\KcdUsLauncher", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
