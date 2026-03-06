#!/usr/bin/env bash
set -euo pipefail

echo "[1/3] Checking cluster access"
oc whoami >/dev/null

echo "[2/3] Installing Red Hat Build of Keycloak Operator (if needed)"
if oc get subscription.operators.coreos.com -n openshift-operators rhbk-operator >/dev/null 2>&1; then
  echo "Found an existing rhbk-operator Subscription in openshift-operators."
  echo "RHBK Operator does not support AllNamespaces install mode; removing the failed install to re-install in a dedicated operator namespace."
  oc delete subscription.operators.coreos.com -n openshift-operators rhbk-operator --ignore-not-found=true
  oc delete csv.operators.coreos.com -n openshift-operators -l operators.coreos.com/rhbk-operator.openshift-operators= --ignore-not-found=true
fi

oc apply -f infra/olm/rhbk-operator/namespace.yaml
oc apply -f infra/olm/rhbk-operator/operatorgroup.yaml

if oc get subscription.operators.coreos.com -n mate-rhbk-operator rhbk-operator >/dev/null 2>&1; then
  echo "Subscription rhbk-operator already exists in mate-rhbk-operator."
else
  oc apply -f infra/olm/rhbk-operator/subscription.yaml
fi

echo "[3/3] Waiting for operator CSV to succeed"
CSV="$(oc get subscription.operators.coreos.com -n mate-rhbk-operator rhbk-operator -o jsonpath='{.status.currentCSV}' || true)"
if [[ -z "${CSV}" ]]; then
  echo "Waiting for subscription to report currentCSV..."
  sleep 10
  CSV="$(oc get subscription.operators.coreos.com -n mate-rhbk-operator rhbk-operator -o jsonpath='{.status.currentCSV}')"
fi
echo "Current CSV: ${CSV}"
oc wait -n mate-rhbk-operator --for=jsonpath='{.status.phase}'=Succeeded "csv.operators.coreos.com/${CSV}" --timeout=15m

echo "Done."

