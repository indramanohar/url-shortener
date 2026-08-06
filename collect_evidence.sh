#!/usr/bin/env bash
# Collects real evidence from a RUNNING instance of your API and saves raw JSON
# responses to ./evidence/ for build_evidence_doc.py to assemble into EVIDENCE.md.
#
# Usage:
#   BASE_URL="http://localhost:5000" API_KEY="test-key" ./collect_evidence.sh
# (edit the two values below directly if you don't want to pass env vars)

set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:5000}"
API_KEY="${API_KEY:-test-key}"
OUT="./evidence"
mkdir -p "$OUT"

# --- tiny JSON field reader (top-level keys only — that's all we need) ---
field() {
  python3 -c "
import json,sys
try:
    d = json.loads(sys.stdin.read())
    print(d.get(sys.argv[1], ''))
except Exception:
    print('')
" "$1"
}

get()  { curl -s "$BASE_URL$1"; }
post() {
  local path="$1" data="${2:-}"
  if [ -n "$data" ]; then
    curl -s -X POST "$BASE_URL$path" -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" -d "$data"
  else
    curl -s -X POST "$BASE_URL$path" -H "X-Api-Key: $API_KEY"
  fi
}
del() { curl -s -X DELETE "$BASE_URL$1" -H "X-Api-Key: $API_KEY"; }

save() { printf '%s' "$1" > "$OUT/$2"; echo "  saved: $OUT/$2"; }

wait_for_approval() {
  local run_id="$1" label="$2"
  for i in $(seq 1 30); do
    resp=$(get "/pipeline/$run_id")
    status=$(printf '%s' "$resp" | field status)
    pending=$(printf '%s' "$resp" | field pendingApproval)
    echo "  [$label poll $i] status=$status pendingApproval=$pending"
    if [ "$pending" = "True" ]; then save "$resp" "${run_id}_${label}_status.json"; return 0; fi
    if [ "$status" != "Running" ]; then
      echo "  left Running unexpectedly (status=$status) — saving what we have"
      save "$resp" "${run_id}_${label}_unexpected.json"; return 1
    fi
    sleep 2
  done
  echo "  timed out waiting for $label approval"; return 1
}

run_scenario() {
  local scenario="$1" body="$2" tag="$3"
  echo "=== $tag ($scenario) ==="
  resp=$(post "/pipeline/run" "$body")
  save "$resp" "${tag}_start.json"
  run_id=$(printf '%s' "$resp" | field runId)
  echo "  runId=$run_id"
  if [ -z "$run_id" ]; then echo "  FAILED to start — check BASE_URL/API_KEY are correct"; return 1; fi

  wait_for_approval "$run_id" design || return 1
  save "$(post "/pipeline/$run_id/approve")" "${tag}_approve_design.json"

  wait_for_approval "$run_id" release || return 1
  save "$(post "/pipeline/$run_id/approve")" "${tag}_approve_release.json"

  sleep 1
  save "$(get "/pipeline/$run_id/audit")" "${tag}_audit.json"
  save "$(get "/pipeline/$run_id/metrics")" "${tag}_metrics.json"
}

echo "############ PART 1 — URL shortener smoke test ############"
resp=$(post "/shorten" '{"url":"https://example.com/very/long/path","alias":"demo1","ttlDays":30}')
save "$resp" "shorten.json"
code=$(printf '%s' "$resp" | field code)
echo "  code=$code"
curl -sI "$BASE_URL/$code" > "$OUT/redirect_headers.txt"; echo "  saved: $OUT/redirect_headers.txt"
save "$(get "/$code/analytics")" "analytics.json"
del "/$code" > /dev/null; echo "  deleted $code"
save "$(get "/health")" "health.json"

echo
echo "############ PART 2 — Three required scenarios ############"
run_scenario "greenfield" '{"scenario":"greenfield","skipVulnScan":true}' "greenfield"
run_scenario "brownfield" '{"scenario":"brownfield","skipVulnScan":true}' "brownfield"
run_scenario "ambiguous"  '{"scenario":"ambiguous","skipVulnScan":false}' "ambiguous"   # real vuln scan here

echo
echo "############ PART 3 — Retry -> rollback proof ############"
run_scenario "greenfield" '{"scenario":"greenfield","injectTestFailure":true,"skipVulnScan":true}' "retry_rollback"

echo
echo "############ PART 4 — Human rejection ############"
resp=$(post "/pipeline/run" '{"scenario":"greenfield","skipVulnScan":true}')
save "$resp" "rejection_start.json"
run_id=$(printf '%s' "$resp" | field runId)
if [ -n "$run_id" ]; then
  wait_for_approval "$run_id" design
  save "$(post "/pipeline/$run_id/reject")" "rejection_reject.json"
  sleep 1
  save "$(get "/pipeline/$run_id/audit")" "rejection_audit.json"
fi

echo
echo "############ PART 5 — Cancellation ############"
resp=$(post "/pipeline/run" '{"scenario":"greenfield","skipVulnScan":true}')
save "$resp" "cancel_start.json"
run_id=$(printf '%s' "$resp" | field runId)
if [ -n "$run_id" ]; then
  save "$(post "/pipeline/$run_id/cancel")" "cancel_cancel.json"
  sleep 2
  save "$(get "/pipeline/$run_id")" "cancel_status.json"
  save "$(get "/pipeline/$run_id/audit")" "cancel_audit.json"
fi

echo
echo "############ PART 6 — Aggregate ############"
save "$(get "/pipeline/metrics")" "aggregate_metrics.json"
save "$(get "/pipeline")" "list_runs.json"

echo
echo "Done. All raw evidence is in $OUT/."
echo "Now run: python3 build_evidence_doc.py"
