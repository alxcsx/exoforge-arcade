#!/usr/bin/env bash
# Doodle Dash demo: upload a level, clear it, publish, browse, rate. Players call over plain HTTP.
# Every step asserts; the script exits non-zero on any wrong result.
#   tools/smoke/doodle.sh        or     source it and call `doodle`
set -euo pipefail

doodle() {
  local root http token tag level image wrong sha file_id file_url upload returned_sha signed signed_url expires code row browse
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  http="${EXO_HTTP:-http://localhost:4001}"
  tag="smoke$RANDOM$RANDOM"
  image="$(mktemp -d)/level.png"
  wrong="${image%level.png}wrong.png"

  post() { curl -sS -X POST "$http/api/$1" -H "authorization: Bearer $token" -H 'content-type: application/json' -d "$2"; }
  field() {
    python3 -I -c "
import json, sys
raw = sys.stdin.read()
try:
    print(json.loads(raw)$1)
except Exception:
    sys.exit('smoke failed: expected $1 in the response, got: ' + raw[:300])
"
  }
  fail() { echo "smoke failed: $*" >&2; exit 1; }
  expect_status() { [ "$3" = "$2" ] && echo "$1: $3 (expected)" || fail "$1 answered $3, expected $2"; }
  expect_error() {
    printf '%s' "$3" | python3 -I -c '
import json, sys
expected = sys.argv[1]
raw = sys.stdin.read()
try:
    err = json.loads(raw).get("error")
    error = err.get("code") if isinstance(err,dict) else err
except Exception:
    sys.exit("expected error " + expected + ", got: " + raw[:300])
if expected not in error:
    sys.exit("expected error " + expected + ", got: " + error[:300])
' "$2" && echo "$1: $2 (expected)" || fail "$1 was not refused with $2"
  }
  row_of() {
    python3 -I -c '
import json, sys
rows = json.load(sys.stdin)["data"]
match = [r for r in rows if r["id"] == sys.argv[1]]
if not match:
    sys.exit("level " + sys.argv[1] + " is missing from browse: " + json.dumps(rows)[:300])
print(json.dumps(match[0]))
' "$1"
  }
  b64() { base64 < "$1" | tr -d '\n'; }
  png() {
    python3 -I - "$@" <<'PY'
import struct, sys, zlib
w, h, tag = (int(sys.argv[3]) if len(sys.argv) > 3 else 32), 18, int(sys.argv[2])
rows = []
for y in range(h):
    row = bytearray(b'\xff\xff\xff' * w)
    if y == 0:
        row[:] = b'\x00\x00\x00' * w
    rows.append(b'\x00' + bytes(row))
# a run-specific pixel in an empty row, so every run uploads a different image
x = tag % w
rows[5] = rows[5][:1 + 3 * x] + bytes([tag % 256, (tag // 256) % 256, 7]) + rows[5][4 + 3 * x:]
def chunk(kind, data):
    body = kind + data
    return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body))
open(sys.argv[1], 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b''.join(rows))) + chunk(b'IEND', b''))
PY
  }

  login() { curl -sS -X POST "$http/api/auth/anonymous" -H 'content-type: application/json' -d "{\"name\":\"$1\",\"disposable\":true}" | field '["data"]["token"]'; }
  png "$image" "$RANDOM$RANDOM"
  png "$wrong" "$RANDOM" 31
  sha="$(shasum -a 256 "$image" | cut -d" " -f1)"

  echo "== players can't touch the file bucket directly"
  expect_status "anonymous upload to /api/files/upload" 401 "$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$http/api/files/upload" -F bucket=doodle_levels -F "file=@$image;type=image/png")"
  token="$(login "$tag-author")"
  code="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$http/api/files/upload" -H "authorization: Bearer $token" -F bucket=doodle_levels -F "file=@$image;type=image/png")"
  expect_status "player POST /api/files/upload" 403 "$code"
  expect_error "player file_bucket.upload_file" forbidden_scope "$(post file_bucket/upload_file '{"bucket":"doodle_levels","filename":"x.png","content":"AAAA"}')"

  echo "== browse needs a player session"
  expect_status "anonymous POST level_share/browse" 401 "$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$http/api/level_share/browse" -H 'content-type: application/json' -d '{}')"

  echo "== upload_level refuses what is not a 32x18 PNG"
  expect_error "upload of non-base64" invalid_png "$(post level_share/upload_level '{"png_base64":"aGVsbG8="}')"
  expect_error "upload of a 31x18 PNG" invalid_size "$(post level_share/upload_level "{\"png_base64\":\"$(b64 "$wrong")\"}")"

  echo "== author uploads the level image through level_share"
  upload="$(post level_share/upload_level "{\"png_base64\":\"$(b64 "$image")\"}")"
  echo "$upload"
  file_id="$(echo "$upload" | field '["data"]["file_id"]')"
  returned_sha="$(echo "$upload" | field '["data"]["sha256"]')"
  [ "$returned_sha" = "$sha" ] || fail "server sha256 $returned_sha differs from local $sha"

  echo "== a plain GET of the level file is refused, even with the author's token"
  file_url="$http/api/files/doodle_levels/$file_id/level.png"
  expect_status "anonymous GET of the file" 403 "$(curl -sS -o /dev/null -w '%{http_code}' "$file_url")"
  expect_status "player GET of the file" 403 "$(curl -sS -o /dev/null -w '%{http_code}' -H "authorization: Bearer $token" "$file_url")"

  echo "== publish without a recorded clear is refused"
  expect_error "publish before a clear" clear_required "$(post level_share/publish "{\"title\":\"Smoke $tag\",\"difficulty\":\"hard\",\"file_id\":\"$file_id\"}")"

  echo "== a clear key must be a level id or an image hash"
  expect_error "record_clear with junk" invalid_key "$(post level_share/record_clear '{"key":"anything"}')"

  echo "== record the clear for this exact image, then publish (author comes from player_data)"
  [ "$(post level_share/record_clear "{\"key\":\"$sha\"}" | field '["data"]["counted"]')" = "True" ] || fail "the first clear of this image was not counted"
  level="$(post level_share/publish "{\"title\":\"Smoke $tag\",\"difficulty\":\"hard\",\"file_id\":\"$file_id\"}")"
  echo "$level"
  level="$(echo "$level" | field '["data"]["level_id"]')"

  echo "== browse lists the new level without exposing where its file lives"
  browse="$(post level_share/browse '{"tag":"difficulty:hard","sort":"newest","limit":50}')"
  row="$(echo "$browse" | row_of "$level")"
  echo "$row"
  [ "$(echo "$row" | field '["title"]')" = "Smoke $tag" ] || fail "the level title is not Smoke $tag"
  [ "$(echo "$row" | field '["author"]')" = "$tag-author" ] || fail "the level author did not come from player_data"
  [ "$(echo "$row" | field '["tag"]')" = "difficulty:hard" ] || fail "the level tag is not difficulty:hard"
  [ "$(echo "$row" | field '["plays"]')" = "0" ] || fail "a fresh level already has plays"
  [ "$(echo "$row" | field '["rating_count"]')" = "0" ] || fail "a fresh level already has ratings"
  echo "$browse" | python3 -I -c '
import json, sys
rows = json.load(sys.stdin)["data"]
leaked = [k for row in rows for k in row if k.startswith("file")]
if leaked:
    sys.exit("browse exposes file locations: " + ", ".join(sorted(set(leaked))))
' || fail "browse leaked file fields"
  echo "browse: no file locations (expected)"

  echo "== the same image can't be published twice"
  expect_error "publish of the published image" already_published "$(post level_share/publish "{\"title\":\"Again\",\"difficulty\":\"hard\",\"file_id\":\"$file_id\"}")"

  echo "== another player can't publish the author's upload, then browses (the same call a curl user makes), plays, rates twice"
  token="$(login "$tag-fan")"
  expect_error "publish of another player's upload" invalid_file "$(post level_share/publish "{\"title\":\"Mine now\",\"difficulty\":\"hard\",\"file_id\":\"$file_id\"}")"

  echo "== downloads go through a short-lived signed url; the bucket itself stays private"
  signed="$(post level_share/get_level_url "{\"level_id\":\"$level\"}")"
  echo "$signed"
  signed_url="$(echo "$signed" | field '["data"]["url"]')"
  expires="$(echo "$signed" | field '["data"]["expires_at"]')"
  case "$signed_url" in
    /api/files/doodle_levels/*sig=*) ;;
    *) fail "the level url is not a signed bucket url: $signed_url" ;;
  esac
  python3 -I -c '
import sys, time
expires = int(sys.argv[1])
if not 250 <= expires - time.time() <= 320:
    sys.exit("expected a ~300s url, expires_at is " + sys.argv[1])
' "$expires" || fail "the level url is not short-lived"
  expect_status "download through the signed url" 200 "$(curl -sS -o "$image.down" -w '%{http_code}' "$http$signed_url")"
  cmp -s "$image" "$image.down" || fail "downloaded bytes differ from the uploaded level"
  echo "downloaded $(wc -c < "$image.down" | tr -d ' ') bytes, identical to the upload"
  expect_status "tampered signature" 403 "$(curl -sS -o /dev/null -w '%{http_code}' "$http${signed_url%?}X")"
  expect_status "expiry in the future" 403 "$(curl -sS -o /dev/null -w '%{http_code}' "$http${signed_url}0")"

  echo "== a clear of the level counts one play per player"
  [ "$(post level_share/record_clear "{\"key\":\"$level\"}" | field '["data"]["counted"]')" = "True" ] || fail "the first clear of the level was not counted"
  [ "$(post level_share/record_clear "{\"key\":\"$level\"}" | field '["data"]["counted"]')" = "False" ] || fail "a second clear by the same player was counted"
  row="$(post level_share/browse '{"tag":"difficulty:hard","sort":"popular","limit":50}' | row_of "$level")"
  [ "$(echo "$row" | field '["plays"]')" = "1" ] || fail "the level does not show exactly one play"

  echo "== rating: 1..5, once per player, averaged"
  expect_error "rate with 0 stars" invalid_rating "$(post level_share/rate "{\"level_id\":\"$level\",\"stars\":0}")"
  row="$(post level_share/rate "{\"level_id\":\"$level\",\"stars\":5}")"
  echo "$row"
  [ "$(echo "$row" | field '["data"]["rating"]')" = "5" ] || fail "the first rating is not 5"
  expect_error "second rating by the same player" already_rated "$(post level_share/rate "{\"level_id\":\"$level\",\"stars\":1}")"
  row="$(post level_share/browse '{"tag":"difficulty:hard","sort":"top","limit":50}' | row_of "$level")"
  [ "$(echo "$row" | field '["rating"]')" = "5" ] || fail "browse does not show the 5* average"
  [ "$(echo "$row" | field '["rating_count"]')" = "1" ] || fail "browse does not show one rating"
  echo "$row"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then doodle; fi
