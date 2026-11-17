# Research: Anthropic Skills — Applicability to WKFLO

**Azure DevOps:** [WI 6848](https://dev.azure.com/PetraCMS/PetraCMS/_workitems/edit/6848) (User Story, PetraCMS\DXP, DXP-WKFLO Sprint 16)
**Type:** Research only — no code changes or PoC in this pass.
**Ask:** "Research what are Anthropic skills, its goals, what value would add to WKFLO solution and how difficult would be to implement porting implementation guidelines."

---

## 1. What Anthropic Skills are

Anthropic Skills (as shipped for Claude and the Claude Agent SDK) are **packaged, named capability folders** the model can discover and load into its own context *on demand*, rather than content that is always baked into the system prompt.

Each skill is a directory containing at minimum a `SKILL.md` file with:

- **YAML frontmatter** — a `name` and a `description`. The description is the only thing shown to the model up front; it's what the model uses to decide *whether this skill is relevant* to the current task.
- **A markdown body** — the actual instructions, guidance, conventions, or step-by-step procedure the model should follow once the skill is selected.
- **Optional companion files** — reference documents, scripts, templates, or examples that the skill body can point to, loaded only if the model decides it needs them.

The key architectural idea is **progressive disclosure**: the system prompt only carries a lightweight index (name + one-line description) for every available skill. The full instructional body is loaded into the conversation only when the model actually invokes that skill by name for the task at hand. This keeps the base system prompt small and lets an agent have access to dozens of specialized behaviors without paying their token cost on every single turn.

Three properties matter for comparing this to WKFLO:

1. **Instructions, not just tools.** A skill's payload is *prose guidance* (how to write a commit message, how to format a report, what steps to follow for a workflow) — distinct from an MCP tool, which is a *callable function*. A skill can reference tools, but the skill itself is text injected into context.
2. **Named + described, selected by relevance.** The model (or a lightweight classifier) matches user intent against the catalog of skill descriptions and chooses zero, one, or several to pull in.
3. **File-based and versionable.** Skills live as files (typically in a repo or a skill directory), so they can be authored, diffed, reviewed, and versioned like code — not embedded as an ever-growing string in one prompt or database row.

---

## 2. What value this would add to WKFLO

WKFLO's current agent-instruction model has three concrete gaps that a Skills-style layer would close:

**(a) One monolithic system prompt per tenant.** `Agent.SystemPrompt` (`AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/Entities/Agent.cs`) is a single `string` column on the `Agent` entity. Every instruction a tenant wants their agent to follow — tone, domain knowledge, step-by-step procedures for specific tasks — has to be concatenated into this one field (further appended to at runtime by `SemanticKernelExtensions.GetWkflowPromptAsync`, which tacks on tool-specific prompt fragments and a response-tone suffix). There's no way to keep the prompt lean for common turns while still supporting deep, task-specific guidance for edge cases; everything is always in context, every turn, whether needed or not.

**(b) No versioned/file-based instruction authoring.** Prompts today are either raw C# string literals (baked into plugin/prompt-construction code, e.g. the classifier prompt inside `AmaisSkillSelectorPlugin.ClassifyPluginsAsync`) or free-text DB columns edited via MD API CRUD commands (`AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.Application/Agents/Commands/`). Neither supports code-review-style diffing, reuse across environments, or a change history independent of the row's `LastUpdatedOn` timestamp.

**(c) No composable, reusable instruction packets across tenants.** Because instructions live inline per-`Agent` row, a procedure useful to multiple tenants (e.g. "how to handle a refund request," "how to draft a blog post in our house style") has to be copy-pasted into every tenant's `SystemPrompt`, with all the drift that implies. A Skills-style catalog would let WKFLO define such packets once, tag them with a name + description, and let any agent's classifier pull in the relevant one(s) at runtime — instead of duplicating text or growing the base prompt unboundedly.

This also dovetails with a pattern WKFLO has already adopted for *callable* capabilities — the MCP tool model (name + `[Description]` → invocable by the model) — but WKFLO has no equivalent today for *instructional* (non-callable) content. Skills would extend the same "name + description, invoked by relevance" idea one level up, to guidance rather than functions.

---

## 3. Nearest existing precedents in this codebase

Two mechanisms already in `wkflo_services` are architecturally close to a Skills system, though neither does the whole job.

### 3.1 `AmaisSkillSelectorPlugin` + `AmaisPluginCatalog` — dynamic, classifier-selected plugin loading

- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Plugins/Amais/AmaisPluginCatalog.cs`
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Plugins/Amais/SkillSelector/AmaisSkillSelectorPlugin.cs`

`AmaisPluginCatalog` is a static, declarative catalog: an `IReadOnlyList<Entry>` where each `Entry` has a `Name`, a `Description`, and an optional map of KernelFunction tool names to PetraCMS endpoint codes (used to gate individual functions by which PetraCMS endpoints are actually deployed for that tenant — see `IsOfferable`/`GetOfferable`/`CreateFilteredPlugin`). The catalog's own doc comment literally calls these entries "AMAIS plugin/skill" — the codebase already uses "skill" informally for this concept.

`AmaisSkillSelectorPlugin.SelectPluginsAsync` does a **classification call**: it builds a prompt listing every offerable catalog entry's name + description, asks the chat-completion model (via a plain `ChatHistory` with `FunctionChoiceBehavior.None()`, so no tool-calling loop, just a JSON-array answer) which plugins are relevant to the current user query, then dynamically adds the corresponding Semantic Kernel plugin objects into the live `Kernel.Plugins` collection (`case "Welcome": _kernel.Plugins.Add(...)`, etc. in `SelectPluginsAsync`). Wiring happens in `SemanticKernelExtensions.AddWkflowToolsAsync`, which constructs the selector only when the tenant's `IntegrationAmais` tool has `AreSkillsEnabled` set, and `AutoSelectAmaisPluginsAsync` (further down the same file) is the call site that actually triggers `SelectPluginsAsync` per turn.

**How it maps onto the Skills model:** the name+description catalog, the relevance classifier, and the "load only what's needed, only when needed" runtime behavior are functionally identical to how a Skills system decides which skill(s) to activate.

**Where it falls short:** the *payload* it loads is always a set of **callable KernelFunctions** (SK plugin methods), never freeform instructional text. There is no code path anywhere in this class or its catalog that says "inject this markdown body into the conversation as guidance." It selects *capabilities*, not *knowledge/procedure*.

### 3.2 MCP Server's `[McpServerToolType]` / `[McpServerTool]` registration

- `AMAIS_WKFLO_MCP_SERVER/src/AMAIS.WKFLO.MCP/AMAIS.WKFLO.MCP.WebAPI/Program.cs` (the `AddMcpServer().WithTools<T>()` chain, e.g. `.WithTools<AmaisBlogPostTools>()`, `.WithTools<AmaisPageTools>()`)
- `AMAIS_WKFLO_MCP_SERVER/src/AMAIS.WKFLO.MCP/AMAIS.WKFLO.MCP.Application/Tools/Amais/Post/AmaisBlogPostTools.cs` (class decorated `[McpServerToolType]`)
- `AMAIS_WKFLO_MCP_SERVER/src/AMAIS.WKFLO.MCP/AMAIS.WKFLO.MCP.Application/Tools/Amais/Post/AmaisBlogPostToolsCreate.cs` (method decorated `[McpServerTool, Description("...")]`)

Every MCP tool is a C# method annotated with `[McpServerTool, Description("...")]` inside a class annotated `[McpServerToolType]`, registered once at startup via `.WithTools<T>()`. The MCP client side that consumes these (`IMcpClientService` / `McpClientService.cs` under `AMAIS_WKFLO_COMMON/.../Interfaces/Services/Mcp/` and `.../Services/Mcp/`) exposes them into the Semantic Kernel as ordinary kernel functions, discoverable by name + description exactly the way the SK/LLM tool-calling loop expects.

**How it maps onto the Skills model:** this is the same "name + description, model decides to invoke" registry shape Anthropic Skills also use, and it already spans a network boundary (WKFLO agents call MCP-hosted tools over HTTP) — proof the pattern generalizes beyond in-process plugins.

**Where it falls short:** identical gap to 3.1 — every registered unit is an invocable *function* with typed parameters and a return value, never a block of instructional markdown. There's also no "trigger criteria" beyond the description text used by the SK/LLM function-calling mechanism itself; there's no separate lightweight classifier step comparable to `AmaisSkillSelectorPlugin` sitting in front of MCP tool selection (MCP relies on the model's native tool-calling judgment against however many tools happen to be registered in the kernel at once).

### 3.3 The missing piece: instructional-text injection

WKFLO does already have a pattern for injecting *ad hoc, one-off content* into a live conversation without it being part of the base system prompt — see `SemanticKernelExtensions.AutoInvokePageDataAsync`, `AutoInvokeTextFeatureDataAsync`, `AutoInvokeGalleryFeatureDataAsync`, and `AutoInvokeAmaisKnowledgeAsync` (all in `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Extensions/SemanticKernelExtensions.cs`). Each of these: checks a dedupe guard against `chatHistory` so it doesn't inject the same content twice, invokes a kernel function or plugin to fetch data, and — if it produced anything — appends it as a `ChatHistory.AddMessage(AuthorRole.System, <content>, metadata: ...)` call. That is *exactly* the mechanical shape a skill-body injection would need — the only difference is the content being injected today is retrieved *data* (page contents, gallery metadata, KB snippets), never authored instructional *text*. This is the concrete gap called out in the difficulty assessment below: the injection plumbing exists, but nothing today feeds it a markdown "how to do X" body.

---

## 4. Difficulty assessment: **Low-to-Medium**

This is a "compose existing primitives, add one small missing primitive" job, not a "build new infrastructure" job.

**Already in place (low-effort side):**
- A working, production-proven **relevance classifier** pattern (`AmaisSkillSelectorPlugin.ClassifyPluginsAsync`) that turns a name+description catalog + user query into a selection list via a lightweight LLM call.
- A working **declarative catalog** shape (`AmaisPluginCatalog.Entry`) with name, description, and optional gating metadata, plus filtering helpers (`IsOfferable`, `GetOfferable`) that already handle partial/per-tenant availability.
- A working **one-off system-message injection** mechanism (`AutoInvoke*` methods) with dedupe-by-content-check against `ChatHistory`, ready to carry arbitrary text instead of retrieved data.
- The MCP tool model, if a skill wants to reference/trigger callable tools alongside its instructional text.

**Missing (the actual net-new work):**
1. **No mechanism to load raw instructional text from anything other than a DB string column or an inline C# literal.** There is no file-based prompt/skill-body loading anywhere in the codebase today — no `SKILL.md`-equivalent parser, no directory convention for skill content, no markdown-with-frontmatter reader.
2. **No `Skill`-shaped entity/model.** Nothing today separates "name + description used for selection" from "body used for injection" the way a skill requires; `AmaisPluginCatalog.Entry` only carries name/description (used to pick a *plugin*, not inject *text*).
3. **No authoring/versioning story.** Prompts are inline C# literals or free-text DB columns edited through MD API CRUD commands — neither supports diff review, reuse across tenants, or rollback independent of `Agent`/`Tool` row history.

None of this requires new infrastructure classes of work (no new external service, no new AI provider integration, no new transport). It's: one new small entity/DTO shape, one new catalog-like static or DB-backed registry, one new classifier extension, and one new injection call — all following patterns that already exist and are already tested in production. That's why this lands as low-to-medium rather than higher: the risk is in decisions (where do skill bodies live — file, DB, or both; per-tenant or shared; admin-editable or not) rather than in unproven technical mechanism.

---

## 5. Implementation guidelines (porting plan — not built in this pass)

### 5.1 Define a `WkfloSkill` concept

Introduce a shape analogous to `AmaisPluginCatalog.Entry` but carrying a body instead of (or alongside) function bindings:

```csharp
public sealed record WkfloSkill(
    string Name,
    string Description,                 // used by the classifier, same role as AmaisPluginCatalog.Entry.Description
    string TriggerCriteria,              // optional extra hint text folded into the classifier prompt
    string MarkdownBody,                 // the actual instructional content injected into chat history
    IReadOnlyList<string> LinkedMcpToolNames = null  // optional: tools this skill expects to be available once selected
);
```

Where the body lives (file vs. DB) is the first decision a follow-up ticket needs to make explicitly — see §5.5.

### 5.2 Extend `AmaisPluginCatalog` (or add a sibling catalog)

Either add a `Skills` list alongside `AmaisPluginCatalog.All`, or introduce a new `WkfloSkillCatalog` with the same shape (`static readonly IReadOnlyList<WkfloSkill> All`, plus `IsOfferable`/`GetOfferable` if per-tenant/per-endpoint gating is needed). Keeping it a sibling rather than merging into `AmaisPluginCatalog.Entry` avoids conflating "select a callable plugin" with "select an instructional packet" in one type — they have different injection targets even though the selection mechanism is shared.

### 5.3 Extend `AmaisSkillSelectorPlugin`'s classifier to also consider skills

`ClassifyPluginsAsync` already builds its prompt from `_availablePlugins.Select(p => $"- {p.Name}: {p.Description}")`. The same string-join approach extends naturally to a combined listing of plugins *and* skills (either as one merged list distinguished by a `Kind` discriminator, or as two clearly-labeled sections in the same prompt so the model can select from both categories in a single classification call — cheaper than a second LLM round-trip). The JSON-array response contract (`["Post"]`, `["Welcome","Page"]`, etc.) doesn't need to change shape, only which names are valid.

### 5.4 Inject the skill body via the existing `AutoInvoke*` pattern

Once a skill is selected, do **not** try to force it through the SK plugin/function pipeline (`kernel.Plugins.Add(...)`) — that pipeline is for callable functions with typed parameters. Instead, reuse the pattern already proven by `AutoInvokePageDataAsync` / `AutoInvokeAmaisKnowledgeAsync`:

1. Check `chatHistory` for a dedupe marker (e.g. `"Skill body (name: {skill.Name})"`) so the same skill isn't injected twice in one conversation.
2. If not already present, call `chatHistory.AddMessage(AuthorRole.System, skill.MarkdownBody, metadata: new Dictionary<string, object> { [CommonMetadataConsts.Timestamp] = ..., [CommonMetadataConsts.ChannelMessageType] = ChannelMessageType.None })`.
3. If the skill has `LinkedMcpToolNames`, ensure those tools/plugins are also loaded into `kernel.Plugins` (reusing `AmaisPluginCatalog.CreateFilteredPlugin`-style loading) so the model can act on the guidance it just received.

This would live as a new `AutoInvokeSkillAsync`-style method in `SemanticKernelExtensions.cs`, called from wherever `AutoSelectAmaisPluginsAsync` is invoked today, right after skill selection resolves.

### 5.5 DB/versioning implications for `Agent`/`Tool` if this becomes admin-editable

Two viable storage models, in increasing order of effort:

- **File-based, code-deployed skills** (fastest to ship): skill bodies live as `.md` files in the repo (e.g. under a new `Skills/` folder analogous to `Plugins/`), loaded at startup into an in-memory catalog exactly like `AmaisPluginCatalog.All` is today. Zero schema changes. Downside: not tenant-editable without a code deploy — fine for a first cut of platform-curated skills (e.g. "how to draft a blog post," "how to handle a refund"), not fine if tenants need to author their own.
- **DB-backed, admin-editable skills** (needed once tenants must author their own): requires a new `Skill` entity (`Name`, `Description`, `TriggerCriteria`, `Body`, `TenantId`/nullable-for-shared, `IsActive`, soft-delete flag consistent with the existing `Agent`/`Tool` global query-filter convention) plus a new SSDT `.sql` table file under `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.DB/dbo/Tables/Skill.sql` (per this repo's schema-change convention — **never** `dotnet ef migrations add`), registered in `AMAIS.WKFLO.Common.MD.DB.sqlproj`, plus MD API CRUD commands under `AMAIS_WKFLO_MD_API/.../Agents/Commands/`-equivalent (`Skills/Commands/`). A join table analogous to `AgentTool` (`AgentSkill`) would let an admin attach specific skills to specific agents, mirroring how tools are attached today. Versioning (so a skill body can be edited without silently changing behavior for conversations already using the old text) would most cleanly follow the existing `TenantAgent` → `PublishedTenantAgent` publish/snapshot pattern already used for agents, rather than inventing a new versioning mechanism.

**Recommendation for a follow-up ticket:** start file-based (no schema change, fastest validation of the pattern end-to-end), and only invest in the DB-backed/admin-editable model once there's a concrete tenant-authoring requirement — consistent with how `AmaisPluginCatalog` itself started as a static, code-owned list rather than a DB table.

---

## 6. References

All paths are relative to the `wkflo_services` repository root; verified present as of this research pass.

- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/Entities/Agent.cs` — `Agent.SystemPrompt`, the single monolithic per-tenant prompt column.
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Extensions/SemanticKernelExtensions.cs` — `GetWkflowPromptAsync`, `AddWkflowToolsAsync`, `AutoInvokePageDataAsync`, `AutoInvokeTextFeatureDataAsync`, `AutoInvokeGalleryFeatureDataAsync`, `AutoInvokeAmaisKnowledgeAsync`, `AutoSelectAmaisPluginsAsync` — the one-off system-message injection pattern to reuse for skill bodies.
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Plugins/Amais/SkillSelector/AmaisSkillSelectorPlugin.cs` — the classifier-selected dynamic plugin loader; nearest existing precedent to a Skills selector.
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Plugins/Amais/AmaisPluginCatalog.cs` — the declarative name+description catalog with per-tenant endpoint gating (`Entry`, `IsOfferable`, `GetOfferable`, `CreateFilteredPlugin`).
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Plugins/Amais/BaseAmaisPlugin.cs` — shared base for AMAIS plugins that call out to the MCP server.
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Interfaces/Services/Mcp/IMcpClientService.cs` and `Services/Mcp/McpClientService.cs` — MCP client-side integration that surfaces MCP tools as SK kernel functions.
- `AMAIS_WKFLO_MCP_SERVER/src/AMAIS.WKFLO.MCP/AMAIS.WKFLO.MCP.WebAPI/Program.cs` — `AddMcpServer().WithTools<T>()` registration chain.
- `AMAIS_WKFLO_MCP_SERVER/src/AMAIS.WKFLO.MCP/AMAIS.WKFLO.MCP.Application/Tools/Amais/Post/AmaisBlogPostTools.cs` and `AmaisBlogPostToolsCreate.cs` — `[McpServerToolType]` / `[McpServerTool, Description(...)]` registration pattern.
- `AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.Application/Agents/Commands/` — existing MD API CRUD pattern a `Skills/Commands/` equivalent would follow.
- `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.DB/dbo/Tables/` and `AMAIS.WKFLO.Common.MD.DB.sqlproj` — SSDT schema-change location/registration convention for a future `Skill` table.
- `CLAUDE.md` (repo root) — "Existing Tool Types" table and the 4-step process for adding a new tool type, whose shape (constant + config model + SK plugin + `SemanticKernelExtensions` case) is the closest existing onboarding recipe to model a "how to add a new skill" recipe on.

### Background only (PetraCMS — confirmed out of scope for implementation)

PetraCMS only *consumes* WKFlo agent configuration via `Amais.WKFLO.Client` and embeds the chat/MAISY UI; it has no local prompt-layering mechanism today, so a Skills feature would not require PetraCMS-side changes unless a future ticket wants tenant admins to author skills from a PetraCMS screen (at which point it would need a new UI surface, not just API wiring). Files reviewed for this context: `PetraCMS.Web/Areas/CHAT/Views/WKFloWebChat/Index.cshtml`, `PetraCMS.ApplicationServices/Chats/Settings/Commands/SaveSettings/SaveSettingsCommandHandler.cs`, `PetraCMS.ApplicationServices/Chats/WKFloWebChat/Commands/ProvisionMaisyAgent/ProvisionMaisyAgentCommandHandler.cs`, `PetraCMS.Data/Repositories/WKFloAgentRepository.cs`, `PetraCMS.Core/DTOs/WKFlo/WKFloAgentDto.cs`, `Amais.WKFLO.Client/Models/ToolConfigs/*.cs`, `Amais.WKFLO.Client/Api/WKFloApi.cs`.
