## Windows Kerberos demo overlay

This overlay assumes **stable hostnames**, which are required for Kerberos SPNs/keytabs:

- `sso.matebank.demo` (Keycloak / RHBK)
- `portal.matebank.demo` (frontend)
- `api.matebank.demo` (backend)

### DNS requirements (demo)

For **Windows integrated auth (SPNEGO)** to work, the workstation must resolve the Keycloak hostname to the OpenShift router.

In this overlay, the Samba AD DC VM tries to add an **A record** in its DNS zone for:

- `sso.matebank.demo` → `OPENSHIFT_ROUTER_IP`

By default, `OPENSHIFT_ROUTER_IP` is set to the TEST-NET value `192.0.2.10` inside the Samba VM provisioning script. Change it to your real OpenShift ingress/router IP (or load balancer VIP).

### Keytab

The committed Secret `kerberos-keytab` is a **placeholder**. Replace it with a real keytab containing the SPN:

- `HTTP/sso.matebank.demo@MATEBANK.DEMO`

