# Demo bancaria (MateBank): RHBK (Keycloak) + LDAP + ASP.NET Core 10 en OpenShift 4.20

Este repo implementa una demo donde **LDAP sigue siendo la fuente de verdad** para usuarios y pertenencia a grupos (autorización), mientras que las aplicaciones pasan a autenticarse con **OIDC** y autorizar con **JWT** emitidos por Keycloak.

## Arquitectura (resumen)
- **LDAP**: usuarios/grupos (p.ej. `bank-tellers`, `risk-analysts`).
- **RHBK (Keycloak)**: federación con LDAP + emisión de tokens OIDC/JWT con un claim de grupos (p.ej. `groups`).
- **Frontend (.NET 10)**: login OIDC (Auth Code + PKCE) contra Keycloak y llamada al backend con access token.
- **Backend (.NET 10)**: valida JWT (issuer, firma, audience) y autoriza por grupos (claim `groups`).
- **Automatización**: scripts para instalar operador RHBK y desplegar la demo en un namespace dedicado.

## Qué demostrar (en la reunión)
- “Hoy autorizan con LDAP” → la pertenencia a grupos se mantiene en LDAP.
- “Quieren ver OIDC + JWT” → el frontend autentica con OIDC y el backend autoriza con JWT.
- “Mantener grupos LDAP como indicadores de autorización” → Keycloak federará LDAP y mapeará grupos a claims consumibles por el backend.

## Deploy rápido en OpenShift

```bash
./scripts/deploy-mate-bank-demo.sh
```

## Acceso a LDAP (admin) + vista “visual”

```bash
./scripts/ldap-info.sh
oc -n mate-bank-demo port-forward statefulset/dirsrv 3389:3389
```

- Para ver el árbol de LDAP de forma visual: **Apache Directory Studio** conectando a `ldap://localhost:3389`
  - **Bind DN**: `cn=Directory Manager`
  - **Password**: lo imprime `./scripts/ldap-info.sh`
  - **Base DN**: `dc=mate,dc=bank,dc=demo`

## Usuarios de demo (LDAP)
- `maria.gomez` / `mariaPassword` → branch-tellers, auditors
- `sofia.fernandez` / `sofiaPassword` → risk-analysts, auditors
- `juan.perez` / `juanPassword` → branch-tellers
- `diego.rodriguez` / `diegoPassword` → credit-approvers
- `it.admin` / `itAdminPassword` → it-admins
- `nico.paz` / `nicoPassword` → *(sin grupos LDAP; demo negativa de autorización)*

## Paso “en vivo” para la demo: agregar un usuario al LDAP
Podés crear un usuario nuevo en LDAP, asignarlo a un grupo existente y mostrar cómo Keycloak lo toma y luego se usa en el portal.

Ejemplo (crear un nuevo “teller”):

```bash
LDAP_USERNAME="camila.rios" LDAP_PASSWORD="camilaPassword" LDAP_GROUPS="branch-tellers" ./scripts/add-ldap-user.sh
TARGET_USERNAME="camila.rios" ./scripts/keycloak-sync-and-check-user.sh
```

Luego ingresá al portal como `camila.rios / camilaPassword` y abrí **Missions** para ver la autorización basada en grupos LDAP.

## Dónde está la documentación detallada
- La explicación completa de arquitectura, flujos OIDC/JWT y el modelo GitOps está en el `README.md` (en inglés).
- En particular, ver la sección **“Developer guide: Keycloak integration + group-based profiling in .NET”** para entender (desde el punto de vista del desarrollador) cómo:
  - Keycloak mapea **grupos de LDAP** al claim `groups`
  - el frontend hace login OIDC (Auth Code + PKCE) y consume el claim de grupos
  - el backend valida JWT y aplica políticas por pertenencia a grupos
