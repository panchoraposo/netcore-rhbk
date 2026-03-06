#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-demo}"
OVERLAY="${OVERLAY:-deploy/overlays/mate-bank-demo}"

echo "[1/9] Checking cluster access"
oc whoami >/dev/null

echo "[2/9] Ensuring RHBK operator is installed"
./scripts/install-rhbk-operator.sh

echo "[3/9] Applying manifests (namespace + LDAP + Postgres + Keycloak + apps)"
oc apply -k "${OVERLAY}"

echo "[4/9] Preparing LDAP server (389ds) permissions (anyuid SCC)"
oc create sa -n "${NAMESPACE}" dirsrv-sa >/dev/null 2>&1 || true
oc adm policy add-scc-to-user anyuid -n "${NAMESPACE}" -z dirsrv-sa >/dev/null

echo "[4b/9] Scaling apps down until images exist"
oc scale -n "${NAMESPACE}" deployment/mate-backend deployment/mate-portal --replicas=0 >/dev/null 2>&1 || true

echo "[4c/9] Pointing apps to internal registry tags"
REGISTRY="image-registry.openshift-image-registry.svc:5000/${NAMESPACE}"
oc set image -n "${NAMESPACE}" deployment/mate-backend backend="${REGISTRY}/mate-backend:latest" >/dev/null 2>&1 || true
oc set image -n "${NAMESPACE}" deployment/mate-portal portal="${REGISTRY}/mate-portal:latest" >/dev/null 2>&1 || true

echo "[5/9] Waiting for LDAP + Postgres pods"
oc wait -n "${NAMESPACE}" --for=condition=Ready pod -l app.kubernetes.io/name=dirsrv --timeout=10m
oc wait -n "${NAMESPACE}" --for=condition=Ready pod -l app.kubernetes.io/name=keycloak-postgres --timeout=10m

echo "[6/9] Seeding LDAP directory (suffix, users, groups)"
BASEDN="dc=mate,dc=bank,dc=demo"
DM_DN="cn=Directory Manager"
DM_PASSWORD="$(oc get secret -n "${NAMESPACE}" dirsrv-dm-password -o jsonpath='{.data.dm-password}' | base64 --decode)"

oc exec -n "${NAMESPACE}" dirsrv-0 -- dsconf localhost backend create --suffix "${BASEDN}" --be-name userroot --create-suffix --create-entries >/dev/null 2>&1 || true

oc exec -n "${NAMESPACE}" dirsrv-0 -- bash -lc "cat <<'LDIF' | ldapadd -c -x -H ldap://localhost:3389 -D '${DM_DN}' -w '${DM_PASSWORD}'\n\ndn: ou=Groups,${BASEDN}\nobjectClass: organizationalUnit\nou: Groups\n\n# Users (ou=People is created by dsconf --create-entries)\n\ndn: uid=juan.perez,ou=People,${BASEDN}\nobjectClass: inetOrgPerson\nuid: juan.perez\ncn: Juan\nsn: Perez\nmail: juan.perez@matebank.demo\nuserPassword: juanPassword\n\ndn: uid=maria.gomez,ou=People,${BASEDN}\nobjectClass: inetOrgPerson\nuid: maria.gomez\ncn: Maria\nsn: Gomez\nmail: maria.gomez@matebank.demo\nuserPassword: mariaPassword\n\ndn: uid=sofia.fernandez,ou=People,${BASEDN}\nobjectClass: inetOrgPerson\nuid: sofia.fernandez\ncn: Sofia\nsn: Fernandez\nmail: sofia.fernandez@matebank.demo\nuserPassword: sofiaPassword\n\ndn: uid=diego.rodriguez,ou=People,${BASEDN}\nobjectClass: inetOrgPerson\nuid: diego.rodriguez\ncn: Diego\nsn: Rodriguez\nmail: diego.rodriguez@matebank.demo\nuserPassword: diegoPassword\n\ndn: uid=it.admin,ou=People,${BASEDN}\nobjectClass: inetOrgPerson\nuid: it.admin\ncn: IT\nsn: Admin\nmail: it.admin@matebank.demo\nuserPassword: itAdminPassword\n\ndn: uid=nico.paz,ou=People,${BASEDN}\nobjectClass: inetOrgPerson\nuid: nico.paz\ncn: Nicolas\nsn: Paz\nmail: nico.paz@matebank.demo\nuserPassword: nicoPassword\n\n# Groups\n\ndn: cn=branch-tellers,ou=Groups,${BASEDN}\nobjectClass: groupOfNames\ncn: branch-tellers\nmember: uid=juan.perez,ou=People,${BASEDN}\nmember: uid=maria.gomez,ou=People,${BASEDN}\n\ndn: cn=risk-analysts,ou=Groups,${BASEDN}\nobjectClass: groupOfNames\ncn: risk-analysts\nmember: uid=sofia.fernandez,ou=People,${BASEDN}\n\ndn: cn=credit-approvers,ou=Groups,${BASEDN}\nobjectClass: groupOfNames\ncn: credit-approvers\nmember: uid=diego.rodriguez,ou=People,${BASEDN}\n\ndn: cn=auditors,ou=Groups,${BASEDN}\nobjectClass: groupOfNames\ncn: auditors\nmember: uid=maria.gomez,ou=People,${BASEDN}\nmember: uid=sofia.fernandez,ou=People,${BASEDN}\n\ndn: cn=it-admins,ou=Groups,${BASEDN}\nobjectClass: groupOfNames\ncn: it-admins\nmember: uid=it.admin,ou=People,${BASEDN}\nLDIF" >/dev/null 2>&1 || true

echo "[7/9] Pinning Keycloak hostname (before start)"
KEYCLOAK_HOST="$(oc get route -n "${NAMESPACE}" mate-keycloak -o jsonpath='{.spec.host}')"
oc patch keycloak -n "${NAMESPACE}" mate-keycloak --type merge -p "{\"spec\":{\"hostname\":{\"hostname\":\"${KEYCLOAK_HOST}\"}}}" >/dev/null
oc delete pod -n "${NAMESPACE}" mate-keycloak-0 >/dev/null 2>&1 || true

echo "Waiting for Keycloak to be Ready"
oc wait -n "${NAMESPACE}" --for=condition=Ready pod/mate-keycloak-0 --timeout=10m >/dev/null
oc wait -n "${NAMESPACE}" --for=condition=Ready keycloak/mate-keycloak --timeout=20m

echo "[8/9] Discovering routes and updating app + realm configuration"
PORTAL_HOST="$(oc get route -n "${NAMESPACE}" portal -o jsonpath='{.spec.host}')"
BACKEND_HOST="$(oc get route -n "${NAMESPACE}" mate-backend -o jsonpath='{.spec.host}')"

PORTAL_URL="https://${PORTAL_HOST}"
BACKEND_URL="https://${BACKEND_HOST}"
KEYCLOAK_AUTHORITY="https://${KEYCLOAK_HOST}/realms/mate-bank"

echo "Pinning Keycloak public hostname to: ${KEYCLOAK_HOST}"
oc patch keycloak -n "${NAMESPACE}" mate-keycloak --type merge -p "{\"spec\":{\"hostname\":{\"hostname\":\"${KEYCLOAK_HOST}\"}}}" >/dev/null

oc patch configmap -n "${NAMESPACE}" mate-app-config --type merge -p "{\"data\":{\"FRONTEND_PUBLIC_URL\":\"${PORTAL_URL}\",\"BACKEND_PUBLIC_URL\":\"${BACKEND_URL}\",\"KEYCLOAK_AUTHORITY\":\"${KEYCLOAK_AUTHORITY}\"}}"

REDIRECT_URIS_JSON="[\"${PORTAL_URL}/*\"]"
WEB_ORIGINS_JSON="[\"${PORTAL_URL}\"]"
oc patch secret -n "${NAMESPACE}" mate-demo-config --type merge -p "{\"stringData\":{\"MATE_PORTAL_REDIRECT_URIS_JSON\":\"${REDIRECT_URIS_JSON}\",\"MATE_PORTAL_WEB_ORIGINS_JSON\":\"${WEB_ORIGINS_JSON}\"}}"

echo "  Portal:   ${PORTAL_URL}"
echo "  Backend:  ${BACKEND_URL}"
echo "  Keycloak: https://${KEYCLOAK_HOST}"

echo "Re-applying Keycloak realm configuration"
oc delete job -n "${NAMESPACE}" mate-keycloak-config --ignore-not-found=true
oc apply -n "${NAMESPACE}" -f deploy/base/keycloak/keycloak-config-job.yaml >/dev/null
oc wait -n "${NAMESPACE}" --for=condition=complete job/mate-keycloak-config --timeout=10m

echo "Ensuring Keycloak client redirect URIs match the current portal route"
KC_ADMIN_USER="$(oc get secret -n "${NAMESPACE}" mate-keycloak-initial-admin -o jsonpath='{.data.username}' | base64 --decode)"
KC_ADMIN_PASSWORD="$(oc get secret -n "${NAMESPACE}" mate-keycloak-initial-admin -o jsonpath='{.data.password}' | base64 --decode)"
KC_BASE="https://${KEYCLOAK_HOST}"

KC_TOKEN="$(
  curl -skS "${KC_BASE}/realms/master/protocol/openid-connect/token" \
    -d grant_type=password \
    -d client_id=admin-cli \
    -d username="${KC_ADMIN_USER}" \
    -d password="${KC_ADMIN_PASSWORD}" \
  | jq -r .access_token
)"

if [[ -z "${KC_TOKEN}" || "${KC_TOKEN}" == "null" ]]; then
  echo "ERROR: could not obtain Keycloak admin token (check mate-keycloak-initial-admin secret)" >&2
  exit 1
fi

MATE_PORTAL_CLIENT_UUID="$(
  curl -skS "${KC_BASE}/admin/realms/mate-bank/clients?clientId=mate-portal" \
    -H "Authorization: Bearer ${KC_TOKEN}" \
  | jq -r '.[0].id'
)"

if [[ -z "${MATE_PORTAL_CLIENT_UUID}" || "${MATE_PORTAL_CLIENT_UUID}" == "null" ]]; then
  echo "ERROR: could not find Keycloak client mate-portal in realm mate-bank" >&2
  exit 1
fi

MATE_PORTAL_CLIENT_JSON="$(
  curl -skS "${KC_BASE}/admin/realms/mate-bank/clients/${MATE_PORTAL_CLIENT_UUID}" \
    -H "Authorization: Bearer ${KC_TOKEN}"
)"

MATE_PORTAL_CLIENT_UPDATED="$(
  echo "${MATE_PORTAL_CLIENT_JSON}" \
  | jq --arg ru "${PORTAL_URL}/*" --arg wo "${PORTAL_URL}" '.redirectUris=[$ru] | .webOrigins=[$wo]'
)"

curl -skS -X PUT "${KC_BASE}/admin/realms/mate-bank/clients/${MATE_PORTAL_CLIENT_UUID}" \
  -H "Authorization: Bearer ${KC_TOKEN}" \
  -H "Content-Type: application/json" \
  --data-binary "${MATE_PORTAL_CLIENT_UPDATED}" \
  >/dev/null

echo "[9/9] Building and deploying app images (binary builds)"
oc start-build -n "${NAMESPACE}" bc/mate-backend --from-dir=apps/backend --follow
oc start-build -n "${NAMESPACE}" bc/mate-portal --from-dir=apps/frontend --follow

echo "Updating Deployments to the newly built images"
oc set image -n "${NAMESPACE}" deployment/mate-backend backend="${REGISTRY}/mate-backend:latest" >/dev/null
oc set image -n "${NAMESPACE}" deployment/mate-portal portal="${REGISTRY}/mate-portal:latest" >/dev/null

echo "Scaling apps up"
oc scale -n "${NAMESPACE}" deployment/mate-backend deployment/mate-portal --replicas=1 >/dev/null
oc rollout status -n "${NAMESPACE}" deployment/mate-backend --watch=true --timeout=5m >/dev/null
oc rollout status -n "${NAMESPACE}" deployment/mate-portal --watch=true --timeout=5m >/dev/null

echo
echo "Routes:"
oc get route -n "${NAMESPACE}"
echo
echo "Tip: open the portal route and sign in with LDAP users:"
echo "  - maria.gomez / mariaPassword (branch-tellers, auditors)"
echo "  - sofia.fernandez / sofiaPassword (risk-analysts, auditors)"
echo "  - juan.perez / juanPassword (branch-tellers)"
echo "  - diego.rodriguez / diegoPassword (credit-approvers)"
echo "  - it.admin / itAdminPassword (it-admins)"
echo "  - nico.paz / nicoPassword (no groups)"
