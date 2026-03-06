# MateBank Demo: Red Hat Build of Keycloak + LDAP + ASP.NET Core 10 on OpenShift 4.20

This repository contains a demo reference implementation for a banking scenario where the organization **currently uses LDAP for authorization** (group membership) and wants to understand how that looks with **OIDC + JWT**, **keeping LDAP groups** as the source of truth for authorization decisions.

## Architecture (high level)

```mermaid
flowchart LR
  User[BankUser] --> Frontend[portal (ASP.NET Core 10)]
  Frontend -->|"OIDC Auth Code + PKCE"| Keycloak[RHBK (Keycloak)]
  Keycloak -->|"LDAP user + group federation"| LDAP[LDAP (generic)]
  Frontend -->|"API call with access token (JWT)"| Backend[backend (ASP.NET Core 10)]
  Backend -->|"JWT validation + authz policies (groups claim)"| Backend
```

### Components
- **LDAP**: Hosts users and groups (demo directory). Group membership is the authorization indicator (e.g., `bank-tellers`, `risk-analysts`).
- **Red Hat Build of Keycloak (RHBK)**: Acts as the OIDC provider and federates users/groups from LDAP.
- **Frontend (ASP.NET Core 10)**: Browser-facing application. Performs OIDC login against Keycloak and calls the backend with a bearer access token.
- **Backend (ASP.NET Core 10 Web API)**: Validates JWTs issued by Keycloak and enforces authorization policies based on LDAP-derived group claims.
- **OpenShift GitOps (Argo CD)**: Deploys everything as Git-managed desired state (app-of-apps pattern).
- **Ansible**: Bootstraps cluster prerequisites (operators, namespaces, RBAC) and wires Argo CD applications.

## How LDAP, Keycloak, and the apps interact

### Identity and authorization source of truth
- **Identity** (username/password) is validated by Keycloak, but **users are stored in LDAP** via user federation.
- **Authorization** is driven by **LDAP group membership**.
- Keycloak maps LDAP groups into OIDC tokens as a claim (for example, `groups`), which the backend consumes.

### Authentication flow (OIDC)
1. User opens the frontend.
2. Frontend redirects the user to Keycloak using **Authorization Code flow + PKCE**.
3. Keycloak authenticates the user (federated against LDAP).
4. Keycloak issues tokens (ID token + access token). The access token is a **JWT**.
5. Frontend calls the backend with `Authorization: Bearer <access_token>`.

### Authorization flow (JWT + LDAP groups)
1. Backend validates:
   - **Issuer** (`iss`) points to the Keycloak realm.
   - **Signature** using Keycloak realm keys (JWKS).
   - **Audience** (`aud`) matches the API client/audience.
2. Backend evaluates authorization policies based on a claim (e.g., `groups`), which represents **LDAP group membership** synchronized/mapped by Keycloak.
3. Backend returns `200` or `403` based on group membership (demo endpoints per group).

## Developer guide: Keycloak integration + group-based profiling in .NET

This section explains the integration from a developer perspective: what is configured in Keycloak, what the frontend does during login, and how the backend authorizes requests based on LDAP group membership exposed as token claims.

### Keycloak side (realm, LDAP federation, and token claims)

The realm is configured-as-code and applied by `keycloak-config-cli`:
- **Realm template**: `deploy/base/keycloak/keycloak-config-configmap.yaml` (`mate-bank-realm.json`)
- **Apply job**: `deploy/base/keycloak/keycloak-config-job.yaml`

Key pieces:

- **LDAP federation (users + groups)**:
  - Keycloak uses a User Storage Provider (LDAP) to authenticate users against LDAP.
  - The LDAP group mapper (`group-ldap-mapper`) imports group membership so Keycloak can evaluate groups for a federated user.

- **Group membership → `groups` claim**:
  - For the `mate-portal` client, the realm config includes a protocol mapper:
    - `oidc-group-membership-mapper`
    - `claim.name = "groups"`
    - emitted into **access token** and **user info**
  - That makes LDAP-derived membership available to apps as a standard JSON array claim (for example: `["/credit-approvers", "/auditors"]`).

- **Audience for the backend API (`aud`)**:
  - Access tokens minted for `mate-portal` must also be accepted by the backend.
  - The realm config adds an `oidc-audience-mapper` that includes `mate-backend` in the access token audience.
  - The backend validates the audience via `Auth:Audience`.

- **Redirect URIs and PKCE**:
  - The `mate-portal` client is configured for Authorization Code + PKCE (`S256`).
  - Allowed `redirectUris` and `webOrigins` must match the portal route host; otherwise Keycloak will reject login with `invalid_redirect_uri`.

### Frontend (.NET): OIDC login + group claim mapping + calling the backend

The frontend is a Razor Pages app that uses:
- **Cookie auth** for the local session
- **OpenID Connect** as the challenge scheme (Authorization Code + PKCE)

Implementation highlights:

- **OIDC setup**: `apps/frontend/Program.cs`
  - `ResponseType = code`, `UsePkce = true`, `SaveTokens = true`
  - `GetClaimsFromUserInfoEndpoint = true` (so the app can fetch additional claims from Keycloak)

- **Critical piece: map the `groups` claim**:
  - Keycloak emits LDAP groups as a custom JSON claim named `groups`.
  - ASP.NET Core maps only a default set of OIDC claims; custom JSON claims need explicit mapping:
    - `options.ClaimActions.MapJsonKey("groups", "groups");`

- **Group “profiling” in the UI**:
  - The portal reads the group claim (`FrontendAuth:GroupClaimType`, default `groups`) from the signed-in user and shows which missions are authorized.
  - Missions are visible to all users, but **the backend enforces authorization**.

- **Calling the backend with the access token**:
  - In `apps/frontend/Pages/Missions.cshtml.cs`, the portal retrieves the `access_token` from the authentication session:
    - `HttpContext.GetTokenAsync("access_token")`
  - Then calls the backend with:
    - `Authorization: Bearer <access_token>`

### Backend (.NET): JWT validation + group-based authorization policies

The backend is a minimal API configured in `apps/backend/Program.cs`.

- **JWT Bearer authentication**:
  - `options.Authority = Auth:Authority` (Keycloak realm URL)
  - `ValidAudience = Auth:Audience` (expects `mate-backend` in `aud`)
  - `MapInboundClaims = false` (avoids legacy claim type remapping)
  - `NameClaimType = preferred_username` (so `user.Identity.Name` is meaningful)

- **Authorization policies based on LDAP groups**:
  - Policies are defined as “has group X” checks using the `groups` claim:
    - `/branch-tellers`, `/risk-analysts`, `/credit-approvers`, `/auditors`, `/it-admins`
  - Each demo endpoint enforces the corresponding policy:
    - `/api/tellers` → `TellersOnly`
    - `/api/risk` → `RiskOnly`
    - `/api/credit` → `CreditApproversOnly`
    - `/api/audit` → `AuditorsOnly`
    - `/api/admin` → `AdminsOnly`

### End-to-end mental model (what to explain in a dev handover)

- **LDAP** remains the authorization system-of-record: change group membership in LDAP.
- **Keycloak** federates LDAP and emits group membership into tokens as `groups`.
- **Frontend** authenticates users (OIDC) and forwards the access token to the backend.
- **Backend** is the final decision point: it validates the JWT and enforces authorization policies based on `groups`.

### Common pitfalls (and what this repo already does)

- **“No groups claim found” in the frontend**:
  - Fix: ensure `MapJsonKey("groups","groups")` and `GetClaimsFromUserInfoEndpoint = true`.

- **`invalid_redirect_uri` from Keycloak**:
  - Fix: make `redirectUris/webOrigins` match the portal host (route).

- **Group naming mismatch**:
  - This demo uses full path group names (values like `/credit-approvers`), and normalizes to ensure the leading `/` is handled consistently.

## GitOps and automation model

```mermaid
flowchart TB
  Git[Git repo] --> Argo[OpenShift GitOps (Argo CD)]
  Argo --> LDAPRes[LDAP resources]
  Argo --> KeycloakRes[Keycloak + DB + realm config]
  Argo --> AppsRes[frontend + backend]
  Ansible[Ansible bootstrap] --> Argo
```

- **Argo CD** is the single source of truth for deployed resources.
- **Ansible** is used once (or occasionally) to set up the platform (operators, namespaces, RBAC), then GitOps takes over.

## Security posture (demo defaults; bank-friendly)
- **No real data**: demo users and groups only.
- **TLS everywhere**: routes for Keycloak and frontend; backend typically internal route or cluster-only service.
- **Principle of least privilege**: separate namespaces and service accounts.
- **Operator upgrades**: recommended **manual approval** for RHBK operator upgrades (avoid unintended migrations).

## Repository structure
- `apps/frontend/`: ASP.NET Core 10 Razor Pages app (OIDC login, token acquisition, API calls)
- `apps/backend/`: ASP.NET Core 10 minimal API (JWT validation, group-based authorization)
- `deploy/base/`: Kustomize bases for LDAP, Postgres, Keycloak, and the apps
- `deploy/overlays/mate-bank-demo/`: Demo overlay (namespace only; hostnames discovered at deploy time)
- `infra/olm/`: Operator installation manifests (RHBK Operator)
- `scripts/`: One-command install/deploy + test helpers

## Deployment entry points
- **One-command deploy (recommended for workshops/demos)**: `scripts/deploy-mate-bank-demo.sh`
- **Operator install only**: `scripts/install-rhbk-operator.sh`

## Where LDAP and Keycloak are configured as code
- **LDAP server**: `deploy/base/ldap/` (389 Directory Server container)
- **Keycloak realm configuration** (clients, mappers, LDAP federation): `deploy/base/keycloak/keycloak-config-configmap.yaml` (applied by `keycloak-config-cli`)
- **Config application job**: `deploy/base/keycloak/keycloak-config-job.yaml` (runs `keycloak-config-cli` after Keycloak is reachable)

## How to access LDAP (admin)
This demo uses 389 Directory Server. To inspect the directory from your laptop:

```bash
./scripts/ldap-info.sh
oc -n mate-bank-demo port-forward statefulset/dirsrv 3389:3389
```

- **Visual browser** (recommended): use *Apache Directory Studio* and connect to `ldap://localhost:3389`
  - **Bind DN**: `cn=Directory Manager`
  - **Password**: printed by `./scripts/ldap-info.sh`
  - **Base DN**: `dc=mate,dc=bank,dc=demo`

## Demo users and authorization groups (LDAP)
LDAP remains the system of record. Keycloak federates LDAP and emits group membership as the `groups` claim in the access token.

- **Users**:
  - `maria.gomez` / `mariaPassword` → `/branch-tellers`, `/auditors`
  - `sofia.fernandez` / `sofiaPassword` → `/risk-analysts`, `/auditors`
  - `juan.perez` / `juanPassword` → `/branch-tellers`
  - `diego.rodriguez` / `diegoPassword` → `/credit-approvers`
  - `it.admin` / `itAdminPassword` → `/it-admins`
  - `nico.paz` / `nicoPassword` → *(no LDAP groups; negative authorization demo)*

## How to deploy on OpenShift 4.20
Prereqs:
- Logged in with `oc` as a cluster admin (or with permissions for Operator installs and SCC assignment)
- `jq` installed locally (for the helper scripts)

Deploy:

```bash
./scripts/deploy-mate-bank-demo.sh
```

## How to test (portal + API + Swagger)
- **Portal**: open the route printed by the deploy script (host starts with `portal-...`) and sign in with any demo user.
- **Swagger UI** (backend): open the backend route printed by the deploy script and append `/swagger/`, then use a token from the helper script in the `Authorize` button.

## Live demo step: add a new LDAP user and use it immediately
You can add a brand new user directly in LDAP, attach them to existing groups, trigger Keycloak sync, and then sign in to the portal.

Example (create a new teller user):

```bash
LDAP_USERNAME="camila.rios" LDAP_PASSWORD="camilaPassword" LDAP_GROUPS="branch-tellers" ./scripts/add-ldap-user.sh
TARGET_USERNAME="camila.rios" ./scripts/keycloak-sync-and-check-user.sh
```

Then sign in to the portal as `camila.rios / camilaPassword` and open **Missions** to see authorization driven by LDAP groups.

### Get a token (password grant client for testing)
This is only for demo/testing convenience; the portal uses Authorization Code + PKCE.

```bash
KC_USERNAME="maria.gomez" KC_PASSWORD="mariaPassword" ./scripts/get-token.sh | head -c 30 && echo
```

### Call the API with a token

```bash
KC_USERNAME="maria.gomez" KC_PASSWORD="mariaPassword" ./scripts/call-backend.sh /api/me | jq .
KC_USERNAME="maria.gomez" KC_PASSWORD="mariaPassword" ./scripts/call-backend.sh /api/tellers
KC_USERNAME="sofia.fernandez" KC_PASSWORD="sofiaPassword" ./scripts/call-backend.sh /api/risk
KC_USERNAME="maria.gomez" KC_PASSWORD="mariaPassword" ./scripts/call-backend.sh /api/audit
```

## What this demo will prove (expected outcome)
- LDAP remains the **system of record** for groups.
- Keycloak provides **OIDC** for applications.
- The backend can authorize requests using **JWT claims derived from LDAP groups**, bridging the current LDAP-based authorization model to an OIDC/JWT world.