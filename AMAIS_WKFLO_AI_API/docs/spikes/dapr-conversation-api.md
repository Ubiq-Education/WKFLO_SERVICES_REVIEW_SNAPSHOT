# Spike: Dapr Conversation API vs. Direct Semantic Kernel Calls

- **ADO Task:** #6489 (Story #6480 — Dapr-based orchestration for multi-agent Claude workflows)
- **Date:** 2026-07-08
- **Related upcoming work:** #6482 — `AgentActor` (Dapr virtual actor wrapping the existing SK call path)
- **Scope note:** Per-call latency/overhead of the Dapr Conversation API vs. direct SDK/SK calls is explicitly **not** a rejection criterion for this spike — the AI API scales horizontally via Azure Container Apps replicas, not single-instance throughput. Latency is not weighed in the recommendation below.

**Update (task #6553):** `AgentActor` as scoped here was never built as an actor — task #6553 found it would have been a fresh, one-shot RPC per call with no reminders/timers and no state surviving past the call, so it never needed the Dapr Actor runtime. It shipped as `IAgentInvoker`, a plain `Scoped` service, instead — see [`docs/dapr-actors-workflows-activities.md`](../dapr-actors-workflows-activities.md) for the full reasoning and [`docs/multi-agent-orchestration.md`](../multi-agent-orchestration.md) for how it's used today. This doesn't change this spike's actual recommendation (direct Semantic Kernel calls over the Dapr Conversation API) — every mention of `AgentActor` below should just be read as "the SK call path that `IAgentInvoker` now wraps," not a Dapr Actor.

## Summary

Dapr's Conversation API (`conversation.*` building block) is still in **Alpha** across every real LLM provider component, including the Anthropic component we'd need (`conversation.anthropic`). It gives a unified request/response contract with framework-level support for tool calling, PII scrubbing, and prompt/response caching, but it does **not** currently support streaming responses, and multi-turn state (`contextId`) is inconsistently documented across language SDKs. It is a fully separate abstraction from Semantic Kernel — there is no integration point between the two; adopting it means bypassing SK's connector layer entirely, not composing with it. Given the AI API already depends on SK streaming (`GetStreamingChatMessageContentsAsync`) for chat responses and on a mature SK plugin ecosystem (5 tool types in `AMAIS.WKFLO.Common.AI.Application/Plugins/`), **recommendation is to stay with direct Semantic Kernel calls for now** and revisit the Conversation API once it reaches Beta/Stable with documented streaming and tool-calling support for the Anthropic component specifically.

## Current State of Dapr Conversation API

**Maturity / component status** (per `docs.dapr.io/reference/components-reference/supported-conversation/`):

| Component | Status | Version | Since Dapr runtime |
|---|---|---|---|
| `conversation.anthropic` | **Alpha** | v1 | 1.15 |
| `conversation.openai` (covers OpenAI **and** Azure OpenAI via `apiType: azure`) | **Alpha** | v1 | 1.15 |
| `conversation.googleai` | **Alpha** | v1 | 1.16 |
| `conversation.aws.bedrock` | Alpha | v1 | 1.15 |
| `conversation.mistral`, `conversation.deepseek`, `conversation.huggingface`, `conversation.ollama` | Alpha | v1 | 1.15/1.16 |
| `conversation.echo` (local/no-op) | **Stable** | v1 | 1.15 | 

Every provider we actually use (azure, openai, anthropic, google) is Alpha only. Nothing has graduated to Beta or Stable.

**Provider parity:** All 4 providers WKFLO uses today (azure, openai, anthropic, google) have a corresponding component. Azure OpenAI is not a distinct component — it's the `conversation.openai` component configured with `apiType: azure`, `endpoint`, and `apiVersion` metadata fields.

**Anthropic component specifics:** `conversation.anthropic` documents only three metadata fields — `key` (required), `model` (optional, defaults to the dated `claude-3-5-sonnet-20240620`), and `responseCacheTTL`. No Anthropic-specific tool-calling or extended-thinking metadata is documented, which is a signal the component is a thin wrapper, not a feature-complete abstraction over the Anthropic SDK.

**Tool / function calling:** Supported at the generic Conversation API framework level, described as following "OpenAI's function calling format." However, no worked example exists in the Dapr docs for the Anthropic component or the .NET SDK specifically — the framework-level claim is not yet backed by concrete .NET usage documentation.

**Streaming:** **Not supported today.** The .NET `DaprConversationClient.ConverseAsync()` returns a single, fully-materialized response. The underlying gRPC proto (`rpc Converse(stream ConversationRequest) returns (stream ConversationResponse)`) is stream-shaped, suggesting streaming is architected for but not implemented. There is an open upstream issue (`dapr/dapr-agents#80`, "Enable Streaming LLM Output") tracking this gap as of the time of this spike.

**Multi-turn conversation state:** A `contextId` concept exists for session continuity, demonstrated in the Go and Java SDK examples, but was not confirmed in the .NET SDK documentation reviewed for this spike. The .NET `ConverseAsync()` shape instead takes an array of `IConversationMessage` (`SystemMessage`, `UserMessage`) per call — closer to manually re-sending history each turn than to a managed `ChatHistory`/session object. This needs direct verification against the SDK source if the Conversation API is revisited later; treat the .NET-side session story as unproven, not just immature.

**Other features present:** PII obfuscation (phone numbers, emails, IPs, credit cards), provider-side prompt-cache retention (`promptCacheRetention`), and component-level response caching (`responseCacheTTL`, in-memory per sidecar).

**Relationship to Semantic Kernel:** Fully separate abstraction. Dapr docs make no mention of SK or any framework integration — the Conversation API is a direct alternative to using a provider SDK (or SK) yourself, not a layer that sits underneath or alongside SK. Adopting it for the `AgentActor` would mean the actor talks to the Dapr sidecar via `DaprConversationClient` instead of building an SK `Kernel`, which removes SK's connector layer but keeps nothing of SK's plugin/function-calling machinery automatically — that would need to be re-expressed against the Conversation API's tool-calling contract from scratch.

## Comparison Table

| Dimension | Direct SK (`AIProviderHelper.GetKernel()`) — current | Dapr Conversation API |
|---|---|---|
| Provider support (azure/openai/anthropic/google) | Full parity today, in production | All 4 covered, but every component is Alpha |
| Tool / function calling | Native SK `[KernelFunction]` plugins; 5 tool types already built (`Knowledge`, `Priority_QA`, `MCP`, `Integration_Amais`, `Chat_Classification`) | Framework-level support claimed (OpenAI-format), no confirmed Anthropic/.NET worked example; would require rebuilding all 5 plugin types against a new contract |
| Streaming | In active use today (`GetStreamingChatMessageContentsAsync` across `SemanticKernelAgentServiceRun*.cs`) | Not supported; open upstream issue tracking it |
| State / history (`ChatHistory` equivalent) | `ChatHistory` is a first-class SK object, fully integrated with plugins and reasoning chain | `contextId` exists conceptually (Go/Java) but unconfirmed in .NET SDK; message-array-per-call pattern observed instead |
| Integration effort for `AgentActor` (#6482) | Zero — same `Kernel` construction pattern the actor would wrap | High — new client, new request/response mapping, no SK plugin reuse, tool-calling and streaming gaps to work around or wait out |
| Dapr-native benefits (tracing, sidecar-level PII scrub/caching) | None out of the box | Yes — PII obfuscation, response caching, prompt-cache retention, and Dapr distributed tracing come "for free" at the sidecar |
| Latency / per-call overhead | N/A — explicitly excluded from this decision | N/A — explicitly excluded from this decision |

## Recommendation

**Stay with direct Semantic Kernel calls (`AIProviderHelper.GetKernel()`) for the `AgentActor` (#6482); do not adopt the Dapr Conversation API now.**

Reasoning:
1. Every relevant provider component (anthropic, openai, googleai) is Alpha — no graduation timeline is published, and Alpha components carry no compatibility guarantee across Dapr runtime upgrades.
2. Streaming is not supported, and the AI API already depends on SK streaming for chat UX today; switching would be a regression, not a lateral move.
3. Tool-calling is claimed at the framework level but has no confirmed, documented Anthropic/.NET code path — adopting now means building against an unverified contract.
4. The existing SK plugin ecosystem (5 tool types, `AMAIS.WKFLO.Common.AI.Application/Plugins/`) has no migration path to the Conversation API's tool-calling contract; adopting would mean re-implementing all of it with no source-level reuse.
5. Latency/overhead was explicitly excluded from this decision per the story's constraints, so this recommendation rests solely on functional maturity gaps, not performance.

This is an "adopt later" call, not "never": the sidecar-native PII scrubbing, response/prompt caching, and Dapr tracing integration are genuinely attractive for a multi-agent orchestration surface, and worth re-evaluating once (a) the Anthropic/OpenAI/GoogleAI components reach Beta or Stable, and (b) streaming and .NET-side multi-turn state (`contextId`) are documented and confirmed working.

## Risk to `AgentActor` (#6482) implementation

None from this recommendation — #6482 should proceed by wrapping the existing SK `Kernel` construction path (`AIProviderHelper.GetKernel()` + `SemanticKernelAgentService`), exactly as originally scoped. No rework is implied by this spike. The only residual risk is scope creep if someone later proposes retrofitting that call path onto the Conversation API before it reaches Beta/Stable — this doc should be cited to head that off until the maturity gaps above are closed.

**Update (task #6553):** "inside the Dapr actor" above is stale — #6553 determined `AgentActor` didn't need to be a Dapr Actor at all (see the update note at the top of this doc) and shipped it as `IAgentInvoker`, a plain `Scoped` service, instead. The risk analysis is otherwise unaffected: this spike's recommendation was about which call path to wrap (direct SK vs. the Conversation API), not about the actor-vs-service question `docs/dapr-actors-workflows-activities.md` later settled.
