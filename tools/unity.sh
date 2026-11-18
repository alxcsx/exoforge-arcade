#!/usr/bin/env bash
# Runs the Unity Editor headless on this project, one caller at a time.
#   tools/unity.sh                 import + compile, exit non-zero on compiler errors
#   tools/unity.sh Class.Method    also run a static editor method (e.g. ArcadeSetup.SetUpAll)
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
unity="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity}"
lock="$root/Temp-unity.lock"

until mkdir "$lock" 2>/dev/null; do
  echo "[unity.sh] waiting for $(cat "$lock/owner" 2>/dev/null || echo another run)…"
  sleep 5
done
echo "${HC_TERMINAL_ID:-$$}" > "$lock/owner"
trap 'rm -rf "$lock"' EXIT

mkdir -p "$root/Logs"
log="$root/Logs/unity-$(date +%Y%m%d-%H%M%S)-$$.log"
args=(-batchmode -nographics -quit -projectPath "$root" -logFile "$log")
if [ $# -gt 0 ]; then args+=(-executeMethod "$1"); fi

"$unity" "${args[@]}"
status=$?

grep -E "error CS|Compilation failed|Scripts have compiler errors|\[Arcade\]|Exception:" "$log" | head -40
echo "[unity.sh] exit $status — full log: $log"
exit $status
