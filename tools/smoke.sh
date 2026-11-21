#!/usr/bin/env bash
# Runs every sample's demo script against the running cluster. The ./exo wrapper and the game-server
# token come from one workspace project (SMOKE_PROJECT, default 2d-hub: <project>/Exoforge).
#   tools/smoke.sh            all samples
#   tools/smoke.sh economy    one sample
#   tools/smoke.sh --reset [sample...]   first wipe the instance's extensions and push only that sample's
#                                        (`exo instance reset --yes`, then `exo extension push --all`, which skips
#                                        the project's "excluded_extensions")
# With --reset, achievements runs in 2d-echo-dungeon: it includes adventure_world, the only source of the events
# smoke/achievements.sh sends (kills and chests). Events from extensions that project excludes (e.g. trade_made
# from brainrot_market) never fire there, and the script does not check them.
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

reset=0
forced_project="${SMOKE_PROJECT:-}"
if [ "${1:-}" = "--reset" ]; then reset=1; shift; fi
samples=("${@:-economy dungeon achievements doodle menu shop}")
failed=()

# Unity project (its Exoforge/exoforge.json "excluded_extensions" decide what is deployed) that owns each sample.
project_of() {
  case "$1" in
    economy|menu) echo 2d-hub ;;
    dungeon|achievements) echo 2d-echo-dungeon ;;
    doodle) echo 2d-doodle-dash ;;
    shop) echo 2d-shop ;;
    *) echo "${SMOKE_PROJECT:-2d-hub}" ;;
  esac
}

for s in ${samples[@]}; do
  echo "=== $s"
  if [ "$reset" = 1 ]; then
    p="${forced_project:-$(project_of "$s")}"
    if ! (cd "$p" && ./exo instance reset --yes --dir Exoforge && ./exo extension push --all --dir Exoforge); then
      echo "=== $s: reset/push FAILED"; failed+=("$s"); continue
    fi
    export SMOKE_PROJECT="$p"
  fi
  if bash "tools/smoke/$s.sh"; then echo "=== $s: ok"; else echo "=== $s: FAILED"; failed+=("$s"); fi
done

if [ ${#failed[@]} -gt 0 ]; then
  echo "failed: ${failed[*]}"
  exit 1
fi
echo "all samples ok"
