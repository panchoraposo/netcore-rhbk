#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-demo}"
BASEDN="${BASEDN:-dc=mate,dc=bank,dc=demo}"
DM_DN="${DM_DN:-cn=Directory Manager}"

DM_PASSWORD="$(oc get secret -n "${NAMESPACE}" dirsrv-dm-password -o jsonpath='{.data.dm-password}' | base64 --decode)"

echo "LDAP (389ds) access info"
echo
echo "Namespace: ${NAMESPACE}"
echo "Base DN:   ${BASEDN}"
echo "Bind DN:   ${DM_DN}"
echo "Password:  ${DM_PASSWORD}"
echo
echo "In-cluster URL:"
echo "  ldap://dirsrv.${NAMESPACE}.svc:3389"
echo
echo "Local port-forward (then connect to ldap://localhost:3389):"
echo "  oc -n ${NAMESPACE} port-forward statefulset/dirsrv 3389:3389"
echo
echo "Quick ldapsearch (after port-forward):"
echo "  ldapsearch -x -H ldap://localhost:3389 -D \"${DM_DN}\" -w \"${DM_PASSWORD}\" -b \"${BASEDN}\" \"(objectClass=*)\" dn"
