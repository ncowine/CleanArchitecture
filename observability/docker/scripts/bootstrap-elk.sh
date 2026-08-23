#!/usr/bin/env bash
# =============================================================================
#  One-time bootstrap for the PRODUCTION ELK add-on
# =============================================================================
# Turning Elasticsearch security on leaves three things that cannot be done in
# YAML, because they are API calls against a running cluster:
#
#   1. give the built-in kibana_system account a password (Kibana cannot start
#      without one, and it has none by default)
#   2. mint a least-privilege API key for the app's audit writer (which runs on
#      the WINDOWS server, so the key is carried there rather than into .env)
#   3. install a retention policy, so the audit index does not grow forever
#
# Run it from THIS folder on the server, once, after the first successful start:
#
#     docker compose -f docker-compose.prod.yml -f docker-compose.prod.elk.yml up -d elasticsearch
#     ./scripts/bootstrap-elk.sh
#
# It prints two secrets that go to DIFFERENT machines: one into .env on this VM,
# one onto the Windows server where the app runs. It is safe to re-run: the ILM
# policy and template are idempotent, and each run mints a NEW api key (revoke
# the old ones in Kibana → Stack Management → API keys).
#
# Requires: .env filled in with ELASTIC_PASSWORD, and curl inside the container.
# =============================================================================
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
  echo "ERROR: no .env in $(pwd). Copy .env.example to .env and fill it in first." >&2
  exit 1
fi

# shellcheck disable=SC1091
set -a; source .env; set +a

: "${ELASTIC_PASSWORD:?ELASTIC_PASSWORD is empty in .env}"

COMPOSE="docker compose -f docker-compose.prod.yml -f docker-compose.prod.elk.yml"

# How long audit records are kept. NOTE: audit retention is frequently dictated
# by policy or regulation rather than by disk space — check what applies to you
# before accepting this default. Deleting records you were required to keep is a
# worse outcome than a larger disk.
AUDIT_RETENTION_DAYS="${AUDIT_RETENTION_DAYS:-90}"

# Run a curl inside the elasticsearch container, authenticated as the superuser.
# Done in-container on purpose: port 9200 is not published, which is the point.
es() {
  local method="$1" path="$2" body="${3:-}"
  if [[ -n "$body" ]]; then
    $COMPOSE exec -T elasticsearch curl -sS -u "elastic:${ELASTIC_PASSWORD}" \
      -X "$method" "http://localhost:9200${path}" \
      -H 'Content-Type: application/json' -d "$body"
  else
    $COMPOSE exec -T elasticsearch curl -sS -u "elastic:${ELASTIC_PASSWORD}" \
      -X "$method" "http://localhost:9200${path}"
  fi
}

echo "==> Waiting for Elasticsearch to answer..."
for i in $(seq 1 60); do
  if es GET /_cluster/health >/dev/null 2>&1; then break; fi
  if [[ $i -eq 60 ]]; then
    echo "ERROR: Elasticsearch did not become reachable. Check: $COMPOSE logs elasticsearch" >&2
    exit 1
  fi
  sleep 5
done
echo "    up."

# ── 1. kibana_system password ────────────────────────────────────────────────
# Generated here rather than asked for, so it is long, random, and never reused.
echo "==> Setting the kibana_system password..."
KIBANA_PW="$(openssl rand -base64 32 | tr -d '/+=' | cut -c1-32)"
es POST "/_security/user/kibana_system/_password" "{\"password\":\"${KIBANA_PW}\"}" >/dev/null
echo "    done."

# ── 2. least-privilege API key for the app ───────────────────────────────────
# Scoped to the audit indices only. Deliberately NOT the "write" privilege:
# create_doc allows appending new records but not overwriting or deleting them,
# which is the property an audit trail needs — if the app is compromised the
# attacker can add records, not quietly rewrite history.
#
# If audit shipping starts logging 403s, the likely cause is the client sending
# an explicit document id; swap "create_doc" for "write" below and re-run.
echo "==> Minting the audit-writer API key..."
API_KEY_JSON="$(es POST /_security/api_key '{
  "name": "cleanarch-audit-writer",
  "role_descriptors": {
    "audit_writer": {
      "cluster": ["monitor"],
      "indices": [
        {
          "names": ["cleanarch-audit-*"],
          "privileges": ["create_index", "create_doc", "auto_configure", "view_index_metadata"]
        }
      ]
    }
  }
}')"

# The client wants the "encoded" form (base64 of id:api_key).
AUDIT_KEY="$(printf '%s' "$API_KEY_JSON" | grep -o '"encoded":"[^"]*"' | cut -d'"' -f4)"
if [[ -z "$AUDIT_KEY" ]]; then
  echo "ERROR: could not read the api key from the response:" >&2
  echo "$API_KEY_JSON" >&2
  exit 1
fi
echo "    done."

# ── 3. retention ─────────────────────────────────────────────────────────────
# The app writes one index per day (cleanarch-audit-YYYY.MM.DD). This ILM policy
# deletes each index once it is old enough, and the index template attaches the
# policy to every index matching the pattern as it is created.
echo "==> Installing the ${AUDIT_RETENTION_DAYS}-day retention policy..."
es PUT /_ilm/policy/cleanarch-audit-retention "{
  \"policy\": {
    \"phases\": {
      \"hot\": { \"actions\": {} },
      \"delete\": {
        \"min_age\": \"${AUDIT_RETENTION_DAYS}d\",
        \"actions\": { \"delete\": {} }
      }
    }
  }
}" >/dev/null

es PUT /_index_template/cleanarch-audit '{
  "index_patterns": ["cleanarch-audit-*"],
  "template": {
    "settings": {
      "index.lifecycle.name": "cleanarch-audit-retention",
      "number_of_replicas": 0
    }
  }
}' >/dev/null
echo "    done."

# ── Output ───────────────────────────────────────────────────────────────────
cat <<REPORT

==============================================================================
 Two secrets, shown ONCE. Elasticsearch does not store either in a form that
 can be read back — if you lose them, re-run this script to issue new ones.
 They go to DIFFERENT machines. Do not put both in the same file.
==============================================================================

1) THIS VM — add to .env in $(pwd):

KIBANA_SYSTEM_PASSWORD=${KIBANA_PW}

2) THE WINDOWS SERVER — set on the app (see README-windows-iis.md).
   This is the app's audit-writer credential, so it belongs where the app runs:

Audit__Elasticsearch__ApiKey=${AUDIT_KEY}

   Point the app at Elasticsearch THROUGH Caddy, never at port 9200 directly:

Audit__Elasticsearch__Uri=https://<your AUDIT_HOST>

   That URL is what keeps this key encrypted in transit. Over plain http it
   would cross the public internet in cleartext on every single audit write.

Then bring the stack up:

  docker compose -f docker-compose.prod.yml -f docker-compose.prod.elk.yml up -d

Note: number_of_replicas is 0 because this is a single node — a replica would
have nowhere to live and would leave the cluster permanently yellow. It also
means there is no redundancy inside Elasticsearch, so the es-data volume backup
described in README-production.md is the only copy of the audit trail.
REPORT
