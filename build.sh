#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
game_dir="${1:-${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}}"
data_dir="${STS2_DATA_DIR:-$game_dir/data_sts2_linuxbsd_x86_64}"
if [[ ! -f "$data_dir/sts2.dll" ]]; then
  echo 'sts2.dll not found. Pass the game directory or set STS2_DATA_DIR.' >&2
  exit 1
fi
if command -v dotnet >/dev/null; then sdk=(dotnet)
elif command -v nix >/dev/null; then sdk=(nix shell nixpkgs#dotnet-sdk_9 -c dotnet)
else echo '.NET SDK 9 is required.' >&2; exit 1; fi
baselib="${BASELIB_DLL:-$game_dir/mods/BaseLib/BaseLib.dll}"
if [[ ! -f "$baselib" ]]; then
  baselib="$(python3 - "$game_dir" <<'PYTHON'
import sys
from pathlib import Path
root=Path(sys.argv[1]).parent.parent / 'workshop/content/2868840'
print(next(root.glob('*/BaseLib/BaseLib.dll'), ''))
PYTHON
)"
fi
[[ -f "$baselib" ]] || { echo 'BaseLib.dll not found. Set BASELIB_DLL.' >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"${sdk[@]}" build slpp.csproj -c Release "-p:Sts2DataDir=$data_dir" "-p:BaseLibDll=$baselib"
"${sdk[@]}" run --project tests/TimelineTests.csproj -c Release
python3 - <<'PY'
import hashlib, json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
package = Path('dist') / f"slpp-{json.loads(Path('slpp.json').read_text())['version']}.zip"
with ZipFile(package, 'w', ZIP_DEFLATED) as archive:
    for file in sorted(Path('dist/slpp').iterdir()):
        archive.write(file, 'slpp/' + file.name)
files = [Path('dist/slpp/slpp.dll'), package]
Path('dist/SHA256SUMS').write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to("dist")}\n' for p in files))
print(f'Package: {package}')
PY
