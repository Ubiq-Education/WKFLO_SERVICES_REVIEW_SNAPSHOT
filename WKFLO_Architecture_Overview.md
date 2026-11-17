# WKFLO — Architecture & Technology Overview

## What is WKFLO?

WKFLO is a **multi-tenant AI agent platform** built for educational institutions. Each school (tenant) configures its own AI agents with custom system prompts, AI models, knowledge bases, and tools. Agents can be embedded directly into any external website or system via a widget or iFrame — no deep integration required.

---

## System Services

Traffic from the browser or embedded widget flows through a single entry point (the BFF) which routes requests to the appropriate backend service.

```
Browser / Widget
      |
      | HTTPS
      v
BFF (YARP Reverse Proxy  :32771)
  ├── /md/**  →  MD API      :32773
  ├── /ai/**  →  AI API      :32775
  └── /mcp/** →  MCP Server  :32875
```

| Service | Purpose |
|---------|---------|
| **BFF** | YARP reverse proxy. Single ingress for all frontend traffic. |
| **MD API** | Master Data API. Manages agents, tools, AI models, tenants, secrets, and knowledge base configuration. |
| **AI API** | Chat orchestration. Executes agents, routes messages, manages chat sessions and history. |
| **MCP Server** | Model Context Protocol server. Exposes CMS operations (blog posts, pages, gallery) as AI-callable tools. |
| **Worker** | Background job processing via a plain Dapr pub/sub subscriber dispatching to `IJobHandler` implementations. (Dapr Workflows/Activities are used in the AI API, not here — see `MultiAgentOrchestratorWorkflow` and related.) |

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language / Runtime | C# / .NET 10 |
| API framework | ASP.NET Core Web API |
| AI orchestration | Microsoft Semantic Kernel v1.77 |
| Internal messaging (CQRS) | MediatR |
| Reverse proxy / gateway | YARP v2.3 |
| ORM | EF Core 10 (SQL Server) |
| Validation | FluentValidation |
| Object mapping | Mapster (AutoMapper backend exists but is currently disabled/commented out) |
| Background jobs | Dapr Workflows / Actors (AI API); plain Dapr pub/sub (Worker) |
| Pub/sub messaging | Dapr + RabbitMQ |
| Vector database | PostgreSQL + pgvector (Neon cloud) |
| Knowledge base / full-text search | Elasticsearch |
| Chat history storage | Azure Blob Storage |
| Distributed cache | Redis (EF Core L2, 30-min TTL) |
| Authentication | Microsoft.Identity.Web, JWT Bearer |
| Observability | OpenTelemetry → Grafana Alloy → Elasticsearch |
| API documentation | Microsoft.AspNetCore.OpenApi + Scalar |
| Document parsing | PdfPig (PDF), ExcelDataReader (.xls), own CFB + piece-table reader (.doc), OpenXml (.docx/.xlsx) |

---

## Model-Agnostic Capability

WKFLO is not tied to any single AI provider. Each agent has an `AIModel` configuration record in the database that specifies the provider, model identifier, endpoint, API version, and a reference to the API key (stored encrypted in `Secret`). At runtime, `AIProviderHelper.GetKernel()` reads that record and builds a Microsoft Semantic Kernel instance wired to the correct provider SDK — all four feed into the same Semantic Kernel pipeline, so the rest of the system is unaware of which provider is active.

**Supported providers:**

| Provider | SDK used |
|----------|---------|
| Azure OpenAI | Azure.AI.OpenAI |
| OpenAI | Semantic Kernel OpenAI connector |
| Anthropic (Claude) | Anthropic SDK v12 wrapped as IChatClient |
| Google Gemini | Semantic Kernel Google connector |

Swapping an agent from GPT-4o to Claude requires only a database record change — no code deployment.

---

## Connectors / Tools System

Agents are extended with **tools** (also called connectors). Each tool is a database record (`Tool` entity) with a `Type` field that identifies its category and a `Config` JSON blob for type-specific settings. When an agent session starts, `SemanticKernelExtensions.AddWkflowToolsAsync()` reads the agent's assigned tools and registers the corresponding Semantic Kernel plugins.

**Tool types:**

| Type | What it does |
|------|-------------|
| **Knowledge** | Retrieves relevant document chunks from the Elasticsearch knowledge base (RAG). The agent uses these as context before answering. |
| **Priority Q&A** | A curated set of authoritative question-answer pairs. Checked before the language model is consulted, guaranteeing consistent answers to known questions. |
| **MCP** | Connects to any external [Model Context Protocol](https://modelcontextprotocol.io/) server. The server's tools are automatically surfaced to the agent as callable functions. Used for CMS operations (blog posts, pages, gallery). |
| **AMAIS Integration** | Deep integration with the AMAIS platform — knowledge search, page/post management, gallery features. |
| **Chat Classification** | Automatically classifies incoming messages and routes them before the model is called. |
| **Contact Form** | Displays a contact form to collect user contact info mid-chat; skips itself once already collected for that subject. |
| **HTTP** | Calls an external HTTP endpoint as a kernel function, per the tool's configured request shape. |
| **Image Generation** | Generates/edits images via a configured AI image provider, uploading results to blob storage. |

Tools are additive — a single agent can have any combination of tool types attached.

---

## Per-School Security & Governance

Security and data isolation operate at three layers:

### 1. Identity & Authentication

Every request must carry a valid JWT Bearer token issued by the school's identity provider (IdP), validated by `Microsoft.Identity.Web`. Machine-to-machine flows use an API key instead. A `UserContext` scoped service is populated from the token claims and injected throughout the request pipeline.

### 2. Tenant Resolution

Every request includes an `amais-wkflo-tenant-guid` header. The `TenantContextResolver` middleware resolves this to a `TenantContext` — a scoped DI service available to all handlers. There is no way to issue a request on behalf of a tenant you are not authenticated for.

### 3. Data Isolation

Every core entity — `Agent`, `Tool`, `Secret`, `AIModel` — carries a `TenantId` foreign key. All database queries filter by the current tenant. Application-layer validators (`BaseMasterDataValidator`) enforce cross-entity ownership checks (e.g., an agent cannot reference a tool from a different tenant). Chat history blobs in Azure Blob Storage are namespaced `{tenantName}/{userId}/{agentId}/{sessionId}.json`, providing additional storage-level isolation.

| Layer | Mechanism |
|-------|----------|
| Authentication | JWT Bearer / API key |
| Tenant identification | `amais-wkflo-tenant-guid` request header |
| Runtime scope | `TenantContext` scoped DI service |
| Database rows | `TenantId` FK on every entity |
| Chat history | Blob path namespaced by tenant |

---

## Architecture Diagram

See `WKFLO_Architecture.drawio` (importable at https://app.diagrams.net via File → Import From → Device). The diagram shows four layers — Client, WKFLO Platform Services, AI Engine (inside AI API), and Data/Infrastructure — with annotated security callouts. The BFF is drawn as a standalone node rather than its own layer.
