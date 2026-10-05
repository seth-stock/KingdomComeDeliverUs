# The installer and how it is built

Kingdom Come: Deliver Us. Unofficial; modified port of Kingdom Come: Together. Not affiliated with Warhorse Studios or Deep Silver.

## Build

`powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1` runs these gates in order and stops at the first failure:

1. `VERSION` is `Major.Minor.Patch` (the script never edits it: the user decides what it says).
2. `dotnet test dotnet\KcdUs.sln` passes.
3. `python tools\Build-QuestCatalog.py --check`: the generated quest files equal the game's data and the plan.
4. `tools\Build-Pak.py` builds the mod (deterministic: sorted entries, fixed timestamps); its sha256 is taken.
5. `tools\perf\soak-record.json` says **PASS for this version and for exactly this pak** (change the mod, run the soak again). `-SkipSoakGate` exists for a development build and says so.
6. `dotnet publish` of the agent, relay and launcher, self-contained win-x64 (a friend needs no .NET), into `release\KCDUS`.
7. Inno Setup 6 compiles `installer\KCDUS.iss` into `release\KingdomComeDeliverUs-Setup-<version>.exe`.
8. **Payload smoke:** the *published* relay is started and answers a real `InfoRequest` with the right release; the published agent starts.

Then it assembles `release\send-to-friends\` (the installer, `READ-ME-FIRST.txt`, `SHA256.txt`) and `KingdomComeDeliverUs-<version>-for-friends.zip`.
It never runs the installer and never touches the game folder.

## What the installer does

| Step | Detail |
|---|---|
| Find the game | Steam's registry key, then `steamapps\libraryfolders.vdf` (every library) for `steamapps\common\KingdomComeDeliverance`; shows the folder, lets the player correct it, refuses one without `Bin\Win64\KingdomCome.exe` and `Data\` |
| Deploy the mod | exactly `Mods\kcdus\mod.manifest`, `mod.cfg`, `Data\kcdus.pak` (never the Lua sources: the game mounts only paks) |
| Install the programs | `%LocalAppData%\KCDUS` (no administrator rights): launcher, agent, relay, the .NET runtime beside them, the guide and licence files |
| First-run settings | writes `kcdus-agent.json` with the game folder |
| Firewall (a task, ticked by default) | runs `Harden-Firewall.ps1 -Add` elevated: blocks inbound TCP 4600 from every IPv4 address except 127.x. One administrator prompt; the mod works without it |
| Shortcuts | Start menu, optional desktop |
| Uninstall | removes the programs; asks whether to remove the mod from the game folder (the three files); never touches saves. The firewall rule stays (`Harden-Firewall.ps1 -Remove` deletes it) |

It does not edit any file of the game, the Steam configuration or any save.

## Silent and test installs

`KingdomComeDeliverUs-Setup-0.1.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="<install dir>" /GAMEDIR="<game dir>" /TASKS=""` installs without any page
(the game folder page is skipped for a valid `/GAMEDIR`). `/TASKS=""` leaves out the desktop icon and the firewall rule.

## Test record

* **The build** (this script): all gates above passed on the day of the release (see the end of the build output in the release notes' commit).
* **A silent install against a stand-in game folder** (a temp folder with `Bin\Win64\KingdomCome.exe` and `Data\`) into a temp install folder, then its uninstaller: see the result in
  `docs/SOAK.md`'s sibling section "Installer test" below. It did not touch the real game or saves.
* **Not seen:** the interactive wizard (the pages, the correction of a wrong folder, the elevated firewall step) on another machine.

### Installer test

(filled in by the build session; see the end of this file)
