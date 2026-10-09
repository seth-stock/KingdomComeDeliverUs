#!/usr/bin/env bash
# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
#
# Builds release/KingdomComeDeliverUs-Linux-<version>.tar.gz (docs/LINUX.md). Run on Linux or WSL with the .NET 8 SDK and python3.
# The agent is published self-contained for linux-x64 (the relay is inside it: the host's agent serves it). The mod pak is built by
# tools/Build-Pak.py, which is platform-independent.
set -euo pipefail
REPO="$(cd "${1:-$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")/..}" && pwd)"
VERSION="$(tr -d '[:space:]' <"$REPO/VERSION")"
NAME="KingdomComeDeliverUs-Linux-$VERSION"
OUT="${OUT_DIR:-$REPO/release}"
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
PKG="$WORK/$NAME"
mkdir -p "$PKG"/{agent,mod,docs} "$OUT"

echo "== mod pak"
python3 "$REPO/tools/Build-Pak.py" --out "$WORK/mod" >/dev/null
cp -r "$WORK/mod/kcdus" "$PKG/mod/kcdus"

echo "== agent (linux-x64, self-contained)"
dotnet publish "$REPO/dotnet/KcdUs.Agent/KcdUs.Agent.csproj" -c Release -r linux-x64 --self-contained -o "$PKG/agent" -v q -nologo
chmod +x "$PKG/agent/KcdUsAgent"

echo "== launcher + docs"
cp "$REPO/linux/kcdus" "$PKG/kcdus"; chmod +x "$PKG/kcdus"
cp "$REPO/VERSION" "$REPO/LICENSE" "$REPO/NOTICE" "$REPO/AUTHORS" "$PKG/"
for d in LINUX.md PLAYING-TOGETHER.md KNOWN-LIMITS.md MENU.md SHARED-WORLDS.md FEATURE-PARITY.md CAPABILITIES.md HUMAN-ACCEPTANCE-TESTS.md SESSION2-RESULTS.md SESSION4-RESULTS.md SESSION5-RESULTS.md SESSION6-RESULTS.md WARHORSE-MODDING-EULA.txt; do cp "$REPO/docs/$d" "$PKG/docs/" 2>/dev/null || true; done
[[ -f "$PKG/docs/WARHORSE-MODDING-EULA.txt" ]] || { echo "the Warhorse modding EULA is missing from docs/: the package must carry it (EULA 4.7)" >&2; exit 1; }
cat >"$PKG/READ-ME-FIRST.txt" <<EOF
Kingdom Come: Deliver Us $VERSION -- Linux build (unofficial, community)

1. Needs: Steam with the first game, "Kingdom Come: Deliverance", installed (it runs under Proton).
2. ./kcdus doctor          shows what is missing
3. ./kcdus install         (game closed) asks you to accept Warhorse's modding EULA (docs/WARHORSE-MODDING-EULA.txt), installs the mod and
                           builds the game's "Multiplayer" menu tab from YOUR OWN game files (nothing of Warhorse's is in this package)
4. ./kcdus play            starts the game and the agent; in the game open  Multiplayer  (main menu or pause menu):
                           host, join, the game world (join a host's world, new world together, which Henry), keys, settings
5. Without the tab:  ./kcdus host   or   ./kcdus join HOST[:PORT]
6. F11 / F12 answer the host's question if your user can read /dev/input; otherwise: ./kcdus join-story | stay-story

Session 6 PLAYTEST: read docs/SESSION6-RESULTS.md and docs/HUMAN-ACCEPTANCE-TESTS.md.
No real Proton gameplay proof; Windows-only own-menu pause/outfit/character adapters are not proved on Linux.
New and not run under a real Proton by its authors: read docs/LINUX.md ("What is and is not tested").
KEEP TCP 4600 CLOSED to other machines (./kcdus harden): the engine's remote console has no password.
Not affiliated with or endorsed by Warhorse Studios or Deep Silver. GPL-3.0-only; see LICENSE and NOTICE.
EOF
( cd "$PKG" && find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum >SHA256SUMS )

tar -C "$WORK" --owner=0 --group=0 --sort=name -czf "$OUT/$NAME.tar.gz" "$NAME"
( cd "$OUT" && sha256sum "$NAME.tar.gz" | tee "$NAME.tar.gz.sha256" )
ls -l "$OUT/$NAME.tar.gz"
