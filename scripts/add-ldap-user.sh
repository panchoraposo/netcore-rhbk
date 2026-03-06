#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-demo}"
BASEDN="${BASEDN:-dc=mate,dc=bank,dc=demo}"
DM_DN="${DM_DN:-cn=Directory Manager}"

LDAP_USERNAME="${LDAP_USERNAME:-}"
LDAP_PASSWORD="${LDAP_PASSWORD:-}"
LDAP_GROUPS_CSV="${LDAP_GROUPS:-}" # e.g. "branch-tellers,auditors"

if [[ -z "${LDAP_USERNAME}" || -z "${LDAP_PASSWORD}" ]]; then
  echo "Usage:"
  echo "  LDAP_USERNAME='new.user' LDAP_PASSWORD='Passw0rd!' LDAP_GROUPS='branch-tellers,auditors' ./scripts/add-ldap-user.sh"
  exit 2
fi

CN="${CN:-${LDAP_USERNAME}}"
SN="${SN:-User}"
EMAIL="${EMAIL:-${LDAP_USERNAME}@matebank.demo}"

DM_PASSWORD="$(oc get secret -n "${NAMESPACE}" dirsrv-dm-password -o jsonpath='{.data.dm-password}' | base64 --decode)"
USER_DN="uid=${LDAP_USERNAME},ou=People,${BASEDN}"

echo "Ensuring LDAP suffix exists: ${BASEDN}"
oc exec -n "${NAMESPACE}" dirsrv-0 -- dsconf localhost backend create \
  --suffix "${BASEDN}" \
  --be-name userroot \
  --create-suffix \
  --create-entries >/dev/null 2>&1 || true

echo "Ensuring OUs exist (People/Groups)"
oc exec -i -n "${NAMESPACE}" dirsrv-0 -- ldapadd -c -x -H ldap://localhost:3389 -D "${DM_DN}" -w "${DM_PASSWORD}" >/dev/null 2>&1 <<EOF || true

dn: ou=People,${BASEDN}
objectClass: organizationalUnit
ou: People

dn: ou=Groups,${BASEDN}
objectClass: organizationalUnit
ou: Groups

EOF

echo "Adding LDAP user: ${USER_DN}"

oc exec -i -n "${NAMESPACE}" dirsrv-0 -- ldapadd -c -x -H ldap://localhost:3389 -D "${DM_DN}" -w "${DM_PASSWORD}" >/dev/null 2>&1 <<EOF || true

dn: ${USER_DN}
objectClass: inetOrgPerson
uid: ${LDAP_USERNAME}
cn: ${CN}
sn: ${SN}
mail: ${EMAIL}
userPassword: ${LDAP_PASSWORD}

EOF

if [[ -n "${LDAP_GROUPS_CSV}" ]]; then
  IFS=',' read -r -a LDAP_GROUP_LIST <<< "${LDAP_GROUPS_CSV}"
  for g in "${LDAP_GROUP_LIST[@]}"; do
    g="$(echo "$g" | xargs)"
    [[ -z "$g" ]] && continue
    GROUP_DN="cn=${g},ou=Groups,${BASEDN}"
    echo "Adding membership: ${GROUP_DN} <= ${USER_DN}"
    oc exec -i -n "${NAMESPACE}" dirsrv-0 -- ldapmodify -c -x -H ldap://localhost:3389 -D "${DM_DN}" -w "${DM_PASSWORD}" >/dev/null 2>&1 <<EOF || true

dn: ${GROUP_DN}
changetype: modify
add: member
member: ${USER_DN}

EOF
  done
fi

echo
echo "Result (ldapsearch):"
oc exec -n "${NAMESPACE}" dirsrv-0 -- ldapsearch -x -H ldap://localhost:3389 -D "${DM_DN}" -w "${DM_PASSWORD}" -b "${USER_DN}" dn uid mail | sed -n '1,80p'

echo
echo "Groups containing this user:"
oc exec -n "${NAMESPACE}" dirsrv-0 -- ldapsearch -x -H ldap://localhost:3389 -D "${DM_DN}" -w "${DM_PASSWORD}" -b "ou=Groups,${BASEDN}" "(member=${USER_DN})" cn | awk '/^cn: /{print}' || true
