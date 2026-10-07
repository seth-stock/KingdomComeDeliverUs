// SPDX-License-Identifier: GPL-3.0-only
using KcdUs.Agent;
using System.Text.RegularExpressions;
var root=Path.GetFullPath(args[0]);var parent=Directory.GetParent(root)!;
if(Path.GetFileName(root)!="game-root" || !Regex.IsMatch(parent.Name,@"^KCDUS-engine-[0-9a-f]{32}$")
    || Path.GetFullPath(Environment.GetEnvironmentVariable("KCDUS_PROBE_SAVE_ROOT")??"")!=Path.Combine(parent.FullName,"saved-games"))
    throw new InvalidOperationException("Only the harness-owned disposable profile is allowed.");
Console.WriteLine(EngineGameStart.StartWithModules(root,new[]{args[1],args[2]},true));
