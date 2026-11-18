#!/usr/bin/env bash
# Achievements demo: world events from Echo Dungeon unlock achievements once per player, and
# achievements.list shows each caller only its own progress. Every step asserts; wrong = exit 1.
#   tools/smoke/achievements.sh        or     source it and call `achievements`
set -euo pipefail

achievements() {
  local root http player_token player_id other_token other_id server_token
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  http="${EXO_HTTP:-http://localhost:4001}"

  exo() { (cd "$root/${SMOKE_PROJECT:-2d-hub}" && ./exo "$@" --dir Exoforge); }
  fail() { echo "FAIL: $*" >&2; return 1; }
  post() { curl -fsS -X POST "$http/api/$1" -H "authorization: Bearer $2" -H 'content-type: application/json' -d "$3"; }
  login() { curl -fsS -X POST "$http/api/auth/anonymous" -H 'content-type: application/json' -d "{\"name\":\"$1\",\"disposable\":true}" | python3 -I -c 'import json,sys; d=json.load(sys.stdin)["data"]; print(d["token"], d.get("player_id",""))'; }

  # "<defs>|<unlocked ids, sorted>|<slime_slayer progress>/<needed>" as the calling player sees it.
  status() { post achievements/list "$1" '{}' | python3 -I -c '
import json, sys
rows = json.load(sys.stdin)["data"]
unlocked = ",".join(sorted(r["id"] for r in rows if r["unlocked"]))
slime = next(r for r in rows if r["id"] == "slime_slayer")
print("{}|{}|{}/{}".format(len(rows), unlocked, slime["progress"], slime["needed"]))'; }

  # unlock rows for one player/achievement, read straight from the table: exactly-once proof.
  unlock_rows() { exo resource list unlocks --limit 500 | python3 -I -c '
import json, sys
text = sys.stdin.read()
rows = json.loads(text[text.find("{"):])["rows"]
print(sum(1 for r in rows if r["player_id"] == sys.argv[1] and r["achievement_id"] == sys.argv[2]))' "$1" "$2"; }

  # the stored trigger counter; list() saturates progress at "needed", the table keeps counting.
  stored_count() { exo resource list achievement_progress --limit 500 | python3 -I -c '
import json, sys
text = sys.stdin.read()
rows = json.loads(text[text.find("{"):])["rows"]
print(sum(r["count"] for r in rows if r["id"] == sys.argv[1] + ":" + sys.argv[2]))' "$1" "$2"; }

  kill() { post adventure_world/report_kill "$server_token" "{\"player_id\":\"$1\",\"name\":\"smoke\",\"enemy\":\"slime\",\"zone\":\"Slime Pit\"}" >/dev/null; }
  chest() { post adventure_world/report_chest "$server_token" "{\"player_id\":\"$1\",\"name\":\"smoke\",\"zone\":\"Treasury\",\"gold\":30}" >/dev/null; }

  # wait_until <what> <want> <cmd...>: events travel through the cluster bus, so poll instead of sleeping blind.
  wait_until() {
    local what="$1" want="$2" got="" i
    shift 2
    for i in $(seq 1 40); do
      got="$("$@")"
      if [ "$got" = "$want" ]; then echo "ok: $what"; return 0; fi
      sleep 0.25
    done
    fail "$what: expected '$want', got '$got'"
  }

  server_token="$(cat "$root/${SMOKE_PROJECT:-2d-hub}/Exoforge/mock-game-server.token" 2>/dev/null || true)"
  if [ -z "$server_token" ]; then
    cat >&2 <<'MSG'
FAIL: <project>/Exoforge/mock-game-server.token is missing; the game server is what reports the kills.
Create it once (needs admin approval), then save the returned token in that file (git-ignored):
  ./exo call auth issue_token --data '{"user_id":"mock-game-server","scopes":["service"]}' --json
Then: ./exo extension push adventure_world && ./exo extension push achievements
MSG
    return 1
  fi

  read -r player_token player_id <<<"$(login "achievements-smoke-$RANDOM")"
  read -r other_token other_id <<<"$(login "achievements-other-$RANDOM")"

  wait_until "definitions are seeded rows, readable by players" "5||0/3" status "$player_token"

  echo "== two kills"
  kill "$player_id"
  kill "$player_id"
  wait_until "nothing unlocked below the threshold" "5||2/3" status "$player_token"

  echo "== third kill and a chest"
  kill "$player_id"
  chest "$player_id"
  wait_until "threshold unlocks both achievements" "5|slime_slayer,treasure_hunter|3/3" status "$player_token"

  echo "== a fourth kill"
  kill "$player_id"
  wait_until "the stored counter keeps counting past the threshold" "4" stored_count "$player_id" enemy_slain
  wait_until "the displayed progress saturates at needed" "5|slime_slayer,treasure_hunter|3/3" status "$player_token"
  wait_until "the unlock is stored exactly once" "1" unlock_rows "$player_id" slime_slayer

  wait_until "another player's list is untouched" "5||0/3" status "$other_token"
  wait_until "another player has no unlock rows" "0" unlock_rows "$other_id" slime_slayer
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then achievements; fi
