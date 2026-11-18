#!/usr/bin/env bash
# Main Menu demo, asserted: news, settings, and the Claim Gift that must FAIL on the broken row
# (a zero-coin gift makes wallet.credit raise invalid_amount; game_menu maps it to wallet_rejected).
# Exits non-zero on any wrong result.
#   tools/smoke/menu.sh
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
http="${EXO_HTTP:-http://localhost:4001}"
tag="menu-smoke-$RANDOM$RANDOM"
token=""
fail=0

exo() { (cd "$root" && ./exo "$@"); }

login() {
  local body
  body=$(curl -sS -m 8 -X POST "$http/api/auth/anonymous" -H 'content-type: application/json' \
    -d "{\"name\":\"$tag\",\"disposable\":true}")
  token=$(printf '%s' "$body" | python3 -I -c 'import json,sys; print(json.load(sys.stdin)["data"]["token"])')
}

post() {
  local body="${2:-}"
  [ -n "$body" ] || body='{}'
  curl -sS -m 8 -X POST "$http/api/$1" -H "authorization: Bearer $token" \
    -H 'content-type: application/json' -d "$body"
}

# expect <name> <response body> <python condition on d/data/code/message>
# Clusters reply to a failing action with {"error":"{:code, \"message\"}"} (or ":{code}"), so code is
# the atom and message the text, parsed out for the assertions below.
expect() {
  local name="$1" body="$2" cond="$3" out
  if out=$(BODY="$body" COND="$cond" python3 -I -c '
import json, os, re
try:
    d = json.loads(os.environ["BODY"])
except Exception as e:
    raise SystemExit("unparseable response: %s" % e)
data = d.get("data")
err = d.get("error")
code, message, details = None, "", {}
if isinstance(err, dict):
    code = err.get("code")
    message = err.get("message") or ""
    details = err.get("details") or {}
elif isinstance(err, str):
    code = err
rid = d.get("rid")
if not eval(os.environ["COND"], {"json": json, "d": d, "data": data, "code": code, "message": message}):
    raise SystemExit("unexpected: " + json.dumps(d)[:500])
' 2>&1); then
    echo "ok    $name"
  else
    echo "FAIL  $name — $out"
    fail=1
  fi
}

gift_row() { exo resource update menu_gifts welcome --data "{\"coins\":$1}" --json >/dev/null; }
restore() { gift_row 0; }

login
trap restore EXIT

echo "== the gift row ships broken: zero coins"
restore

echo "== news is a player-readable resource, read with no action written for it"
expect "player lists menu_news" "$(post resource_store/list '{"resource":"menu_news"}')" \
  "data is not None and any(row.get('id') == 'welcome' and 'Welcome' in row.get('headline', '') for row in data['rows'])"

echo "== settings: default, saved per player, out of range refused"
expect "defaults to volume 80, colour-blind off" "$(post game_menu/get_settings)" \
  "data is not None and data['volume'] == 80 and data['color_blind'] is False"
expect "saves volume 40, colour-blind on" "$(post game_menu/save_settings '{"volume":40,"color_blind":true}')" \
  "data is not None and data['volume'] == 40 and data['color_blind'] is True"
expect "reads back the saved settings" "$(post game_menu/get_settings)" \
  "data is not None and data['volume'] == 40 and data['color_blind'] is True"
expect "volume 400 is refused" "$(post game_menu/save_settings '{"volume":400,"color_blind":true}')" \
  "code == 'invalid_volume'"

echo "== Claim Gift fails: wallet refuses the zero-coin row (rid for Studio's timeline)"
gift_out="$(post game_menu/claim_gift)"
expect "zero-coin gift is refused, not paid at 0" "$gift_out" \
  "code == 'wallet_rejected' and isinstance(d.get('rid'), str) and len(d['rid']) > 0"
expect "the refusal names the wallet's own error" "$gift_out" \
  "'invalid_amount' in message"

echo "== fix the row in Studio (here: the CLI), claim again — exactly once"
gift_row 100
expect "claiming the fixed gift pays 100" "$(post game_menu/claim_gift)" \
  "data is not None and data['coins'] == 100 and data['balance'] == 100"
expect "claiming again is refused" "$(post game_menu/claim_gift)" \
  "code == 'already_claimed'"
expect "the balance moved once" "$(post wallet/get_balance)" "data is not None and data['coins'] == 100"

if [ "$fail" -ne 0 ]; then
  echo "menu smoke: FAILED"
  exit 1
fi
echo "menu smoke: all ok"
