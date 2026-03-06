#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-demo}"
REALM="${REALM:-mate-bank}"
CLIENT_ID="${CLIENT_ID:-mate-cli}"
KC_USERNAME="${KC_USERNAME:-maria.gomez}"
KC_PASSWORD="${KC_PASSWORD:-mariaPassword}"

KEYCLOAK_HOST="$(oc get route -n "${NAMESPACE}" mate-keycloak -o jsonpath='{.spec.host}')"
KEYCLOAK_BASE_URL="https://${KEYCLOAK_HOST}"

CLIENT_SECRET="$(oc get secret -n "${NAMESPACE}" mate-demo-config -o jsonpath='{.data.MATE_CLI_CLIENT_SECRET}' | base64 --decode)"

curl -skS -X POST "${KEYCLOAK_BASE_URL}/realms/${REALM}/protocol/openid-connect/token" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode grant_type=password \
  --data-urlencode client_id="${CLIENT_ID}" \
  --data-urlencode client_secret="${CLIENT_SECRET}" \
  --data-urlencode username="${KC_USERNAME}" \
  --data-urlencode password="${KC_PASSWORD}" \
  | jq -r .access_token

