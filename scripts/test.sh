#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/common.sh
mode=game
case "${1:-}" in
  --unit|--game|--ui) mode="${1#--}"; shift ;;
  --*) echo 'Usage: ./scripts/test.sh [--unit|--game|--ui] [game-directory]' >&2; exit 2 ;;
esac
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/unit -p 'test_*.py'
slpp_dotnet run --project tests/unit/TimelineTests.csproj -c Release
[[ "$mode" == unit ]] && exit 0
project_dir="$PWD"
slpp_game_paths "${1:-}"
sandbox_dir="${SLPP_TEST_DIR:-$project_dir/artifacts/sandbox}"
mkdir -p "$sandbox_dir" artifacts/validation
sandbox_dir="$(cd "$sandbox_dir" && pwd)"
./scripts/build.sh "$game_dir"
python3 - "$game_dir" "$sandbox_dir" "$project_dir" "$baselib" <<'PY'
import json, shutil, sys
from pathlib import Path
source, sandbox, project, library = map(Path, sys.argv[1:])
marker = sandbox / '.slpp-sandbox'
if any(sandbox.iterdir()) and not marker.is_file():
    raise SystemExit('SLPP_TEST_DIR must be empty or an existing slpp sandbox.')
marker.touch()
game = sandbox / 'game'
game.mkdir(exist_ok=True)
for item in source.iterdir():
    dest = game / item.name
    if item.name in ('mods', 'steam_appid.txt') or dest.exists(): continue
    if item.name == 'SlayTheSpire2': shutil.copy2(item, dest)
    else: dest.symlink_to(item.resolve())
shutil.copytree(project / 'artifacts/dist/slpp', game / 'mods/slpp', dirs_exist_ok=True)
for name in ('BaseLib.dll', 'BaseLib.pck', 'BaseLib.json'):
    target = game / 'mods/BaseLib' / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(library.parent / name, target)
user = sandbox / 'userdata/SlayTheSpire2'
settings = user / 'default/1/settings.save'
settings.parent.mkdir(parents=True, exist_ok=True)
settings.write_text(json.dumps({'schema_version':8, 'mod_settings':{'mods_enabled':True,'mod_list':[]},
    'volume_master':0, 'skip_intro_logo':True, 'seen_ea_disclaimer':True, 'fullscreen':False, 'fps_limit':60}))
(user / '.slpp-test-sandbox').touch()
PY
runner=()
if command -v steam-run >/dev/null; then runner=(steam-run); fi
read -r -a suites <<< "${SLPP_TEST_SUITES:-full resume settings settings-resume}"
if [[ "$mode" == ui ]]; then
  command -v Xvfb >/dev/null || { echo 'Xvfb is required for UI tests.' >&2; exit 1; }
  read -r -a suites <<< "${SLPP_TEST_SUITES:-ui}"
  display_file="$sandbox_dir/display"
  Xvfb -displayfd 3 -screen 0 1280x720x24 -nolisten tcp 3> "$display_file" > artifacts/validation/display.log 2>&1 &
  display_pid=$!
  trap 'kill "$display_pid" 2>/dev/null || true; wait "$display_pid" 2>/dev/null || true' EXIT
  for ((attempt=0; attempt<100; attempt++)); do
    [[ -s "$display_file" ]] && break
    kill -0 "$display_pid" 2>/dev/null || { echo 'Xvfb failed. See artifacts/validation/display.log' >&2; exit 1; }
    sleep 0.1
  done
  [[ -s "$display_file" ]] || { echo 'Xvfb did not become ready.' >&2; exit 1; }
  DISPLAY=":$(< "$display_file")"
  export DISPLAY
fi
for suite in "${suites[@]}"; do
    case "$suite" in full|resume|settings|settings-resume|ui|world|characters|crystal|potions|choices|transitions|archive|layout) ;; *) echo "Unknown test suite: $suite" >&2; exit 2 ;; esac
  args=(--audio-driver Dummy --force-steam=off --slpp-selftest --slpp-suite="$suite")
  if [[ "$suite" == ui || "$suite" == layout ]]; then
    [[ "$mode" == ui ]] || { echo 'Use ./scripts/test.sh --ui for rendered tests.' >&2; exit 2; }
    args+=(--display-driver x11 --rendering-method gl_compatibility --rendering-driver opengl3 --windowed --resolution 1280x720)
  else args+=(--headless); fi
  echo "Running isolated test: $suite (muted)"
  result=0
  report="$sandbox_dir/userdata/SlayTheSpire2/slpp-selftest.json"
  rm -f "$report" "artifacts/validation/$suite.json"
  XDG_DATA_HOME="$sandbox_dir/userdata" timeout --kill-after=10 "${SLPP_TEST_TIMEOUT:-300}" "${runner[@]}" "$sandbox_dir/game/SlayTheSpire2" "${args[@]}" > "artifacts/validation/$suite.log" 2>&1 || result=$?
  [[ "$result" == 0 ]] || { echo "Test failed. See artifacts/validation/$suite.log" >&2; exit "$result"; }
  [[ -f "$report" ]] || { echo "Missing test report. See artifacts/validation/$suite.log" >&2; exit 1; }
  cp "$report" "artifacts/validation/$suite.json"
  python3 - "artifacts/validation/$suite.json" <<'PY'
import json, sys
r=json.load(open(sys.argv[1]))
assert r['success'], r['error']
print(f"PASS {len(r['passed'])} checks, game {r['gameBuild']}")
PY
  if [[ "$suite" == ui || "$suite" == layout ]]; then
    if [[ "$suite" == ui ]]; then
      cp "$sandbox_dir/userdata/SlayTheSpire2/slpp-settings.png" artifacts/validation/settings.png
      cp "$sandbox_dir/userdata/SlayTheSpire2/slpp-shortcuts.png" artifacts/validation/shortcuts.png
    fi
    cp "$sandbox_dir/userdata/SlayTheSpire2/slpp-ui.png" artifacts/validation/interface.png
    cp "$sandbox_dir/userdata/SlayTheSpire2/slpp-toolbar.png" artifacts/validation/toolbar.png
    cp "$sandbox_dir/userdata/SlayTheSpire2/slpp-help.png" artifacts/validation/shortcuts-help.png
  fi
done
