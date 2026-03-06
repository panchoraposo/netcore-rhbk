#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-demo}"
REALM="${REALM:-mate-bank}"
PROVIDER_NAME="${PROVIDER_NAME:-ldap-openldap}"
TARGET_USERNAME="${TARGET_USERNAME:-}"

if [[ -z "${TARGET_USERNAME}" ]]; then
  echo "Usage:"
  echo "  TARGET_USERNAME='new.user' ./scripts/keycloak-sync-and-check-user.sh"
  exit 2
fi

KC_HOST="$(oc get route -n "${NAMESPACE}" mate-keycloak -o jsonpath='{.spec.host}')"
KC_BASE="https://${KC_HOST}"

ADMIN_USER="$(oc get secret -n "${NAMESPACE}" mate-keycloak-initial-admin -o jsonpath='{.data.username}' | base64 --decode)"
ADMIN_PASS="$(oc get secret -n "${NAMESPACE}" mate-keycloak-initial-admin -o jsonpath='{.data.password}' | base64 --decode)"

TOKEN="$(curl -skS -X POST "${KC_BASE}/realms/master/protocol/openid-connect/token" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode grant_type=password \
  --data-urlencode client_id=admin-cli \
  --data-urlencode username="${ADMIN_USER}" \
  --data-urlencode password="${ADMIN_PASS}" \
  | jq -r .access_token)"

if [[ -z "${TOKEN}" || "${TOKEN}" == "null" ]]; then
  echo "Failed to obtain admin token from Keycloak."
  exit 1
fi

echo "Keycloak: ${KC_BASE}"
echo "Realm:    ${REALM}"
echo

echo "Finding LDAP provider component id (${PROVIDER_NAME})"
PROVIDER_ID="$(curl -skS "${KC_BASE}/admin/realms/${REALM}/components?type=org.keycloak.storage.UserStorageProvider" \
  -H "Authorization: Bearer ${TOKEN}" \
  | jq -r --arg name "${PROVIDER_NAME}" '.[] | select(.name==$name) | .id' | head -n 1)"

if [[ -z "${PROVIDER_ID}" || "${PROVIDER_ID}" == "null" ]]; then
  echo "Could not find LDAP provider component named '${PROVIDER_NAME}' in realm '${REALM}'."
  exit 1
fi

echo "Triggering LDAP changed-users sync"
curl -skS -X POST "${KC_BASE}/admin/realms/${REALM}/user-storage/${PROVIDER_ID}/sync?action=triggerChangedUsersSync" \
  -H "Authorization: Bearer ${TOKEN}" \
  | jq .

echo
echo "Searching user in Keycloak (${TARGET_USERNAME})"
curl -skS "${KC_BASE}/admin/realms/${REALM}/users?username=${TARGET_USERNAME}&exact=true" \
  -H "Authorization: Bearer ${TOKEN}" \
  | jq 'map({id, username, enabled})'
