#!/usr/bin/env bash
# Echo Dungeon demo: a player token is refused, the game server's service token reports, progress shows up.
#   tools/smoke/dungeon.sh        or     source it and call `dungeon`
set -euo pipefail

dungeon() {
  local root http player_token server_token player_id refused progress
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  http="${EXO_HTTP:-http://localhost:4001}"

  post() { curl -sS -X POST "$http/api/$1" -H "authorization: Bearer $2" -H 'content-type: application/json' -d "$3"; echo; }
  login() { curl -sS -X POST "$http/api/auth/anonymous" -H 'content-type: application/json' -d "{\"name\":\"$1\",\"disposable\":true}" | python3 -I -c 'import json,sys; d=json.load(sys.stdin)["data"]; print(d["token"], d.get("player_id",""))'; }
  fail() { echo "smoke failed: $*" >&2; exit 1; }
  # ok <label> <response>: the call must have succeeded, or the smoke stops with the server's answer.
  ok() {
    python3 -I -c '
import json, sys
label, raw = sys.argv[1], sys.argv[2]
try:
    d = json.loads(raw)
except ValueError:
    sys.exit("smoke failed: " + label + ": not JSON: " + raw[:300])
if d.get("status") != "ok":
    sys.exit("smoke failed: " + label + ": " + raw[:300])
' "$1" "$2"
  }
  report() { # <action> <payload>: a call the game server makes with its service token.
    local response
    response="$(post "adventure_world/$1" "$server_token" "$2")"
    ok "adventure_world.$1 as the game server" "$response"
  }

  read -r player_token player_id <<<"$(login "dungeon-smoke-$RANDOM")"

  echo "== a player reporting its own kill is refused"
  refused="$(post adventure_world/report_kill "$player_token" "{\"player_id\":\"$player_id\",\"name\":\"cheater\",\"enemy\":\"slime\",\"zone\":\"Mossy Hall\"}")"
  echo "$refused"
  python3 -I -c '
import json, sys
raw = sys.argv[1]
try:
    err = json.loads(raw).get("error")
    error = err.get("code") if isinstance(err,dict) else err
except ValueError:
    error = None
if error != "forbidden_scope":
    sys.exit("smoke failed: a player token was not refused with forbidden_scope: " + raw[:300])
' "$refused"

  echo "== a forged token is refused with 401 unauthenticated"
  forged="$(curl -sS -o /dev/stdout -w '\n%{http_code}' -X POST "$http/api/adventure_world/get_progress" \
    -H 'authorization: Bearer exo_ses_forged.not-a-secret' -H 'content-type: application/json' -d '{}')"
  python3 -I -c '
import json, sys
body, status = sys.argv[1].rsplit("\n", 1)
code = (json.loads(body).get("error") or {}).get("code")
if status != "401" or code != "unauthenticated":
    sys.exit("smoke failed: a forged token got " + status + " " + body[:300])
' "$forged"

  server_token="$(cat "$root/${SMOKE_PROJECT:-2d-hub}/Exoforge/mock-game-server.token" 2>/dev/null || true)"
  if [ -z "$server_token" ]; then
    cat >&2 <<'MSG'
smoke failed: no <project>/Exoforge/mock-game-server.token, so the game server cannot report.
Create a service account once (needs admin approval), then save the token it issues in that file (it is git-ignored):
  ./exo user create mock-game-server@example.com --password '<secret>' --name 'Mock Game Server' --role service
  ./exo user token <user_id>
MSG
    return 1
  fi

  echo "== the game server reports three kills, a chest and a zone"
  for _ in 1 2 3; do
    report report_kill "{\"player_id\":\"$player_id\",\"name\":\"smoke\",\"enemy\":\"slime\",\"zone\":\"Slime Pit\"}"
  done
  report report_chest "{\"player_id\":\"$player_id\",\"name\":\"smoke\",\"zone\":\"Treasury\",\"gold\":30}"
  report report_zone "{\"player_id\":\"$player_id\",\"name\":\"smoke\",\"zone\":\"Treasury\"}"

  echo "== the player reads its progress (expect kills 3, chests 1, zones 1)"
  progress="$(post adventure_world/get_progress "$player_token" '{}')"
  echo "$progress"
  python3 -I -c '
import json, sys
raw, player_id = sys.argv[1], sys.argv[2]
try:
    d = json.loads(raw)
except ValueError:
    sys.exit("smoke failed: get_progress: not JSON: " + raw[:300])
if d.get("status") != "ok":
    sys.exit("smoke failed: get_progress: " + raw[:300])
p = d["data"]
want = {"kills": 3, "chests": 1, "zones": 1}
wrong = {k: (p.get(k), v) for k, v in want.items() if p.get(k) != v}
if wrong:
    sys.exit("smoke failed: progress " + json.dumps(p) + ", got and expected " + json.dumps(wrong))
if p.get("id") != player_id:
    sys.exit("smoke failed: progress id " + repr(p.get("id")) + " is not the player " + repr(player_id))
print("ok: %s kills, %s chests, %s zones for %s" % (p["kills"], p["chests"], p["zones"], player_id))
' "$progress" "$player_id"
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then dungeon; fi
