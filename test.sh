#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
project_dir="$PWD"
game_dir="${1:-${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}}"
sandbox_dir="${SLPP_TEST_DIR:-$project_dir/../../work/validation-sandbox}"
mkdir -p "$sandbox_dir" validation
sandbox_dir="$(cd "$sandbox_dir" && pwd)"
./build.sh "$game_dir"
python3 - "$game_dir" "$sandbox_dir" "$project_dir" <<'PY'
import json, shutil, sys
from pathlib import Path
source, sandbox, project = map(Path, sys.argv[1:])
game = sandbox / 'game'
game.mkdir(exist_ok=True)
for item in source.iterdir():
    dest = game / item.name
    if item.name in ('mods', 'steam_appid.txt') or dest.exists(): continue
    if item.name == 'SlayTheSpire2': shutil.copy2(item, dest)
    else: dest.symlink_to(item.resolve())
shutil.copytree(project / 'dist/slpp', game / 'mods/slpp', dirs_exist_ok=True)
base = next((source.parent.parent / 'workshop/content/2868840').glob('*/BaseLib/BaseLib.dll')).parent
shutil.copytree(base, game / 'mods/BaseLib', dirs_exist_ok=True)
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
for suite in "${suites[@]}"; do
  args=(--headless --audio-driver Dummy --force-steam=off --slpp-selftest)
  [[ "$suite" == full ]] || args+=(--slpp-suite="$suite")
  echo "Running isolated test: $suite (muted)"
  result=0
  XDG_DATA_HOME="$sandbox_dir/userdata" "${runner[@]}" "$sandbox_dir/game/SlayTheSpire2" "${args[@]}" > "validation/$suite.log" 2>&1 || result=$?
  cp "$sandbox_dir/userdata/SlayTheSpire2/slpp-selftest.json" "validation/$suite.json"
  [[ "$result" == 0 ]] || { echo "Test failed. See validation/$suite.log" >&2; exit "$result"; }
  python3 - "validation/$suite.json" <<'PY'
import json, sys
r=json.load(open(sys.argv[1]))
assert r['success'], r['error']
print(f"PASS {len(r['passed'])} checks, game {r['gameBuild']}")
PY
done
