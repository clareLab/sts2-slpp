#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
game_dir="${1:-${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}}"
[[ -d "$game_dir" ]] || { echo 'Game directory does not exist' >&2; exit 1; }
[[ -f dist/slpp/slpp.dll ]] || { echo 'Run ./build.sh first' >&2; exit 1; }
if pgrep -x SlayTheSpire2 >/dev/null; then echo 'Close the game before installing the mod.' >&2; exit 1; fi
destination="$game_dir/mods/slpp"
if [[ -d "$destination" ]]; then
  backup="backups/slpp-$(date +%Y%m%d-%H%M%S)"
  mkdir -p backups
  cp -a "$destination" "$backup"
  echo "Previous version backup: $backup"
fi
mkdir -p "$destination"
cp dist/slpp/slpp.dll dist/slpp/slpp.json "$destination/"
echo "Installed: $destination"
echo 'Enable BaseLib and Save & Load ++ in the game Mod menu.'
