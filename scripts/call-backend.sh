#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-demo}"
PATH_SUFFIX="${1:-/api/me}"

BACKEND_HOST="$(oc get route -n "${NAMESPACE}" mate-backend -o jsonpath='{.spec.host}')"
BACKEND_BASE_URL="https://${BACKEND_HOST}"

TOKEN="$(./scripts/get-token.sh)"

curl -skS "${BACKEND_BASE_URL}${PATH_SUFFIX}" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Accept: application/json"

