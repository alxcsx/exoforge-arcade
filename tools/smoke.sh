#!/usr/bin/env bash
# Runs every sample's demo script against the cluster in Exoforge/exoforge.json.
#   tools/smoke.sh            all samples
#   tools/smoke.sh economy    one sample
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

samples=("${@:-economy dungeon achievements doodle menu}")
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
