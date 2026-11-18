#!/usr/bin/env bash
# Runs the Unity Editor headless on one arcade project, one caller at a time.
#   tools/unity.sh <project>                 import + compile, exit non-zero on compiler errors
#   tools/unity.sh <project> Class.Method    also run a static editor method (e.g. ArcadeSetup.SetUpAll)
# <project> is a folder at the repo root: 2d-hub, 2d-echo-dungeon, 2d-doodle-dash.
set -uo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
if [ $# -lt 1 ] || [ ! -d "$repo/${1%/}/ProjectSettings" ]; then
  echo "usage: tools/unity.sh <project> [Class.Method]   (project: $(cd "$repo" && ls -d */ProjectSettings | cut -d/ -f1 | tr '\n' ' '))" >&2
  exit 2
fi
root="$repo/${1%/}"
unity="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity}"
# One lock for every project: Unity runs are slow and heavy, so they go one at a time.
lock="$repo/.unity-run.lock"

until mkdir "$lock" 2>/dev/null; do
  echo "[unity.sh] waiting for $(cat "$lock/owner" 2>/dev/null || echo another run)…"
  sleep 5
done
echo "${HC_TERMINAL_ID:-$$}" > "$lock/owner"
trap 'rm -rf "$lock"' EXIT

mkdir -p "$root/Logs"
log="$root/Logs/unity-$(date +%Y%m%d-%H%M%S)-$$.log"
args=(-batchmode -nographics -quit -projectPath "$root" -logFile "$log")
if [ $# -gt 1 ]; then args+=(-executeMethod "$2"); fi

"$unity" "${args[@]}"
status=$?

grep -E "error CS|Compilation failed|Scripts have compiler errors|\[Arcade\]|Exception:" "$log" | head -40
echo "[unity.sh] exit $status — full log: $log"
exit $status
