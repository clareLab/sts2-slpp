#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/common.sh
slpp_game_paths "${1:-}"
slpp_dotnet build src/slpp.csproj -c Release "-p:Sts2DataDir=$data_dir" "-p:BaseLibDll=$baselib"
python3 - <<'PY'
import hashlib, json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
package = Path('artifacts/dist') / f"slpp-{json.loads(Path('src/slpp.json').read_text())['version']}.zip"
with ZipFile(package, 'w', ZIP_DEFLATED) as archive:
    for file in [Path('artifacts/dist/slpp/slpp.dll'), Path('artifacts/dist/slpp/slpp.json'), Path('artifacts/dist/slpp/LICENSE')]:
        archive.write(file, 'slpp/' + file.name)
files = [Path('artifacts/dist/slpp/slpp.dll'), package]
Path('artifacts/dist/SHA256SUMS').write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to("artifacts/dist")}\n' for p in files))
print(f'Package: {package}')
PY
