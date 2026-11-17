# Connecting a local WKFLO to a local DXP service

WKFLO's AI API and MD API talk to several AMAIS_DXP services (Ingress, MasterData, ...)
over HTTPS. By default your local WKFLO stack points at the **staging** DXP deployment.
This doc covers pointing it at a **DXP service you're running locally instead** (e.g. in
Visual Studio) — useful while developing a change that spans both repos, before it's
published/deployed anywhere.

## What's actually happening

- WKFLO's AI API (and MD API) run inside Docker containers.
- A DXP service you run locally (Visual Studio / IIS Express or `dotnet run`) listens on
  the **host** machine, not inside Docker — so the container reaches it via
  `host.docker.internal:<port>`, not `localhost`.
- That local DXP service serves HTTPS with a locally-issued dev certificate
  (`dotnet dev-certs` or [mkcert](https://github.com/FiloSottile/mkcert)). Docker
  containers don't trust your host's certificate store by default, so calls from WKFLO to
  `https://host.docker.internal:<port>` fail as untrusted **unless the container itself
  trusts that CA**.
- The AI API's `Dockerfile` bakes an extra CA into the container's trust store **at build
  time** (via `local-dev-ca/`) — not by exec'ing into the running container, because
  Visual Studio's debug-mode compose overlay (`docker-compose.vs.debug.g.yml`) hardcodes
  its own `entrypoint`, which would silently undo a runtime fix. Baking it into the image
  is the only approach that survives container recreation and VS debug launches.
- `local-dev-ca/` is empty (just a `.gitkeep`) by default, so none of this affects anyone
  who hasn't dropped a CA file in there — the `Dockerfile` step is a no-op for them.

## Manual steps

1. **Run the DXP service using its Docker-accessible Kestrel profile, not IIS Express.**
   `AMAIS_DXP_INGRESS_API` already has one in its `launchSettings.json` —
   `"Kestrel (Docker-accessible)"`. It differs from the default "IIS Express" profile in
   two ways that both matter here:
   - `"applicationUrl": "http://0.0.0.0:34930;https://0.0.0.0:44344"` — binds to all
     interfaces. IIS Express only binds to `localhost`, which the Docker container simply
     can't reach via `host.docker.internal` no matter what CA you trust.
   - `"Kestrel__Certificates__Default__Path": "certs/host-docker-internal.pfx"` — serves a
     cert issued for the `host.docker.internal` hostname specifically, not `localhost`.
     TLS validates the hostname too, so a `localhost` cert would still fail even with the
     right CA trusted.

   If the DXP service you need doesn't have an equivalent profile yet, add one to its
   `launchSettings.json` following Ingress's as a template (same shape, different ports).

   If `certs/host-docker-internal.pfx` doesn't exist yet for that project (it's gitignored
   — `*.pfx` — so it's never committed, each dev generates their own), generate it with
   mkcert from the project's root, matching the profile's expected password:
   ```
   mkcert -pkcs12 -p12-file certs/host-docker-internal.pfx host.docker.internal
   ```
   mkcert's PKCS12 export defaults to password `changeit`, which is exactly what the
   profile is already configured to expect — no extra config needed.

   Launch the service with this profile and note the HTTPS port it's actually using
   (Ingress's is `44344`; a different service will likely use a different port).

2. **Make the AI API container trust your local dev certificate:**
   - Install [mkcert](https://github.com/FiloSottile/mkcert) if you don't have it, then
     run `mkcert -install` once (this also makes your own browser/OS trust it).
   - Find mkcert's root CA folder: `mkcert -CAROOT` — it contains `rootCA.pem`.
   - Copy that file into `local-dev-ca/rootCA.crt` in this repo (**must be renamed to
     `.crt`** — Debian's `update-ca-certificates`, which the `Dockerfile` runs, only
     scans `*.crt` files).
   - Rebuild the AI API image so the CA gets baked in:
     ```
     docker compose build amais-wkflo-ai-webapi
     ```

3. **Point `.env` at your local DXP instance instead of staging.** Open `.env` at the repo
   root and find the block for the DXP service you're running locally, e.g.:
   ```
   # DXP Ingress
   AMAIS_DXP_INGRESS_SERVER_URL=https://amais-dxp-ingress-staging.azurewebsites.net
   AMAIS_DXP_INGRESS_MI_CLIENT_ID=3cbdbab6-4e59-4170-b301-6b30480d86d1
   AMAIS_DXP_INGRESS_AUDIENCE=api://ce91d5ac-a24b-4173-b4f6-c30bbd6b660f/.default

   # DXP Ingress local
   # AMAIS_DXP_INGRESS_SERVER_URL=https://host.docker.internal:44344
   # AMAIS_DXP_INGRESS_MI_CLIENT_ID=3cbdbab6-4e59-4170-b301-6b30480d86d1
   # AMAIS_DXP_INGRESS_AUDIENCE=api://ce91d5ac-a24b-4173-b4f6-c30bbd6b660f/.default
   ```
   Comment out the staging block and uncomment the local one (adjust the port to match
   step 1). `MI_CLIENT_ID`/`AUDIENCE` identify the *calling* client, not the destination —
   they stay the same in both blocks. If your `.env` doesn't have a "local" block yet for
   the DXP service you need, add one following this same pattern.

4. **Recreate the container** to pick up both the new image and the new `.env`:
   ```
   docker compose up -d --build amais-wkflo-ai-webapi
   ```

5. **When you're done**, swap the `.env` block back to staging (comment the local one out
   again) and recreate the container. `.env` is gitignored — this never gets committed —
   but leaving it pointed at your local machine will silently break anyone else who runs
   `docker compose` from a stale mental model of "wkflo talks to staging."

## Prompt to hand your agent instead

Paste this to Claude Code from the `wkflo_services` repo, filling in the DXP service name
and port:

```
I want to point my local wkflo_services stack at a locally-running AMAIS_DXP <SERVICE
NAME> service instead of staging, following LOCAL_DXP_CONNECTION.md in this repo, from
both the wkflo_services and AMAIS_DXP sides. In AMAIS_DXP: check whether
<SERVICE NAME>'s WebAPI project already has a "Kestrel (Docker-accessible)" launch
profile (binds 0.0.0.0, serves certs/host-docker-internal.pfx) — if not, add one
following AMAIS_DXP_INGRESS_API's as a template, and tell me the port it ends up on. Also
check whether certs/host-docker-internal.pfx already exists for that project — if not,
tell me to run `mkcert -pkcs12 -p12-file certs/host-docker-internal.pfx
host.docker.internal` from the project root myself (it's gitignored, so I need to
generate my own). In wkflo_services: check whether local-dev-ca/rootCA.crt already exists
and is a real cert (not the empty .gitkeep placeholder) before deciding whether the AI
API image needs rebuilding — if it's missing, tell me to run `mkcert -CAROOT` and copy
the file in myself before you rebuild. Then do the .env toggle (add a "local" block for
this service if one doesn't exist yet, following the existing DXP Ingress pattern) and
recreate the container.
```
