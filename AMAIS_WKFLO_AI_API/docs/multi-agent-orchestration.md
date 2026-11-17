# Multi-Agent Orchestration — Local Run & Testing

- **ADO Story:** #6480 — Dapr-based orchestration for multi-agent Claude workflows
- **ADO Task:** #6488 — Write local run + testing documentation (final task of the story)
- **Date:** 2026-07-08
- **Related docs:** [`docs/spikes/dapr-conversation-api.md`](spikes/dapr-conversation-api.md) (spike that preceded `AgentActor`, task #6489); [`docs/dapr-actors-workflows-activities.md`](dapr-actors-workflows-activities.md) (task #6553 — reviews when this pipeline should use a Dapr Actor vs. a plain Workflow Activity; concluded `AgentActor` never needed to be an actor and converted it to `IAgentInvoker`, a plain scoped service, referenced throughout this doc)

This is a sibling to `docs/spikes/` — not a spike. Everything described below is already built and merged into the working tree.

## 1. Architecture Overview

`IAgentInvoker`/`AgentInvoker` (`Application/Agents/Invocation/`) wraps a single runnable-agent invocation (`TenantAgent` or `PublishedTenantAgent`, via `IRunnableAISyncService`): it receives an `AgentInvocationRequest`, delegates to `InvokeAgentCommand` over the existing Semantic Kernel path, and returns a structured `AgentInvocationResult`. As of task #6553 this is a plain `Scoped` service, not a Dapr Actor — the original `AgentActor` was a fresh, one-shot RPC per call with no reminders/timers and no state surviving past the call, so it never needed the Actor runtime (see `docs/dapr-actors-workflows-activities.md` for the full reasoning and how this compares to the repo's one genuine actor, `ClassificationJobSchedulerActor`). `MultiAgentOrchestratorWorkflow` (`Application/Agents/Workflows/MultiAgentOrchestrator/`) is a Dapr Workflow that sequences one "lead" agent step, then fans out to two or more agents concurrently, then fans back in and aggregates every result — every call goes through `InvokeAgentWorkflowActivity` because Dapr Workflow replay determinism forbids external/side-effecting calls directly from workflow code. Separately, `AgentEventSubscriber` (`WebAPI/EndPoints/Dapr/AgentEventSubscriber.cs`) handles cross-agent async notifications (topic `work-ai-request-AgentEventMessage`, `AiWorkerTopicConsts.AgentEventMessageTopic`, on `pubsub-ai`) by scheduling its own small `AgentEventWorkflow` → `AgentEventWorkflowActivity` — the same subscriber-schedules-a-workflow shape every other pub/sub topic in this codebase uses, not a special case any more. `AgentEventWorkflowActivity` calls `IAgentInvoker` directly rather than going through `IWkfloMediator.Send`, the same as `InvokeAgentWorkflowActivity` (see Section 4a for why). The topic is published by `AgentEventPublish` (`WebAPI/EndPoints/Agent/AgentEventPublish.cs`, `POST /api/v1/agents/event`) — see Section 4 for the full request/response shape. It lives outside the `Tenant`/`Public` endpoint areas since the topic is conceptually agent-type-agnostic, and now genuinely supports both tenant agents and published tenant agents (see Section 4a) — though not the share-id-based public flow, which is a structurally different access pattern. Both the lead step and every fan-out call use a shared retry policy (3 attempts, exponential backoff 2s→4s→8s, capped at 30s, 2‑minute overall timeout) for transient/infrastructure failures, while business-level failures (bad agent id, model error) are already caught inside `AgentInvoker` and surfaced as `AgentInvocationResult.Success == false` instead of throwing. Tracing is wired at every hop: `InvokeAgentWorkflowActivity` and `AgentInvoker` both tag the ambient `Activity` with `wkflo.agent_role`, `wkflo.runnable_agent_id`, and `wkflo.orchestration_instance_id` so a fan-out is distinguishable in the trace viewer.

## 2. Local Run Instructions

The AI API's Dapr sidecar already runs as a container (`dapr-ai-1` in `docker-compose.yml`) wired to `--app-id amais-wkflo-ai-webapi`, `--dapr-http-port 3500`, `--dapr-grpc-port 50001`, pointed at `infra/dapr/components` and `infra/dapr/dapr-config.yaml`. The straightforward way to exercise the full pipeline locally is to bring up that whole stack:

```bash
docker compose -f docker-compose.yml -f docker-compose.override.yml up amais-wkflo-ai-webapi dapr-ai-1 redis rabbitmq placement scheduler alloy sqlserver
```

(add `amais-wkflo-bff-webapi` and `amais-wkflo-md-webapi` too if you need traffic to arrive via the BFF, or omit `-d` flags to watch logs). Confirm the AI API and its sidecar are healthy:

```bash
curl http://localhost:32774/api/v1/healthcheck   # AI API app container (HTTP port 32774:8080)
curl http://localhost:3500/v1.0/healthz          # AI API's Dapr sidecar (dapr-ai-1, mapped via network_mode: service:amais-wkflo-ai-webapi)
```

### Running the AI API process directly with `dapr run` (fast local iteration)

For iterating on workflow code without rebuilding the container, keep the shared infra running via docker-compose (`redis`, `rabbitmq`, `placement`, `scheduler`, `alloy`, `sqlserver` — i.e. everything except `amais-wkflo-ai-webapi`/`dapr-ai-1`), then run the AI API and its own sidecar via the Dapr CLI, mirroring the exact flags `dapr-ai-1` uses in `docker-compose.yml`:

```bash
dapr run \
  --app-id amais-wkflo-ai-webapi \
  --app-port 8080 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --placement-host-address localhost:6111 \
  --resources-path infra/dapr/components \
  --config infra/dapr/dapr-config.yaml \
  -- dotnet run --project AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.WebAPI
```

**Known gap:** `docker-compose.yml`'s `scheduler` service (required by Dapr Workflow/Actor reminders in Dapr ≥1.15) does not publish a host port — only `placement` does (`6111:6111`). A `dapr run` process on the host cannot reach the dockerized `scheduler` as-is. Either add a `ports: ["6222:6222"]` mapping to the `scheduler` service locally and pass `--scheduler-host-address localhost:6222` to `dapr run`, or run the AI API fully inside docker-compose (the first approach above) where everything shares `wkflonetwork2`. This doc does not change `docker-compose.yml` — flagging it here as a known local-dev friction point for whoever picks it up next.

## 3. Sample Input/Output

`MultiAgentOrchestratorWorkflowInput` — a lead "researcher" agent followed by a "writer" and a "fact-checker" running concurrently:

```json
{
  "input": {
    "text": "Summarize the latest quarterly earnings call for Acme Corp and produce a client-ready brief.",
    "files": []
  },
  "sessionId": "8f14e45f-ceea-467e-a2c4-a94a2e2f3f1c",
  "chainOfThought": true,
  "leadAgent": {
    "agentId": 4821,
    "agentRole": "researcher"
  },
  "fanOutAgents": [
    { "agentId": 4830, "agentRole": "writer" },
    { "agentId": 4842, "agentRole": "fact-checker" }
  ]
}
```

`input.files` (optional, `List<FileAttachment>`) is forwarded to every agent in the pipeline — lead and every fan-out participant — via the same mechanism the existing single-agent chat flow (`MessageTenantAgentByIdCommand`) already uses.

Resulting `MultiAgentOrchestratorWorkflowOutput`:

```json
{
  "orchestrationInstanceId": "9b1f2e7a-6d4c-4a3e-9b0a-1e2f3a4b5c6d",
  "success": true,
  "leadResult": {
    "agentRole": "researcher",
    "agentId": 4821,
    "success": true,
    "content": "Acme Corp reported Q2 revenue of $412M (+8% YoY)... [full researcher output]",
    "errorMessage": null,
    "completedAtUtc": "2026-07-08T14:02:11.4021Z",
    "metadata": {
      "OrchestrationInstanceId": "9b1f2e7a-6d4c-4a3e-9b0a-1e2f3a4b5c6d"
    }
  },
  "fanOutResults": [
    {
      "agentRole": "writer",
      "agentId": 4830,
      "success": true,
      "content": "**Acme Corp Q2 Client Brief**\n\nAcme Corp delivered a strong quarter... [full brief]",
      "errorMessage": null,
      "completedAtUtc": "2026-07-08T14:02:19.7754Z",
      "metadata": {
        "OrchestrationInstanceId": "9b1f2e7a-6d4c-4a3e-9b0a-1e2f3a4b5c6d"
      }
    },
    {
      "agentRole": "fact-checker",
      "agentId": 4842,
      "success": true,
      "content": "Verified: revenue figure matches the 10-Q filing. No discrepancies found in the draft brief.",
      "errorMessage": null,
      "completedAtUtc": "2026-07-08T14:02:20.1032Z",
      "metadata": {
        "OrchestrationInstanceId": "9b1f2e7a-6d4c-4a3e-9b0a-1e2f3a4b5c6d"
      }
    }
  ]
}
```

If the lead or a fan-out agent fails permanently (after exhausting the 3-attempt retry policy), that entry's `success` is `false` with `content: null` and a populated `errorMessage`, `leadResult.success == false` still lets fan-out proceed (using the original `input.text` unmodified, with the same `input.files` still attached — see `MultiAgentOrchestratorWorkflow.RunAsync`), and the top-level `success` becomes `false` only if a fan-out result also fails.

## 4. Triggering It Locally

**Update:** the "no trigger exists" gap flagged in earlier drafts of this doc is closed. `MultiAgentOrchestratorTrigger` (`WebAPI/EndPoints/Agent/MultiAgentOrchestratorTrigger.cs`) is a thin tenant-authenticated endpoint, `POST /api/v1/agents/orchestrator`, that forwards `TriggerMultiAgentOrchestratorCommand` to `IWkfloMediator`. Following the exact same pattern as `#4a` below and `TenantAgentChatMessage`/`PublicAgentChatMessage`: `TriggerMultiAgentOrchestratorCommandHandler` wraps the command in `WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>`, saves it into `statestore-ai`, and publishes only the correlation key onto `work-ai-request-{nameof(TriggerMultiAgentOrchestratorCommand)}` — the payload-size-safe indirection, since `Input.Text` can be arbitrarily long.

`MultiAgentOrchestratorSubscriber.TriggerMultiAgentOrchestratorCall` (`WebAPI/EndPoints/Dapr/MultiAgentOrchestratorSubscriber.cs` — its own file, separate from `AIWorkerSubscriber`'s two `work-ai-request-*` methods since it needs an extra `DaprClient` dependency and its own state-cleanup logic) receives the key, loads the queued command, builds a `MultiAgentOrchestratorWorkflowInput` from it (including the `TenantContext`/`ChatContext`/`UserContext`/`AgentSharableContext` populated at publish time), and calls `DaprWorkflowClient.ScheduleNewWorkflowAsync(nameof(MultiAgentOrchestratorWorkflow), instanceId, workflowInput)` directly — passing the **fully-resolved input object**, not the raw key. This deliberately differs from `MessageTenantAgentByIdWorkflow`'s pattern (which threads the key all the way into the workflow and lets a `BaseWorkflowActivity`-based activity load it later): the payload-size limit only applies to the pub/sub **publish** hop, not to `ScheduleNewWorkflowAsync`'s input (which goes through Dapr's own workflow-state persistence), so once the subscriber has the full object in hand there's no need for further indirection — and `MultiAgentOrchestratorWorkflow` never adopted `BaseWorkflowActivity` in the first place (see `#6484`'s design notes). The subscriber deletes the queued state entry itself in a `finally`, since nothing else does.

```bash
curl -X POST http://localhost:32774/api/v1/agents/orchestrator \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <jwt>" \
  -H "amais-wkflo-tenant-name: <your-tenant-name>" \
  -d '{
        "input": { "text": "Summarize the latest quarterly earnings call for Acme Corp and produce a client-ready brief.", "files": [] },
        "sessionId": "8f14e45f-ceea-467e-a2c4-a94a2e2f3f1c",
        "chainOfThought": true,
        "leadAgent": { "agentId": 4821, "agentRole": "researcher" },
        "fanOutAgents": [
          { "agentId": 4830, "agentRole": "writer" },
          { "agentId": 4842, "agentRole": "fact-checker" }
        ]
      }'
```

Expect a `200 OK` with `{ "resultType": "Ok", "data": true, ... }` immediately — the publish is fire-and-forget, matching every other endpoint in this doc that goes through the queue. Poll the actual workflow run against the Dapr Workflow management API on the sidecar (port `3500`):

```bash
curl http://localhost:3500/v1.0/workflows/dapr/<instanceID>
```

The `instanceID` is the same GUID generated as the queue message's `InstanceId` (visible in `dapr-ai-1`'s logs, or by watching `AIWorkerSubscriber`'s log line for the schedule call). Use `agentId` values for `TenantAgent`/`PublishedTenantAgent` records that already exist in your local `WKFLO.DB` (create tenant agents via the normal MD API tenant-agent endpoints first if you don't have suitable ones).

## 4a. Triggering the Cross-Agent Event Notification (`AgentEventTopic`)

**Update:** the gap flagged in earlier drafts of this doc — "`AgentEventSubscriber` exists but nothing in the codebase publishes to this topic" — is closed. `AgentEventPublish` (`WebAPI/EndPoints/Agent/AgentEventPublish.cs`) is a thin tenant-authenticated endpoint, `POST /api/v1/agents/event`, that just forwards `PublishAgentEventCommand` to `IWkfloMediator`. All the actual work — validation (`PublishAgentEventCommandValidator`, FluentValidation via `BaseAIValidator`, including tenant/agent-existence checks reused from `InvokeAgentCommandValidator`) and the publish itself — lives in `PublishAgentEventCommandHandler`, following the same Command/Handler/Validator vertical slice as every other WKFLO feature (an earlier version of this endpoint called `DaprClient` directly with no MediatR command in between; that's been replaced).

**Payload size:** the message broker backing `pubsub-ai` has a payload size limit, so — like every other publish path in this codebase (`TenantAgentChatMessage`, `PublicAgentChatMessage`) — the full `AgentEventMessage` is never published directly on the topic. `PublishAgentEventCommandHandler` wraps it in `WkfloQueueMessage<AgentEventMessage>`, calls `DaprClient.SaveStateAsync(StateStoreName, "msg:{instanceId}", data)` to park the full payload in `statestore-ai` (Redis), and publishes only that short key string via `PublishEventAsync`.

**Subscriber → Workflow → Activity → `IAgentInvoker`:** `AgentEventSubscriber` receives the key and immediately schedules `AgentEventWorkflow` (`DaprWorkflowClient.ScheduleNewWorkflowAsync(nameof(AgentEventWorkflow), instanceId, key)`) and returns — matching the exact shape of every other pub/sub subscriber in this codebase (`AIWorkerSubscriber`/`VoiceWorkerSubscriber`). `AgentEventWorkflow` (`Workflow<string,bool>`) is a trivial single-activity wrapper; `AgentEventWorkflowActivity` does the actual work: loads the queue message back with `GetStateAsync<WkfloQueueMessage<AgentEventMessage>>`, deletes the state entry once read (in a `finally`, so it's cleaned up even if the downstream invocation fails), and calls `IAgentInvoker.InvokeAsync` directly. This activity deliberately does **not** use `BaseWorkflowActivity` — that base's idempotency layer expects the queued payload to itself be a MediatR command dispatched via `IWkfloMediator.Send`, and `AgentEventMessage` is plain data consumed via `IAgentInvoker` instead, the same reason `InvokeAgentWorkflowActivity` (the orchestrator's own activity) doesn't use it either. This handler still treats every delivery as one-shot — no retry policy, no idempotency-key tracking — matching its behavior before this activity/workflow layer was introduced.

**Tenant context propagation:** `PublishAgentEventCommandHandler` also populates the `WkfloQueueMessage`'s `TenantContext`/`ChatContext`/`UserContext`/`AgentSharableContext` from its own request-scoped ambient contexts. `AgentEventWorkflowActivity` forwards those onto `AgentInvocationRequest`, and `AgentInvoker.InvokeAsync` restores them onto its own scoped context instances via `IWkfloMapper` before dispatching to MediatR — mirroring `BaseWorkflowActivity.LoadContext`. Without this, `IRunnableAISyncService` (which depends on ambient `TenantContext`) would see a blank tenant in every agent invocation, since a fresh DI scope (Workflow Activity or ASP.NET Core request) doesn't carry over the ambient values from whatever originally triggered it. The same mechanism is wired for the orchestrator-workflow path (`MultiAgentOrchestratorWorkflowInput`/`MultiAgentOrchestratorWorkflow`) and is now exercised end-to-end via `TriggerMultiAgentOrchestratorCommandHandler`/`AIWorkerSubscriber.TriggerMultiAgentOrchestratorCall` (see Section 4).

**Agent scope:** the endpoint lives in a scope-neutral `Agent` folder (not `Tenant`/`Public`) because the event topic is conceptually agent-type-agnostic, and it genuinely supports both now: `AgentEventMessage` flows through `InvokeAgentCommand`, which calls `IRunnableAISyncService.RunRunnableAgentByIdAsync` — a lookup against the `RunnableAgent` base type (`TenantAgent` or `PublishedTenantAgent`), validated in `PublishAgentEventCommandValidator` via `BeExistingRunnableAgent` (existence + tenant ownership + `IsActive`, across both subtypes). The share-id-based public flow (`MessagePublicAgentByShareIdCommand`, keyed by `string ShareId` rather than an `int` agent id) is a structurally different access pattern and still isn't supported here — the endpoint still requires the tenant header at runtime for that reason.

To exercise the full pipeline locally end to end (publish → `AgentEventSubscriber` schedules `AgentEventWorkflow` → `AgentEventWorkflowActivity` invokes `IAgentInvoker`), with the stack from Section 2 running and a real `TenantAgent` id from your local `WKFLO.DB`:

```bash
curl -X POST http://localhost:32774/api/v1/agents/event \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <jwt>" \
  -H "amais-wkflo-tenant-name: <your-tenant-name>" \
  -d '{
        "agentId": 4821,
        "agentRole": "watcher",
        "input": { "text": "The upstream research task just completed — react accordingly.", "files": [] },
        "sessionId": "8f14e45f-ceea-467e-a2c4-a94a2e2f3f1c",
        "eventType": "task-completed"
      }'
```

Expect a `200 OK` with `{ "resultType": "Ok", "data": true, ... }` immediately — the publish is fire-and-forget from the caller's perspective. To confirm the rest of the path actually ran:

- **Subscriber invocation:** `docker logs dapr-ai-1` (or the sidecar's stdout under `dapr run`) should show a delivery to `/work-ai-request-AgentEventMessage` shortly after the publish, followed by a workflow schedule for `AgentEventWorkflow` using the same instance id as the queue message's `InstanceId` — the same correlation mechanism `AIWorkerSubscriber`'s topics already use.
- **Application logs:** if the target agent invocation fails, `AgentEventWorkflowActivity` logs a warning (`AgentEventWorkflowActivity: agent {AgentId} (role {AgentRole}) failed to react to event {EventType}: ...`) or, on an unhandled exception, an error (`AgentEventWorkflowActivity: failed to invoke agent {AgentId} for event {EventType}`) — the activity returns `false` on failure but nothing retries it (no retry policy attached to this activity call), so success is only observable via logs/traces, not any HTTP response.
- **Traces:** `AgentInvoker.InvokeAsync` tags whichever span is already ambient — the workflow activity's own auto-instrumented span — with `wkflo.agent_role`, `wkflo.runnable_agent_id`, and `wkflo.agent_invocation_success`. Correlate a specific publish call to its downstream invocation via the workflow instance id (same as the `WkfloQueueMessage.InstanceId` generated at publish time), visible in `dapr-ai-1`'s logs and in the Dapr Workflow management API (`curl http://localhost:3500/v1.0/workflows/dapr/<instanceID>`, same as Section 4).

## 4b. Handing Off to Another Agent from Inside a Conversation (`RunnableAgent.EnableSpinUpAgents`)

Sections 4 and 4a describe HTTP-triggered entry points into the pub/sub-backed publish pipeline. There is a third, LLM-triggered entry point into the *same* pipeline: when a `RunnableAgent`'s `EnableSpinUpAgents` flag is set, `SemanticKernelExtensions.AddWkflowToolsAsync` attaches `AgentEventPublisherPlugin` (`publish_agent_event`, under `Common.AI.Application/Plugins/AgentEventPublisher/`) directly to that agent's kernel. Unlike every other tool type in this codebase, it isn't backed by a `Tool`/`Tool.Config` row — there's nothing to attach or configure per-tenant beyond the boolean flag; the target agent(s) and message text are decided by the calling agent's own LLM at call time, as kernel-function arguments.

**Multi-target by design, resolved by name.** `publish_agent_event`'s first parameter, `targetAgentsJson`, is a JSON array of `{"agentName": <string>}` objects (`AgentEventTarget`, `Common/Models/AI/`) rather than a single numeric target — the LLM has no natural way to know a numeric agent id, so it names each target agent instead, and the plugin resolves that name to the real agent itself. For each target, the plugin queries the root `Agents` DbSet (not `.OfType<RunnableAgent>()` — see the same EF Core caveat noted for `RunnableAISyncService` earlier in this doc: that intermediate TPH type has no entity mapping of its own) filtered to the current tenant and non-deleted, then downcasts/filters to `RunnableAgent { IsActive: true }` in memory. Zero matches, more than one match (`Agent.Name` has no tenant-scoped uniqueness constraint), or a `MarketplaceAgent` sharing the name (not a `RunnableAgent`) all resolve to "not found"/"ambiguous" for that one target — the call still proceeds for every other target in the array. The plugin publishes one `PublishAgentEventDomainEvent` per successfully-resolved target, so one target's resolution or publish failure doesn't block the others; the returned acknowledgement string summarizes which targets were notified, which failed, and why. `message`/`eventType` are shared across every target in the call — there's no per-target message today.

**Allowlist enforcement.** A resolved target is only notified if it's on the calling agent's explicit `AgentAllowedSpinUpAgent` allowlist (see below) — `AgentEventPublisherPlugin`'s constructor now takes the calling agent's own id (`callingAgentId`, threaded from `AddWkflowToolsAsync`'s `agent.Id`), and after resolving a target name to a real agent it checks `AgentAllowedSpinUpAgents.AnyAsync(x => x.AgentId == callingAgentId && x.AllowedSpinUpAgentId == resolvedAgent.Id)` before publishing. A resolved-but-not-allowed target fails identically to a not-found one (`"{name} (not found)"`) — the plugin does not distinguish "doesn't exist" from "exists but isn't allowed", so an agent's LLM can't enumerate names it's not permitted to reach. This replaced the earlier behavior of allowing any active `RunnableAgent` in the same tenant.

**A prior iteration of this feature used deterministic `@agentName` mention resolution instead** (`SemanticKernelAgentService.TryHandleAgentMentionAsync`/`AgentMentionResolverService`, which regex-scanned the raw message and published the hand-off without any LLM decision, short-circuiting the calling agent's own reply for that turn). That mechanism was removed in favor of going back to this LLM-callable plugin with multi-target support — the deterministic resolver and its short-circuit behavior no longer exist in this codebase.

**Why an extra hop (domain events) instead of calling the command directly:** `AgentEventPublisherPlugin` lives in `Common.AI.Application`, which has no project reference to this project (`AI.Application`, where `PublishAgentEventCommand` is declared) — only the reverse reference exists. So the plugin publishes a Common-declared `IWkfloNotification` instead (`PublishAgentEventDomainEvent`, under `Common.AI.Application/Domain/Agents/Events/PublishAgentEvent/`), once per target, and this project's own `PublishAgentEventDomainEventHandler` (`Agents/Events/PublishAgentEvent/`) picks each one up via `INotificationHandler<T>`, constructs the real command, and `IWkfloMediator.Send`s it — going through the exact same validated publish pipeline described in Section 4a, just entered from inside a running agent instead of from HTTP. This mirrors the existing `AgentTokensUsedDomainEvent` → `ReportUsageAgentTokensUsedDomainEventHandler` bridge that already solves the identical cross-project problem for token-usage reporting.

**Return value is only an acknowledgement.** The plugin method returns immediately after publishing every target — it does not wait for any target's reaction, and the return string exists specifically to stop the calling LLM from claiming it already has the other agent(s)' answer. The real answers land in the same chat session later via the existing blob-based chat history path, same as every other entry point into this pipeline.

**There is no LLM-triggered path to spin up a full multi-agent orchestration from inside a running agent** — the earlier `MultiAgentOrchestratorPlugin` (`trigger_multi_agent_orchestrator`) was removed and has no replacement; `POST /api/v1/agents/orchestrator` (Section 4) is the only way to trigger `MultiAgentOrchestratorWorkflow`.

**Discovering valid `@name` targets.** Since `publish_agent_event` resolves target agents by exact `Name`, a caller (typically the UI, to build an autocomplete/picker) needs to know which names are valid ahead of time. Two GET endpoints expose this: `GET /api/v1/tenant/agent/{id}/spin-up/agents` (tenant-scoped, `[Authorize]`) and `GET /api/v1/agent/{shareId}/spin-up/agents` (public, `[AllowAnonymous]`, resolves the agent via `AgentChatSetting.ShareId` like every other public agent endpoint). Both return `SuccessResult<List<string>>` — the names of the agents on the querying agent's `AgentAllowedSpinUpAgent` allowlist, filtered to active, non-deleted `RunnableAgent`s. This replaced the earlier "every other active `RunnableAgent` in the same tenant" behavior. Backed by `GetAvailableSpinUpAgentsByTenantAgentIdQuery`/`GetAvailableSpinUpAgentsByShareIdQuery` (`Agents/Queries/`), which join through the allowlist table and downcast to `RunnableAgent` in memory — the same EF Core-safe pattern used throughout this feature.

**The `AgentAllowedSpinUpAgent` allowlist.** A tenant admin explicitly grants agent A permission to notify agent B by creating an `AgentAllowedSpinUpAgent` row (`AgentId` → `AllowedSpinUpAgentId`, both FKs to `Agent`, composite PK, no soft-delete — modeled on the `Tag`/`EntityTag` junction pattern, MD API's `Common.MD.Domain`). CRUD lives entirely in MD API, one row at a time (no bulk endpoint): `POST /api/v1/tenant/agent/{id}/spin-up/agents/{allowedAgentId}` (`AssignAllowedSpinUpAgentCommand`, rejects a duplicate assignment as `InvalidResult`) and `DELETE /api/v1/tenant/agent/{id}/spin-up/agents/{allowedAgentId}` (`UnassignAllowedSpinUpAgentCommand`, `NotFoundResult` if the row doesn't exist). Both ids must resolve to an existing `TenantAgent` in the caller's own tenant.

**Publishing copies the allowlist, dropping unpublished targets.** `PublishTenantAgentCommandHandler` copies a `TenantAgent`'s allowlist rows onto its `PublishedTenantAgent` every time it publishes or republishes: it re-points each `AllowedSpinUpAgentId` from the target `TenantAgent`'s own id to that target's `PublishedTenantAgentId`, and **drops** any target that hasn't itself been published yet (a public/share-id caller shouldn't be able to reach a tenant-only draft agent). Republishing fully replaces the published agent's allowlist rows rather than merging — the same remove-then-add pattern this handler already uses for `EntityTag`/`AgentTool` sync.

**Reading back a multi-agent session's full transcript.** Since a `publish_agent_event` hand-off means several agents can each post messages into the *same* `sessionId`, the existing single-agent chat history endpoints (`GET /api/v1/tenant/agent/{id}/chat/{sessionId}/messages`, `GET /api/v1/agent/{shareId}/chat/{sessionId}/messages`) only ever return one agent's half of the conversation. Two additional endpoints aggregate across every participant: `GET /api/v1/tenant/agent/{id}/chat/{sessionId}/session-messages` (`TenantAgentsChatMessagesBySessionIdList`) and `GET /api/v1/agent/{shareId}/chat/{sessionId}/session-messages` (`PublicAgentsChatMessagesBySessionIdList`). Both keep the `{id}`/`{shareId}` route segment for parity with the single-agent endpoints and (for the public one) because `WebChatAuthorizationAttribute`'s DB-auth path requires a `shareId` route value to authorize at all — neither handler uses it to filter to one agent. Instead, each handler queries the SQL `Conversation` table (`Agents/Queries/ListTenantAgentsChatMessagesBySessionId`, `.../ListPublicAgentsChatMessagesBySessionId`) for every distinct `AgentId` with a row for that `SessionId` — `Conversation` is upserted per `(AgentId, SessionId)` on every chat save, so it's already the authoritative index of "who chatted in this session," and `SessionId` is indexed. Each participating agent's blob is then loaded via `IChatStoreService.LoadChatAsync` in parallel (`Task.WhenAll`), mapped through the same `TenantAgentChatMessageResult`/`PublicAgentChatMessageResult` DTOs the single-agent endpoints use (now also carrying `AgentId`/`AgentName` per message, stamped by all four of these handlers, single- and multi-agent alike, so a merged transcript can be attributed back to its speaker), and the combined list is sorted by `Timestamp` before returning. Note `Conversation.Agent` is typed as `TenantAgent` specifically, not the polymorphic base `Agent` — both handlers resolve `AgentName` via a separate lookup against the root `Agents` DbSet rather than that navigation, since the public flow's participants are `PublishedTenantAgent`s.

## 5. Observing a Run

- **Traces:** Grafana Alloy (container `alloy`, `infra/alloy/config.alloy`) receives OTLP from both the app (`OTEL_EXPORTER_OTLP_ENDPOINT=http://alloy:4317`) and the Dapr sidecars (`dapr-config.yaml` → `tracing.otel.endpointAddress: alloy:4317`), and forwards to Tempo per whatever `GRAFANA_TEMPO_ENDPOINT`/`TEMPO_USERNAME` your `.env` points at. Look for spans named `InvokeAgent` (from `InvokeAgentWorkflowActivity`'s dedicated `ActivitySource`, `"AMAIS.WKFLO.AI.MultiAgentOrchestrator"`) tagged with `wkflo.agent_role`, `wkflo.runnable_agent_id`, and `wkflo.orchestration_instance_id` — these let you pick out the lead step and each fan-out call individually even though Dapr's own auto-instrumented workflow spans are tagged only by generic route template. `AgentInvoker.InvokeAsync` (as of #6553, a plain in-process call — see `docs/dapr-actors-workflows-activities.md`) enriches that same span with `wkflo.agent_invocation_success` rather than creating a second span, since there's no longer a separate transport hop to a Dapr Actor.
- **Logs:** `MultiAgentOrchestratorWorkflow` uses `context.CreateReplaySafeLogger<T>()`, so log lines only emit on the real (non-replay) execution — expect one log line per lead/fan-out attempt and one error line per permanently-failed step, not duplicates on every workflow replay. `InvokeAgentWorkflowActivity` and `AgentInvoker` both log at `Information`/`Error` per call.
- **Dapr dashboard / sidecar logs:** `docker logs dapr-ai-1` (or the sidecar spawned by `dapr run`, `LOG_LEVEL=debug` per the compose config) show actor placement, activation, and workflow-history-append events if you need to debug below the application log level.

## 6. What This Does NOT Do (Out of Scope)

- **No direct agent-to-agent calls.** `IAgentInvoker` deliberately exposes a single request/response method and is only ever invoked by a workflow activity or the pub/sub subscriber — agents never call each other directly, even within a fan-out.
- **No migration of existing single-agent workflows.** `MessageTenantAgentByIdWorkflow`, `MessagePublicAgentByShareIdWorkflow`, and their voice equivalents are untouched; they continue to call `InvokeAgentCommand`/the SK path directly and do not route through `IAgentInvoker` or `MultiAgentOrchestratorWorkflow`.
- **No adoption of the Dapr Conversation API.** Per the spike (`docs/spikes/dapr-conversation-api.md`), agent invocation wraps the existing Semantic Kernel `Kernel` construction path (`AIProviderHelper.GetKernel()`), not `DaprConversationClient` — every provider's Conversation API component is still Alpha with no streaming support. (The spike's decision predates task #6553's actor-to-plain-service conversion; the underlying SK path it evaluated is unchanged.)
- **No per-fan-out-agent existence/active validation at trigger time.** `TriggerMultiAgentOrchestratorCommandValidator` checks the lead agent and tenant, but an invalid `FanOutAgents[i].AgentId` still only surfaces as a per-participant soft failure deep in the workflow, same as the lead-agent path already did.
- **Latency/overhead of the actor and workflow layers is not evaluated here** — consistent with the spike's scope note, this doc is about correctness/local testing, not performance.
