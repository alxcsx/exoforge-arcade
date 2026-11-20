#!/usr/bin/env bash
# Shop demo, asserted: a seeded catalog, a purchase that moves gold and inventory through three
# extensions (loja_itens -> items, inventario), and a purchase the player cannot afford.
# Exits non-zero on any wrong result.
#   tools/smoke/shop.sh
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
http="${EXO_HTTP:-http://localhost:4001}"
tag="shop-smoke-$RANDOM$RANDOM"
token=""
fail=0

exo() { (cd "$root/${SMOKE_PROJECT:-2d-shop}" && ./exo "$@" --dir Exoforge); }

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

login

echo "== seed the catalog and the offers (idempotent, studio-scoped: through the CLI)"
exo call items seed_itens --json >/dev/null
exo call loja_itens seed_loja --json >/dev/null

echo "== the shop lists the seeded offers with their catalog data"
expect "four offers, priced from the catalog" "$(post loja_itens/listar_itens)" \
  "data is not None and len(data) == 4 and any(o['item_id'] == 'pocao_vida' and o['preco'] == 50 for o in data)"

echo "== buying spends gold and fills the inventory"
expect "a new player starts with 1000 gold and no items" "$(post inventario/obter_inventario)" \
  "data is not None and data['ouro'] == 1000 and len(data['itens']) == 0"
expect "buying a potion costs 50" "$(post loja_itens/comprar_item '{"item_id":"pocao_vida"}')" \
  "data is not None and data['ouro'] == 950 and any(i['item_id'] == 'pocao_vida' and i['quantidade'] == 1 for i in data['itens'])"
expect "a second potion stacks" "$(post loja_itens/comprar_item '{"item_id":"pocao_vida"}')" \
  "data is not None and data['ouro'] == 900 and any(i['item_id'] == 'pocao_vida' and i['quantidade'] == 2 for i in data['itens'])"

echo "== what the player cannot afford is refused and costs nothing"
expect "the legendary sword (500) is bought once" "$(post loja_itens/comprar_item '{"item_id":"espada_lendaria"}')" \
  "data is not None and data['ouro'] == 400"
expect "a second one is refused" "$(post loja_itens/comprar_item '{"item_id":"espada_lendaria"}')" \
  "code == 'sem_ouro'"
expect "an unknown item is refused" "$(post loja_itens/comprar_item '{"item_id":"nope"}')" \
  "code == 'item_nao_encontrado'"
expect "gold is untouched by the refusals" "$(post inventario/obter_inventario)" \
  "data is not None and data['ouro'] == 400"

if [ "$fail" -ne 0 ]; then
  echo "shop smoke: FAILED"
  exit 1
fi
echo "shop smoke: all ok"
