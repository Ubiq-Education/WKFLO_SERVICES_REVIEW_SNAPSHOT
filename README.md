# WKFLO Services

WKFLO Services is the .NET backend for a multi-tenant AI agent platform. It provides APIs for agent configuration and chat orchestration, a BFF proxy, and MCP servers for agent tools.

# Hotfix images

The container build pipeline (`pipelines/build-release.yml`) tags images by the branch it runs on. The tagging is done by the shared `build-push.yml` template in `infra-libraries`.

| Branch | Image tags | Deployed automatically |
|--------|------------|------------------------|
| `main` | `<BuildId>`, `latest` | Staging (`UpdateStaging`) |
| `hotfix/*` | `<BuildId>-hotfix` | No |
| Any other branch | `<BuildId>-preview` | No |

Only `main` moves `latest`. Hotfix and preview images are not automatically deployed to staging, but can be selected using the manual staging override.

To release an emergency fix to production without the other changes being tested in staging:

1. Create `hotfix/<work-item>` from the commit that is running in production, and commit the fix there.
2. Run the service's build pipeline manually on the hotfix branch (default Run Type). Only the `Build` stage runs; `UpdateStaging` is skipped. The image is pushed as `<BuildId>-hotfix`.
3. Merge the hotfix branch into `main` with a PR, so the fix is also in the updates being tested in staging.
4. Give the `<BuildId>-hotfix` tag to the Ops team, who set it as the production image through the ControlMonkey variables.

A hotfix that needs a database change can't use this flow yet: `sql-release.yml` only releases the dacpac from `main` builds.
