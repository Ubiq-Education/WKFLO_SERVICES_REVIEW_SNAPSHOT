# WKFLO Services — Claude Development Guide

## What This Project Is

Multi-tenant AI agent platform built on .NET 10. Tenants configure agents with custom tools, prompts, and AI models. Agents can be embedded via widget/iframe in external systems. The UI project lives in a separate repository.

---

## gstack
Use /browse from gstack for all web browsing. Never use mcp__claude-in-chrome__* tools.
Available skills: /office-hours, /plan-ceo-review, /plan-eng-review, /plan-design-review,
/design-consultation, /review, /ship, /land-and-deploy, /canary, /benchmark, /browse,
/qa, /qa-only, /design-review, /setup-browser-cookies, /setup-deploy, /retro,
/investigate, /document-release, /codex, /cso, /autoplan, /careful, /freeze, /guard,
/unfreeze, /gstack-upgrade.

---

## Services Overview

| Directory | Service | Purpose |
|-----------|---------|---------|
| `AMAIS_WKFLO_MD_API` | MD API | Master data — manages agents, tools, AI models, secrets, tenants, knowledge base |
| `AMAIS_WKFLO_AI_API` | AI API | Chat orchestration — executes agents, routes messages, manages chat history |
| `AMAIS_WKFLO_BFF` | BFF | YARP reverse proxy — routes all frontend traffic to MD/AI APIs |
| `AMAIS_WKFLO_MCP_SERVER` | MCP Server | Model Context Protocol — exposes tools to AI agents via HTTP |
| `AMAIS_WKFLO_DXP_MCP_SERVER` | DXP MCP Server | Read-only DXP page analytics tools (`get_top_pages`, `get_page_performance`); calls only the DXP Report API. Tenant always comes from the CMS JWT `site_id` claim |
| `AMAIS_WKFLO/AMAIS_WKFLO_COMMON` | Common | Shared domain entities, infrastructure, interfaces used by all services |

**Traffic flow**: Frontend → BFF (`:32771`) → `/md/**` → MD API | `/ai/**` → AI API | `/mcp/**` → MCP Server

---

## Architecture Pattern

Clean Architecture + CQRS via MediatR. Every feature follows the same vertical slice structure:

```
Application/
  {Entity}/
    Commands/{CommandName}/
      {CommandName}Command.cs          ← IRequest<Result>, IRequestId<int>, IMap
      {CommandName}CommandHandler.cs   ← IRequestHandler<Command, Result>
      {CommandName}CommandValidator.cs ← BaseAIValidator / BaseValidator (FluentValidation)
    Queries/{QueryName}/
      {QueryName}Query.cs
      {QueryName}QueryHandler.cs
    Events/{EventName}/
      {EventName}DomainEvent.cs
      {EventName}DomainEventHandler.cs
    Models/
      {FeatureName}Result.cs           ← IMapFrom<Entity>

WebAPI/
  EndPoints/{Scope}/{Entity}/
    {Entity}{Action}.cs                ← BaseAsyncEndpoint<TRequest, TResponse>
```

---

## Where Things Go — Quick Reference

### Adding a new feature to MD API

| What | Where |
|------|-------|
| Command/Query logic | `AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.Application/{Entity}/Commands/` |
| Result DTO | `AMAIS.WKFLO.MD.Application/{Entity}/Models/` |
| Endpoint (Tenant scope) | `AMAIS.WKFLO.MD.WebAPI/EndPoints/Tenant/{Entity}/` |
| Endpoint (Marketplace scope) | `AMAIS.WKFLO.MD.WebAPI/EndPoints/Marketplace/{Entity}/` |
| Unit tests | `AMAIS_WKFLO_MD_API/tests/AMAIS.WKFLO.MD.UnitTests/Application/{Entity}/Commands/{CommandName}/` |
| Functional tests | `AMAIS_WKFLO_MD_API/tests/AMAIS.WKFLO.MD.FunctionalTests/` |

### Adding a new feature to AI API

| What | Where |
|------|-------|
| Command/Query logic | `AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.Application/Agents/Commands/` |
| Result DTO | `AMAIS.WKFLO.AI.Application/Agents/Models/` |
| Endpoint (Tenant) | `AMAIS.WKFLO.AI.WebAPI/EndPoints/Tenant/Agents/` |
| Endpoint (Public) | `AMAIS.WKFLO.AI.WebAPI/EndPoints/Public/Agents/` |
| Unit tests | `AMAIS_WKFLO_AI_API/tests/AMAIS.WKFLO.AI.UnitTests/Application/Agents/Commands/{CommandName}/` |
| Functional tests | `AMAIS_WKFLO_AI_API/tests/AMAIS.WKFLO.AI.FunctionalTests/EndPoints/` |

### Adding a new domain entity

| What | Where |
|------|-------|
| Entity class | `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/Entities/` |
| EF Core configuration | `AMAIS.WKFLO.Common.MD.Domain/Data/Config/` |
| Add DbSet to interface | `AMAIS.WKFLO.Common.MD.Domain/Entities/Interfaces/IMasterDataDbContext.cs` |
| Add DbSet to context | `AMAIS.WKFLO.Common.MD.Domain/Data/MasterDataDbContext.cs` |

### Adding a new DI service

| Layer | File |
|-------|------|
| WebAPI infrastructure | `WebAPI/Infrastructure/Extensions/ServiceCollectionExtensions.cs` |
| Application layer | `Application/Extensions/ServiceCollectionExtensions.cs` |
| Common infrastructure | `AMAIS.WKFLO.Common.Infrastructure/Extensions/ServiceCollectionExtensions.cs` |

---

## Key Base Classes

| Class | Location | Use when |
|-------|----------|----------|
| `BaseAsyncEndpoint<TReq, TRes>` | `WebAPI/EndPoints/` | Every endpoint inherits this |
| `BaseAsyncEndpoint<TReq1, TReq2, TRes>` | Same | Endpoint with route + body params |
| `BaseAsyncEndpoint<TRes>` | Same | Endpoint with no input |
| `Entity<int>` | `Common/Models/Entities/Entity.cs` | Every domain entity inherits this |
| `BaseAIValidator<T>` | `Common.AI.Application/Common/Validators/` | Validators in AI API |
| `BaseValidator<T>` | `Common/Models/Validators/` | Validators in MD API |

---

## Result Pattern

All handlers return `Result`. Never throw exceptions for control flow.

| Type | HTTP | When |
|------|------|------|
| `SuccessResult<T>` | 200 | Happy path |
| `InvalidResult` | 400 | Validation error |
| `NotFoundResult` | 404 | Entity not found |
| `UnauthorizedResult` | 401 | Auth failure |
| `UnexpectedResult` | 500 | Unhandled error |

---

## Naming Conventions

| Artifact | Pattern | Example |
|----------|---------|---------|
| Command | `{Action}{Entity}Command` | `CreateTenantAgentCommand` |
| Query | `{Get/List}{Entity}Query` | `GetTenantAgentByIdQuery` |
| Handler | `{CommandName}Handler` | `CreateTenantAgentCommandHandler` |
| Validator | `{CommandName}Validator` | `CreateTenantAgentCommandValidator` |
| Endpoint | `{Entity}{Action}` | `TenantAgentCreate` |
| Result DTO | `{Feature}Result` | `TenantAgentCreateResult` |
| Settings | `{Feature}Settings` | `BlobStorageSettings` |
| Unit test class | `{HandlerName}Tests` | `CreateTenantAgentCommandHandlerTests` |
| Unit test fixture | `{HandlerName}Testing` | `CreateTenantAgentCommandHandlerTesting` |

---

## OpenAPI / Scalar / Tenant Header

API docs use `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` (Swashbuckle removed). Scalar UI is at `/scalar/v1`; OpenAPI JSON at `/openapi/v1.json`. Enabled in non-dev environments via `SCALAR_ENABLED=true`.

The `amais-wkflo-tenant-name` header is injected via an operation transformer in `AddOpenApi()`.

- **MD API**: triggers for tag `"Tenant Area"` — all MD tenant endpoints use this tag ✓
- **AI API**: triggers for tags `"Tenant Area"` **or** `"Tenant API"` — AI tenant endpoints use `"Tenant API"` ✓

Endpoint annotations use `[Tags("...")]`, `[EndpointName("...")]`, `[EndpointSummary("...")]`, `[EndpointDescription("...")]` — no Swashbuckle attributes. Request body examples live in `Infrastructure/OpenApi/Transformers/Examples/` as `IOpenApiOperationTransformer` classes registered via `options.AddOperationTransformer<T>()`.

When creating a new tenant endpoint, use the matching tag or the header won't appear in the docs.

---

## Databases

| DB | Used by | Access pattern |
|----|---------|---------------|
| SQL Server | MD API, AI API | EF Core + `IMasterDataDbContext` |
| PostgreSQL (Neon) | AI API | Raw Npgsql via `INpgsqlProvider`; pgvector (1536-dim) via `VectorStoragePgsqlProvider` for LTM / semantic search |

Connection strings come from environment variables (`DB_CONNECTION_STRING`), never hardcoded.

---

## DB Schema Management (SSDT)

The project uses **SSDT** (SQL Server Data Tools), not EF Core migrations. Never suggest `dotnet ef migrations add`.

Every new table requires two things:
1. A SQL file at `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.DB/dbo/Tables/[EntityName].sql`
2. Registration in `AMAIS.WKFLO.Common.MD.DB.sqlproj` inside the existing `<ItemGroup>`:
   ```xml
   <Build Include="dbo\Tables\[EntityName].sql" />
   ```

---

## AI Providers

The system supports four AI providers through a common Semantic Kernel abstraction. `AIProviderHelper.GetKernel()` reads the agent's `AIModel` DB record and builds a provider-specific SK kernel — **swapping the model on an agent requires only a DB record change, no code change**.

| Provider | `AIModel.Provider` value | SDK |
|----------|------------------------|-----|
| Azure OpenAI | `"azure"` | Azure.AI.OpenAI |
| OpenAI | `"openai"` | SK OpenAI connector |
| Anthropic (Claude) | `"anthropic"` | Anthropic SDK v12 wrapped as `IChatClient` |
| Google Gemini | `"google"` | SK Google connector |

Key files:
- Factory: `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Helpers/AIProviderHelper.cs`
- Provider models: `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/Models/AI/Providers/`
- Agent service: `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Services/SemanticKernel/SemanticKernelAgentService.cs`

---

## Existing Tool Types

The eight built-in tool types and their runtime behavior:

| Constant (`CommonToolTypeConsts`) | Value | What it does |
|-----------------------------------|-------|-------------|
| `Knowledge` | `"Knowledge"` | Elasticsearch RAG — retrieves relevant document chunks and injects them as context |
| `PriorityQA` | `"Priority_QA"` | Static Q&A pairs checked before the model — guarantees consistent authoritative answers |
| `MCP` | `"MCP"` | Connects to any external MCP server; its tools become SK kernel functions. Supports OAuth 2.1 (authorization-code + PKCE) MCP servers — e.g. `mcp.humantic.ai` — via `McpServerConfig.AuthType = OAuth2`; tenants connect once through the MD API's `tools/{id}/mcp/oauth/*` consent endpoints and tokens are persisted/refreshed automatically thereafter |
| `IntegrationAmais` | `"Integration_Amais"` | Deep AMAIS CMS integration (blog, pages, gallery, skill selector) |
| `ChatClassification` | `"Chat_Classification"` | Auto-classifies incoming messages before the model is called |
| `ContactForm` | `"Contact_Form"` | Displays a contact form to collect user contact info mid-chat; skips itself if already collected for the subject |
| `Http` | `"HTTP"` | Calls an external HTTP endpoint as a kernel function, per the tool's configured request shape |
| `ImageGeneration` | `"Image_Generation"` | Generates/edits images via a configured AI image provider (`edit_image`), uploading results to blob storage |

Plugin implementations: `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/Plugins/`

---

## Adding a New Tool Type

Tool capabilities are identified by `Tool.Type` (a free-form string), **not** by `Tool.ToolType` (the EF TPH discriminator). Adding a new tool type requires exactly 4 changes — no database or MD API changes needed:

1. Add a constant to `CommonToolTypeConsts` in `AMAIS.WKFLO.Common/Models/CommonConsts.cs`
2. Create a config model at `AMAIS.WKFLO.Common/Models/Config/{Name}ToolConfig.cs` — add `[JsonProperty("camelCase")]` to every property so the class matches the camelCase JSON the UI saves
3. Create a SK plugin at `AMAIS.WKFLO.Common.AI.Application/Plugins/{Name}/{Name}Plugin.cs` — constructor receives `Tool`, `IChatNotificationService`, `ChatContext`; methods decorated with `[KernelFunction]` and `[Description]`
4. Add a `case` in `SemanticKernelExtensions.AddWkflowToolsAsync` (`SemanticKernelExtensions.cs`) to instantiate and register the plugin

All CRUD for the tool uses the existing tenant tool endpoints; tool-specific settings are stored as JSON in `Tool.Config`.

---

## Tagging System

Tenants can assign free-form colored labels (Tags) to Agents, Tools, and AIModels. The system uses two entities:

- **`Tag`** (`Entities/Tag.cs`) — tenant-scoped, Name + Color (nullable hex), soft-delete via `IsDeleted`.
- **`EntityTag`** (`Entities/EntityTag.cs`) — polymorphic junction with composite PK `(TagId, EntityType, EntityId)`. **No soft-delete** — rows are hard-deleted on unassign. `EntityType` is a string discriminator (`"Agent"`, `"Tool"`, `"AIModel"`) defined in `TaggableEntityTypes` in `CommonConsts.cs`. This pattern follows `UserStar` — EF Core cannot configure a FK on a generic abstract base class.

`BaseMasterDataValidator` exposes `BeExistingTag(IRequestId<int>)` for tag existence checks. Validators for Assign/Unassign reject unknown `EntityType` values with `WKFLOMD0905`. To support a new taggable entity type, add a constant to `TaggableEntityTypes` and update both validators. Endpoints live in `EndPoints/Tenant/Tags/`; routes follow `GET /api/v1/tenant/tags`, `POST /api/v1/tenant/tag/{tagId}/entity`, `DELETE /api/v1/tenant/tag/{tagId}/entity/{entityType}/{entityId}`, and `GET /api/v1/tenant/entity/{entityType}/{entityId}/tags`.

---

## EF Core Second-Level Cache

`EFCoreSecondLevelCacheInterceptor` is already configured globally on **HybridCache** (30-minute absolute expiration; `AddWkfloEfSecondLevelCache` in `Common.MD.Domain/Extensions`, called by `AddDomain`). Opt in per query with `.Cacheable()`:

```csharp
var result = await _db.Tenants
    .AsNoTracking()
    .Where(t => t.Name == _tenantContext.TenantName)
    .Cacheable()
    .FirstOrDefaultAsync(cancellationToken);
```

- Use `.AsNoTracking().Cacheable()` on all read-only queries in handlers
- Do **not** add `.Cacheable()` to queries that track entities for updates
- Cache is auto-invalidated on any write to the affected entity type

**Redis (shared L2 + invalidation broadcast).** MD API and AI API are separate processes with several replicas each, so a per-process cache would serve stale data after a write made elsewhere. When Redis is configured, HybridCache uses it as L2 and `BroadcastingEfCacheServiceProvider` publishes every invalidation on a Redis pub/sub channel; `EfCacheInvalidationSubscriber` (one subscription per process) applies other processes' invalidations to the local HybridCache. The broadcast is required: HybridCache reads a tag's invalidation time from L2 only once per process, so L2 alone does not propagate later invalidations. Cache and broadcast failures are logged, never thrown; the 30-minute TTL is the fallback.

- Without Redis configured (local runs, Worker, tests) the cache is per process (L1 only) and nothing is broadcast.
- Redis is resolved in `AMAIS.WKFLO.Common/Redis` from env vars: `REDIS_CONNECTION_STRING` (local docker-compose) or `AZURE_REDIS_HOSTNAME` (`host:port`, port defaults to 10000) + `AZURE_REDIS_CLIENT_ID` (client id of the Container Apps identity that has the Redis access policy). Never use `AZURE_CLIENT_ID` or `MI_CLIENT_ID` for Redis.
- Keys use the `amais-wkflo-` Redis instance prefix (EF keys and tags add `ef-`). Cached payloads are Newtonsoft JSON with type names restricted by `EfCacheSerializationBinder` (checked before any type is loaded); a new cached column type that is rejected must be added to its allowlist. Dates stay ISO text and decimals stay decimals (`DateParseHandling.None`, `FloatParseHandling.Decimal`); the interceptor's reader converts them by column type.
- `BroadcastingEfCacheServiceProvider` is always registered (also without Redis): it reads with the invalidation tags so that L1 entries loaded from L2 are dropped by `RemoveByTag`. Without Redis its transport is a no-op.
- The broadcast happens **after the transaction commits**. A write's tags are invalidated locally at once (library behaviour), but `EfCacheInvalidationInterceptor` collects them per `DbContext` (`PendingEfCacheInvalidations`) and, on commit (or when a SaveChanges ends outside a transaction), invalidates locally again and publishes them in one message; a rollback or failed SaveChanges publishes nothing. Writes outside SaveChanges and transactions (`ExecuteUpdate`, raw SQL) and `ClearAllCachedEntries` publish at once. The interceptor must be registered **before** `SecondLevelCacheInterceptor` (`AddDomain` and the functional test `AddDomainTest` do), and its hooks must stay synchronous so the context it records flows to the library.
- Redis outages: commands fail fast (`BacklogPolicy.FailFast`, short timeouts), `CircuitBreakerDistributedCache` makes the L2 behave as empty for 30 s after a failure, and reconnects are retried every 30 s. The local cache is cleared when the subscription reconnects.
- Tests: functional test setups register `WrappedDistributedCache` as L2. HybridCache silently disables its L2 when the distributed cache is exactly a `MemoryDistributedCache`, so never use `AddDistributedMemoryCache()` for this. `EfCacheInvalidationBroadcastTests` shows how to simulate two replicas with an in-memory `IEfCacheInvalidationTransport` (register it after `AddWkfloEfSecondLevelCache` to replace the no-op one).

---

## Global EF Core Query Filters

The following entities have `!m.IsDeleted` applied globally in `MasterDataDbContext.OnModelCreating`:

- `Agent` (and subclasses: `TenantAgent`, `MarketplaceAgent`, `PublishedTenantAgent`)
- `Tool` (and subclasses: `TenantTool`, `MarketplaceTool`, `PublishedTenantTool`)
- `AgentChatSetting`
- `PredefinedPrompt`
- `Secret`
- `AIModel`
- `User`
- `Tag`

**Impact on tests**: A soft-deleted entity is invisible to all EF queries including validators. Expect **400** (not 404) for both non-existent and soft-deleted entities.

---

## Chat Blob Storage

Chat history lives in Azure Blob Storage (`chat-history` container) via `IChatStoreService`. The blob path is:
```
{tenantName}/{userId}/{agentId}/{sessionId}.json
```
where `userId` defaults to `Guid.Empty.ToString()` when null (M2M/anonymous flows).

**Critical:** Every call to `SaveChatAsync`, `LoadChatAsync`, `RateMessageAsync`, `DeleteChatAsync`, and `ListChatsAsync` resolves the path independently. All calls for the same conversation **must pass the same `userId`**, otherwise they silently target different blobs. Any handler touching `IChatStoreService` must inject `UserContext` and forward `_userContext.Guid`.

**Rating key:** Ratings are stored in `PersistedChat.MessageRatings` as `Dictionary<string, string>` keyed by the **GUID** in `CommonMetadataConsts.MessageId` metadata on each SK `ChatMessageContent`. Values are `"thumbsUp"` or `"thumbsDown"`. Every message creation path must set this metadata; never use timestamps as rating keys.

**Rating counters:** Both `RateMessageAsync` and `SaveChatAsync` return absolute `(int thumbsUp, int thumbsDown)` counts recomputed from the blob after each write. Callers must **assign** these directly — never use `+=`/`-=`. `SaveChatAsync` callers pass the counts to `SyncConversationCommand`, which updates `Conversation.TotalThumbsUp/TotalThumbsDown` in SQL on every message save (self-healing).

**Preserving ratings on save:** `SaveChatAsync` reads the existing blob before overwriting so `MessageRatings` are never erased. Any future method that writes a full `PersistedChat` to blob must follow the same read-preserve-write pattern — creating a new `PersistedChat` from scratch without copying existing ratings silently destroys all rating data.

---

## Chat Documents (large attachments)

A document whose extracted text is longer than `ChatDocumentSettings.InlineMaxChars` (50,000) is **not** put in the chat history. `ProcessFilesAsync` (`SemanticKernelAgentService`) extracts the text once and `IChatDocumentIndexer` splits it (`DocumentChunker`, ~1,500 chars, page/sheet markers are hard breaks), embeds it in batches of 64 in coverage (bit-reversal) order, and stores the chunks in **one shared Postgres vector collection** (`chat_documents`, created on first use like the LTM collections; flat/exact index; `PostgresChatDocumentStore`). Limits: 2,000 chunks and 90 s per document, after which the document is `partial` (chunks stay spread evenly over the whole text). A summary is built once from a 200,000-char sampled excerpt (`DocumentExcerptSampler`) with the agent's own model, and its tokens are reported as tenant usage. Shorter documents stay inline (`"Document content:\n..."`), exactly as before.

- **History:** the document is a short message (`AttachedDocumentMessage`: file, size, characters, summary) with the same Timestamp/MessageId and FileName/FileType/FileSize metadata plus `DocumentId`, `DocumentCharCount`, `DocumentChunkCount`, `DocumentIndexedChunkCount`, `DocumentIndexStatus` (`ready`/`partial`/`failed`) on its text item. An old `"Document content:\n"` message over the threshold is indexed and replaced in place when the chat is loaded (`ConvertLegacyDocumentsAsync`).
- **Per turn:** `ChatDocumentRetriever` embeds the user's message once and adds the best passages (top 8, 16K chars, document order, neighbours) to **this turn's instructions only** (`BuildChatCompletionAgentInstructions`), never to the `ChatHistory` or its pinned system message; with no good match it gives the summary plus an excerpt. Text and voice. No documents means no embedding call. The `search_attached_documents` function (query and optional fileName only) is registered only when the chat has indexed documents, counts toward `MaxToolCallsPerTurn`, and its result is replaced by `[document passages omitted]` before the history is saved (`BuildPersistableChatHistory`).
- **Isolation:** every store query and delete takes a `ChatDocumentScope` (tenant from `TenantContext`, the turn's session id, user from `UserContext`, `Guid.Empty` for anonymous chats) and filters by all three plus the document ids (`ChatDocumentFilters`). Document ids come from the agent's own history, never from model output or the request. Do not add a store method that reads or deletes by document id alone.
- **Forwarding:** `publish_agent_event` forwards an indexed document as `FileAttachment.SourceDocumentId`; the target agent gets its own copy of the chunks (same tenant, session, user).
- **Cleanup:** deleting a chat or wiping an agent's short-term memory (MD API) deletes that agent's chunks in the session. Documents are not purged on a schedule (the daily `PurgeChatImagesWorkflowActivity` purges chat images only). Known limitation: chunks can be left behind, unreachable but still scoped, when that delete fails or when a turn fails after indexing (a new attachment or a converted legacy message) and before the history is saved; nothing removes them later. Functional test hosts replace the store with `NullChatDocumentStore`.
- Messages that only carry an attachment (`ChatHistoryMessageExtractor.IsAttachment`) are skipped by conversation analysis and the Worker topic classifier.

---

## DXP Chatbot Analytics Events — TopicVersion is mandatory on any topic-carrying event

`DxpChatbotAnalyticsService` reports several event types to AMAIS_DXP (`ReportMessageTopicAsync`, `ReportMessageFeedbackAsync`, etc.). **Any event that carries `TopicId`/`TopicName` must also carry `TopicVersion`** (the tenant's Chat_Classification tool config version at the time that topic assignment was made, resolved via `ChatClassificationConfigHelper.ResolveTopicVersion(config)`). This applies whether the event is reported at classification time, re-reported later (resolution judgment, feedback rating, batch reclassification), or is a brand new event type added in the future.

**Why:** AMAIS_DXP filters every topic-scoped query by the tenant's CURRENT `TopicVersion` (see US 7142's TopicVersion consistency work). An event with `TopicId`/`TopicName` but no `TopicVersion` is invisible to those filtered queries forever — not an error, just silently excluded — so omitting it is a correctness bug, not a missing nice-to-have. `MessageTopicClassification.TopicVersion` (SQL) is the durable source of truth for "what version was this message classified under" — read it from there whenever a topic-carrying event is reported after the fact (resolution re-report, feedback rating, reclassification re-report), rather than re-resolving the tenant's CURRENT config version, which would be wrong for anything but the original classification-time report itself.

When adding a new `Report*Async` method (or a new optional field to an existing one) that includes topic identity, add `topicVersion` as a parameter following the same `AddIfPresent` omit-if-null convention every other optional field on this service already uses — never write an empty string (see this service's own doc comments for why that breaks ES `exists` filters and can lock in the wrong dynamic mapping).

---

## Mapping — IWkfloMapper and IMap

Handlers inject `IWkfloMapper` (not `AutoMapper.IMapper`). The interface is in `AMAIS.WKFLO.Common/Mappings/` and has two implementations, both defined in `WkfloMapperExtensions`. **Mapster is the only active backend today** — `WkfloMapperExtensions.CreateMapper()` defaults to `WkfloMapperBackend.Mapster`, and the AutoMapper registration path (`AddAutoMapperWkfloMapper`, `CreateAutoMapper()`) is currently commented out in source, not callable. Treat `IMap.Mapping(Profile profile)` (the AutoMapper overload) as dormant scaffolding — don't rely on it working until the AutoMapper path is re-enabled.

Every class implementing `IMap` must still have **both** mapping methods — `Mapping(Profile profile)` for AutoMapper and `Mapping(TypeAdapterConfig config)` for Mapster — since the interface is shared, but only the Mapster overload actually runs today. The interface provides a default no-op for the Mapster overload, so existing classes compile, but every real class must override it.

**Mapster rules:**
- All `.Map()` lambdas with navigation chains need explicit null guards (`src.X != null ? src.X.Y : null`); Mapster throws where AutoMapper returns null.
- When type inference fails (e.g., `Guid` → `string`, multi-level `?.`), use `AfterMapping((src, dest) => { dest.X = src.Y?.Z; })` instead of `.Map()`.
- Key equivalents: `.Ignore(d => d.X)` = AutoMapper Ignore; `.TwoWays()` = `ReverseMap()`; `.IgnoreNullValues(true)` = ForAllMembers null condition.
- **Entity-only fields are left at C# default after convention mapping.** If the command/DTO has no `Guid` property, `_mapper.Map<Tenant>(command)` leaves `tenant.Guid = Guid.Empty`. Set server-generated fields explicitly in the handler after mapping (e.g. `tenant.Guid = Guid.NewGuid()`).

---

## Testing

### Unit Tests — MD API pattern
Two files per handler, mirroring the Application folder structure:

```
{CommandName}CommandHandlerTesting.cs  ← fixture: sets up mocks + instantiates handler
{CommandName}CommandHandlerTests.cs    ← test cases: IClassFixture<*Testing>, [Theory][InlineData]
```

Stack: xUnit + Moq + MockQueryable.Moq + the real mapper (never mock `IWkfloMapper`).
Always use `TestContext.Current.CancellationToken`. Use `WkfloMapperExtensions.CreateMapper()` in test fixtures — do not write manual `TypeAdapterConfig`/`MapperConfiguration` blocks.

### Unit Tests — AI API pattern
Single file per handler with inline setup in the constructor (no separate Testing fixture).

### Functional Tests
- Use `IClassFixture<TestFixture>`
- SQLite in-memory for `MasterDataDbContext`
- **Real Azure Blob Storage** for `IChatStoreService` (from `appsettings.Development.json`)
- Authenticated endpoints require `TokenBuilder.BuildJWTToken()` + `Authorization: Bearer` header
- Tenant endpoints require `amais-wkflo-tenant-name` header set to the tenant name

### Functional Test Namespace Warning
Never use `Tenant` as a namespace segment in the functional test assemblies. It shadows the `Tenant` entity class across the entire assembly.
- ❌ `EndPoints.Tenant.Agents`
- ✓ `EndPoints.TenantAgents`

---

## Environment Variables (Key ones)

| Variable | Purpose |
|----------|---------|
| `DB_CONNECTION_STRING` | SQL Server connection (MD API) |
| `REDIS_CONNECTION_STRING` | Local Redis for the EF cache (docker-compose), e.g. `redis:6379,abortConnect=false`. Takes precedence over the Azure variables |
| `AZURE_REDIS_HOSTNAME` | Azure Managed Redis endpoint `host:port` for the EF cache (MD API, AI API). Unset means L1-only cache |
| `AZURE_REDIS_CLIENT_ID` | Client id of the user-assigned identity used to authenticate to Azure Managed Redis |
| `SCALAR_ENABLED` | Set to `true` to enable Scalar/OpenAPI UI in non-dev environments |
| `OTEL_SERVICE_NAME` | OpenTelemetry service name |
| `LuckyPennySoftwareLicense` | AutoMapper license key (required in all mapper setups) |
| `SignalR_HttpTransport` | Set to `LongPolling` to disable WebSockets |

---

## Docker Services

| Service | Port (HTTP) | Port (HTTPS) |
|---------|-------------|--------------|
| BFF | 32770 | 32771 |
| MD API | 32772 | 32773 |
| AI API | 32774 | 32775 |
| MCP Server | 32874 | 32875 |
| DXP MCP Server | 32974 | 32975 |
| SQL Server | 1401 | — |

Infrastructure: RabbitMQ (Dapr pub/sub), Redis (state), Dapr sidecars, Grafana Alloy (OTel collector), Elasticsearch.

---

## Scalar Access via BFF

When `SCALAR_ENABLED=true`:

| API | UI | JSON Schema |
|-----|----|-------------|
| MD API | `{bff}/md/scalar/v1` | `{bff}/md/openapi/v1.json` |
| AI API | `{bff}/ai/scalar/v1` | `{bff}/ai/openapi/v1.json` |

**Note:** The BFF YARP appsettings must forward `/md/scalar/**`, `/md/openapi/**`, `/ai/scalar/**`, `/ai/openapi/**` to the respective backends (replacing the old `/md/swagger/**` and `/ai/swagger/**` routes).
