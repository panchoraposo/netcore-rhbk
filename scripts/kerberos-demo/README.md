## Kerberos demo helper scripts

- `pull-keytab-and-apply-secret.sh`: copies `/root/http.keytab` from the `samba-ad` VM and recreates the `kerberos-keytab` Secret (binary-safe).

Environment variables:
- `NAMESPACE` (default `mate-bank-kerberos-demo`)
- `VM_NAME` (default `samba-ad`)
- `KEYTAB_LOCAL_PATH` (default `./http.keytab`)
