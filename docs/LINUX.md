# Kingdom Come: Deliver Us on Linux (Steam + Proton)

**Status: experimental, and not yet run under a real Proton by the people who wrote it.** Read
[What is and is not tested](#what-is-and-is-not-tested) first.

## Can it work on Linux?

Yes, and for the first game it is the simpler of the two mods: nothing is injected into the game.

* The mod is a **pak** the game loads from `Mods/kcdus/` itself (Proton runs the game as it always does).
* The agent talks to the game over the engine's **remote console, TCP `127.0.0.1:4600`**, and reads **`kcd.log`**.
  A Wine socket is a real host socket, so a native Linux program connects to it; `kcd.log` is an ordinary file in the game folder.
* The agent and the relay are **native Linux programs** (.NET 8, self-contained: the player needs no .NET). The host's agent
  serves the relay in-process.
* The Windows launcher (WinForms) and the Inno installer are replaced by one shell script, `kcdus`.

The protocol is unchanged: a Linux player and a Windows player can play together on the same version.

### F11 / F12

Windows reads these with `GetAsyncKeyState`, which does not exist here. On Linux the agent reads **F11 and F12 only** from the kernel's
input devices (`/dev/input/event*`). That needs your user to be in the **`input` group** (log out and in again after adding yourself).
If no device is readable, the agent says so in its log and `kcdus doctor` tells you; the same two answers are one command away:

```bash
./kcdus join-story     # = F11: join the host for this stretch of the story
./kcdus stay-story     # = F12: stay in the open world
```

(Both answer "ok:false" when no question is pending.)

## Install and play

Needs Steam with **Kingdom Come: Deliverance** (the first game) installed; it runs under Proton as usual.

```bash
tar xzf KingdomComeDeliverUs-Linux-*.tar.gz && cd KingdomComeDeliverUs-Linux-*
./kcdus doctor            # what is missing, in plain words
./kcdus install           # with the game closed: copies mod.manifest, mod.cfg and kcdus.pak into <game>/Mods/kcdus
./kcdus play              # asks Steam to start the game; pick your own save as usual
./kcdus host Henry        # you host: the agent serves the relay (TCP 7788)
./kcdus join 203.0.113.9:7788 Hans   # or: you join a friend
./kcdus say hello         # a chat line;  ./kcdus status  shows what the agent sees
./kcdus uninstall         # removes the mod folder (your saves are never touched)
```

Everyone loads **their own save**; the mod shows each other as presence, keeps the clocks together (forward only), and handles the
join-or-stay question on scripted stretches. See [PLAYING-TOGETHER.md](PLAYING-TOGETHER.md) and [KNOWN-LIMITS.md](KNOWN-LIMITS.md).

### Security: keep TCP 4600 closed

The engine's remote console **has no password and listens on every address**. The Windows installer adds a firewall block for it.
Nothing here can add a firewall rule for you (the script never runs `sudo`); `./kcdus harden` prints the commands for ufw, firewalld and
iptables, and `kcdus install` prints them too. Open only the relay port (7788), and only on the host.

## What is and is not tested

Tested (a Windows 11 PC and Ubuntu 24.04 under WSL2):

| What | How | Result |
|---|---|---|
| The whole .NET suite on real Linux | Ubuntu 24.04, .NET 8: 115 tests | pass |
| The Linux key reader | decoding of real `input_event` bytes, press/hold/release, the shared edge logic | pass (synthetic bytes) |
| `kcdus` + the package | a fake Steam tree and fake `steam`; `doctor`, `install`, `play` (the `-applaunch 379430` call is checked), `host`, `join`, `say`, `status`, `join-story`; the **real linux-x64 agent**, host and guest on one machine, connected through the relay, chat delivered to a stub of the remote console | works, against stand-ins |
| The mod itself, on Windows | the same pak: soak, one machine with a bot (see SOAK.md) | as in the 0.1.0 report |

**Not tested, because nobody involved had it:**

* **The game under Proton** with this mod: whether `Mods/kcdus` is loaded, whether the remote console opens under Wine (it
  should: it is a plain socket the engine opens itself), whether `kcd.log` is written where the agent looks.
* **Reading `/dev/input`** for real: the decoding is tested; a real keyboard was not.
* Two real players, on any OS (same gap as the Windows 0.1.0 build).
* Flatpak Steam and non-default library folders other than through `libraryfolders.vdf`.

## Troubleshooting

* `./kcdus doctor` first.
* **"the game is running but the co-op mod did not answer"**: the mod is not installed, or the game has not reached a loaded save yet.
* **Status shows `gameConsole: false`**: the remote console is not answering on 4600. Check `Mods/kcdus/mod.cfg` contains
  `log_EnableRemoteConsole = 1` and that Proton's game process is running (`kcdus doctor`).
* **Another program uses 1415 or 7788**: `KCDUS_STATUS_PORT` / `KCDUS_RELAY_PORT`.

## For developers

* Build the package on Linux/WSL: `linux/Build-LinuxPackage.sh` (needs the .NET 8 SDK and python3). It publishes the agent
  `linux-x64` self-contained, builds the pak with `tools/Build-Pak.py`, and writes `release/KingdomComeDeliverUs-Linux-<version>.tar.gz` + `.sha256`.
* New code: `dotnet/KcdUs.Agent/LinuxKeys.cs`, the Linux paths in `AgentConfig.cs` (`GameLocator`), `linux/kcdus`, `dotnet/KcdUs.Tests/LinuxTests.cs`.
* The version string is the repository's `VERSION`; it was not changed for this.
