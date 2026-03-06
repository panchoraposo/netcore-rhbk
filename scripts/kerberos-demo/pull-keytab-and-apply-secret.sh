#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-mate-bank-kerberos-demo}"
VM_NAME="${VM_NAME:-samba-ad}"
KEYTAB_LOCAL_PATH="${KEYTAB_LOCAL_PATH:-./http.keytab}"

echo "This script pulls /root/http.keytab from the Samba AD VM and applies it as the 'kerberos-keytab' Secret."
echo
echo "Prereqs:"
echo "- OpenShift Virtualization installed (VM running)"
echo "- 'virtctl' available in PATH (for scp)"
echo "- Logged into the cluster with 'oc'"
echo

if ! command -v virtctl >/dev/null 2>&1; then
  echo "ERROR: virtctl not found in PATH."
  exit 1
fi

echo "[1/2] Copying keytab from VM to: ${KEYTAB_LOCAL_PATH}"
virtctl -n "${NAMESPACE}" scp --local-ssh=true --namespace "${NAMESPACE}" "root@${VM_NAME}:/root/http.keytab" "${KEYTAB_LOCAL_PATH}"

echo "[2/2] Creating/updating Secret kerberos-keytab"
oc -n "${NAMESPACE}" create secret generic kerberos-keytab \
  --from-file=http.keytab="${KEYTAB_LOCAL_PATH}" \
  --dry-run=client -o yaml | oc apply -f -

echo
echo "Done. Restart Keycloak pod to re-read the mounted keytab if needed."
