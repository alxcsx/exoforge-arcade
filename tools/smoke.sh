#!/usr/bin/env bash
# Runs every sample's demo script against the running cluster. The ./exo wrapper and the game-server
# token come from one workspace project (SMOKE_PROJECT, default 2d-hub: <project>/Exoforge).
#   tools/smoke.sh            all samples
#   tools/smoke.sh economy    one sample
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

samples=("${@:-economy dungeon achievements doodle menu shop}")
failed=()

for s in ${samples[@]}; do
  echo "=== $s"
  if bash "tools/smoke/$s.sh"; then echo "=== $s: ok"; else echo "=== $s: FAILED"; failed+=("$s"); fi
done

if [ ${#failed[@]} -gt 0 ]; then
  echo "failed: ${failed[*]}"
  exit 1
fi
echo "all samples ok"
