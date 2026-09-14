#!/usr/bin/env bash
# Temporary shim: .github/workflows/build.yml still runs this file with bash.
# The implementation lives in start-cosmos-emulator.ps1. Delete this file once the workflow
# calls the .ps1 directly (shell: pwsh).
set -euo pipefail

exec pwsh -NoProfile -File "$(dirname "${BASH_SOURCE[0]}")/start-cosmos-emulator.ps1"
