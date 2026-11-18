#!/usr/bin/env bash
# Brainrot Exchange demo against the running cluster.
# Every check asserts; the script exits non-zero on any wrong result.
#   tools/smoke/economy.sh            all sections
#   tools/smoke/economy.sh market     one section
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

http="${EXO_HTTP:-http://localhost:4001}"
secret="arcade-demo-secret" # sample-only, publicly known
player=""
token=""

fail() { echo "FAIL  $*" >&2; exit 1; }

# login creates a fresh disposable player and keeps its token in $token/$player.
login() {
  player="econ-smoke-$(date +%s)-$RANDOM"
  local body
  body=$(curl -sS -m 8 -X POST "$http/api/auth/anonymous" -H 'content-type: application/json' \
    -d "{\"player_id\":\"$player\",\"device_id\":\"$player\",\"disposable\":true}") \
    || fail "login: cluster unreachable at $http"
  token=$(printf '%s' "$body" | python3 -I -c '
import json, sys
raw = sys.stdin.read()
try: d = json.loads(raw)
except ValueError: sys.exit("login: not JSON: " + raw[:300])
if d.get("status") != "ok": sys.exit("login: " + raw[:300])
print(d["data"]["token"])
') || fail "login: $body"
}

# player_call <service> <action> <json> sends the signed-in player's token.
player_call() {
  curl -sS -m 8 -X POST "$http/api/$1/$2" -H "authorization: Bearer $token" \
    -H 'content-type: application/json' -d "$3"
}

# ok_json <label> <response> <jq filter> asserts the whole response.
ok_json() {
  if printf '%s' "$2" | jq -e "$3" >/dev/null 2>&1; then
    echo "ok    $1"
  else
    fail "$1: $2"
  fi
}

balance() {
  local raw coins
  raw=$(player_call wallet get_balance '{}')
  coins=$(printf '%s' "$raw" | jq -er '.data.coins') || fail "wallet.get_balance did not return coins: $raw"
  printf '%s' "$coins"
}

# fund <coins> credits the signed-in player through the sample storefront webhook.
fund() {
  local rid body sig out
  rid="fund-$(date +%s)-$RANDOM"
  body="{\"receipt_id\":\"$rid\",\"player_id\":\"$player\",\"coins\":$1}"
  sig="sha256=$(printf '%s' "$body" | openssl dgst -sha256 -hmac "$secret" | awk '{print $NF}')"
  out=$(curl -sS -m 8 -X POST "$http/api/webhooks/brainrot_market/storefront_purchase" \
    -H 'content-type: application/json' -H "x-storefront-signature: $sig" -d "$body")
  ok_json "funding $1 coins through the storefront webhook" "$out" '.status=="credited"'
}

buy() { player_call brainrot_market buy_shares "{\"symbol\":\"$1\",\"shares\":$2}"; }
sell() { player_call brainrot_market sell_shares "{\"symbol\":\"$1\",\"shares\":$2}"; }

wallet() {
  login
  echo "== wallet"
  ok_json "player reads only their own balance" "$(player_call wallet get_balance '{}')" \
    ".status==\"ok\" and .data.player_id==\"$player\" and .data.coins==0"
  ok_json "player credit over HTTP is refused" \
    "$(player_call wallet credit "{\"player_id\":\"$player\",\"amount\":999,\"reason\":\"cheat\"}")" \
    '.status=="error" and (.error|tostring|contains("forbidden_scope"))'
  ok_json "player debit over HTTP is refused" \
    "$(player_call wallet debit "{\"player_id\":\"$player\",\"amount\":1,\"reason\":\"cheat\"}")" \
    '.status=="error" and (.error|tostring|contains("forbidden_scope"))'
  [ "$(balance)" -eq 0 ] || fail "refused credit/debit changed the balance"
  echo "ok    refused credit/debit left the balance at 0"
}

daily() {
  login
  echo "== daily_rewards"
  local reward before claim
  reward=$(player_call resource_store list '{"resource":"daily_reward_table","limit":10}' \
    | jq -er '.data.rows[] | select(.day == 1) | .coins') || fail "daily_reward_table has no day 1"
  ok_json "status before the first claim" "$(player_call daily_rewards get_status '{}')" \
    '.status=="ok" and .data.next_day==1 and .data.can_claim==true'
  before=$(balance)
  claim=$(player_call daily_rewards claim '{}')
  ok_json "first claim pays day one through wallet" "$claim" \
    ".status==\"ok\" and .data.day==1 and .data.coins==$reward and .data.balance==$((before + reward))"
  ok_json "second claim the same day is refused" "$(player_call daily_rewards claim '{}')" \
    '.status=="error" and (.error|tostring|contains("already_claimed"))'
  [ "$(balance)" -eq $((before + reward)) ] || fail "the refused second claim changed the balance"
  echo "ok    claim paid $reward once and the refusal paid nothing"
}

webhook() {
  login
  echo "== storefront webhook"
  local rid body sig out code
  rid="rcpt-$(date +%s)-$RANDOM"
  body="{\"receipt_id\":\"$rid\",\"player_id\":\"$player\",\"coins\":500}"
  sig="sha256=$(printf '%s' "$body" | openssl dgst -sha256 -hmac "$secret" | awk '{print $NF}')"
  ok_json "valid signature credits" \
    "$(curl -sS -m 8 -X POST "$http/api/webhooks/brainrot_market/storefront_purchase" \
      -H 'content-type: application/json' -H "x-storefront-signature: $sig" -d "$body")" \
    '.status=="credited"'
  [ "$(balance)" -eq 500 ] || fail "a credited receipt did not add 500 coins"
  ok_json "replayed receipt is a duplicate" \
    "$(curl -sS -m 8 -X POST "$http/api/webhooks/brainrot_market/storefront_purchase" \
      -H 'content-type: application/json' -H "x-storefront-signature: $sig" -d "$body")" \
    '.status=="duplicate"'
  [ "$(balance)" -eq 500 ] || fail "a replayed receipt paid twice"
  out=$(curl -sS -m 8 -w '\n%{http_code}' -X POST "$http/api/webhooks/brainrot_market/storefront_purchase" \
    -H 'content-type: application/json' -H 'x-storefront-signature: sha256=00' -d "$body")
  code=$(printf '%s' "$out" | tail -1)
  [ "$code" = 401 ] || fail "a bad signature answered $code, expected 401"
  ok_json "bad signature is rejected" "$(printf '%s' "$out" | sed '$d')" '.error=="bad_signature"'
  out=$(curl -sS -m 8 -w '\n%{http_code}' -X POST "$http/api/webhooks/brainrot_market/storefront_purchase" \
    -H 'content-type: application/json' -H "x-storefront-signature: $sig" \
    -d "{\"receipt_id\":\"$rid\",\"player_id\":\"$player\",\"coins\":5000}")
  code=$(printf '%s' "$out" | tail -1)
  [ "$code" = 401 ] || fail "a tampered body answered $code, expected 401"
  [ "$(balance)" -eq 500 ] || fail "a bad or tampered receipt paid coins"
  echo "ok    only the signed receipt paid, once"
}

market() {
  login
  echo "== brainrot_market"
  fund 1000
  local start after price bought sold proceeds farm
  start=$(balance)
  bought=$(buy SKBD 2)
  ok_json "buy shares with funds" "$bought" \
    '.status=="ok" and .data.side=="buy" and .data.shares==2 and .data.coins==(.data.price*2)'
  price=$(printf '%s' "$bought" | jq -er '.data.price') || fail "buy_shares returned no price"
  after=$(balance)
  [ "$after" -eq $((start - price * 2)) ] \
    || fail "buying 2 SKBD at $price moved the balance from $start to $after"

  ok_json "buy shares without funds is refused" "$(buy SKBD 1000000)" \
    '.status=="error" and (.error|tostring|contains("insufficient_funds"))'
  [ "$(balance)" -eq "$after" ] || fail "a refused buy changed the balance"
  ok_json "a refused buy holds no shares" "$(player_call brainrot_market get_portfolio '{}')" \
    '.status=="ok" and (.data.holdings|length)==1 and .data.holdings[0].symbol=="SKBD" and .data.holdings[0].shares==2'

  sold=$(sell SKBD 2)
  ok_json "sell shares pays the current price" "$sold" \
    '.status=="ok" and .data.side=="sell" and .data.shares==2 and .data.coins==(.data.price*2)'
  proceeds=$(printf '%s' "$sold" | jq -er '.data.coins') || fail "sell_shares returned no coins"
  [ "$(balance)" -eq $((after + proceeds)) ] || fail "the sale did not pay $proceeds coins"

  after=$(balance)
  ok_json "selling shares not held is refused" "$(sell SKBD 1)" \
    '.status=="error" and (.error|tostring|contains("not_enough_shares"))'
  [ "$(balance)" -eq "$after" ] || fail "a refused sale paid coins"
  ok_json "a refused sale cannot sell what is not held" "$(player_call brainrot_market get_portfolio '{}')" \
    '.status=="ok" and (.data.holdings|length)==0'

  after=$(balance)
  farm=$(player_call brainrot_market buy_farm '{}')
  ok_json "buy a farm" "$farm" '.status=="ok" and .data.farm_level==1'
  [ "$(balance)" -eq $((after - 200)) ] || fail "the first farm did not cost 200 coins"
  ok_json "collect idle" "$(player_call brainrot_market collect_idle '{}')" \
    '.status=="ok" and .data.farm_level==1 and .data.coins>=0'
  ok_json "portfolio" "$(player_call brainrot_market get_portfolio '{}')" \
    '.status=="ok" and .data.farm_level==1 and .data.pending_idle_coins>=0'
}

resources() {
  login
  echo "== player-readable resources"
  ok_json "player lists meme_stocks (AllowPlayerRead)" \
    "$(player_call resource_store list '{"resource":"meme_stocks","limit":10}')" \
    '.status=="ok" and ([.data.rows[].symbol]|index("SKBD")!=null) and ([.data.rows[].symbol]|index("SIGMA")!=null) and ([.data.rows[].symbol]|index("OHIO")!=null)'
  ok_json "player cannot list portfolios" \
    "$(player_call resource_store list '{"resource":"portfolios","limit":10}')" \
    '.status=="error" and (.data==null) and (.error|tostring|contains("forbidden_scope"))'
  ok_json "player lists daily_reward_table" \
    "$(player_call resource_store list '{"resource":"daily_reward_table","limit":10}')" \
    '.status=="ok" and (.data.rows|length)>=1 and ([.data.rows[].coins]|length)>=1'
  ok_json "a player cannot trigger the cron tick" "$(player_call brainrot_market tick_prices '{}')" \
    '.status=="error" and (.error|tostring|contains("forbidden_scope"))'
  ok_json "player gets one stock" \
    "$(player_call resource_store get '{"resource":"meme_stocks","id":"SKBD"}')" \
    '.status=="ok" and .data.row.symbol=="SKBD"'
}

all() {
  wallet
  daily
  webhook
  market
  resources
  echo "economy smoke: all ok"
}

if [ $# -eq 0 ]; then all; else for f in "$@"; do "$f"; done; fi
