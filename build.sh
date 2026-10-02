#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
source scripts/common.sh
slpp_game_paths "${1:-}"
slpp_dotnet build slpp.csproj -c Release "-p:Sts2DataDir=$data_dir" "-p:BaseLibDll=$baselib"
python3 - <<'PY'
import hashlib, json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
package = Path('dist') / f"slpp-{json.loads(Path('slpp.json').read_text())['version']}.zip"
with ZipFile(package, 'w', ZIP_DEFLATED) as archive:
    for file in [Path('dist/slpp/slpp.dll'), Path('dist/slpp/slpp.json'), Path('dist/slpp/LICENSE')]:
        archive.write(file, 'slpp/' + file.name)
files = [Path('dist/slpp/slpp.dll'), package]
Path('dist/SHA256SUMS').write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to("dist")}\n' for p in files))
print(f'Package: {package}')
PY
