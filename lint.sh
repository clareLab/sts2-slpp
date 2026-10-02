#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
source scripts/common.sh
slpp_dotnet format whitespace . --folder --include ./*.cs tests/*.cs --verify-no-changes
slpp_dotnet build tests/TimelineTests.csproj -c Release
if command -v shellcheck >/dev/null; then shellcheck -x ./*.sh scripts/*.sh
elif command -v nix >/dev/null; then nix shell nixpkgs#shellcheck -c shellcheck -x ./*.sh scripts/*.sh
else echo 'ShellCheck is required.' >&2; exit 1; fi
git diff --check
