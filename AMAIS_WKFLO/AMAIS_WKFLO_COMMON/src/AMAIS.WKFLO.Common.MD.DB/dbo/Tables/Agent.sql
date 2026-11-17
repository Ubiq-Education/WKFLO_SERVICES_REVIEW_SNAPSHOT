CREATE TABLE [dbo].[Agent]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[AgentType] NVARCHAR(100) NOT NULL,
	[Name] NVARCHAR (300) NOT NULL,
	[Description] NVARCHAR (500) NOT NULL,
	[SystemPrompt] NVARCHAR(MAX) NOT NULL,
	[ResponseTone] NVARCHAR(50) NOT NULL DEFAULT 'Friendly',
	[IsActive] BIT NULL,
	[EnableSTM] BIT NOT NULL DEFAULT 0, -- Shared column: RunnableAgent rows (TenantAgent/PublishedTenantAgent) use it as their own runtime STM toggle; MarketplaceAgent rows independently use it as the template's install-time default (see MarketplaceAgent.EnableSTM's doc comment) - same physical column, distinct meaning per AgentType row, never both on the same row. Tightened from BIT NULL (WI 7317, 2nd regression) after backfilling legacy NULL rows via a pre-deployment script - see Script.PreDeployment.sql. The DEFAULT is defense-in-depth only (guards a future raw-SQL/bulk insert that omits the column); it does not by itself fix already-NULL rows or get exercised by EF-driven inserts, since EF always specifies every mapped column explicitly.
	[EnableLTM] BIT NOT NULL DEFAULT 0, -- Same latent nullable-column-vs-non-nullable-bool-property gap as EnableSTM above, just never triggered by an existing NULL row because every TenantAgent/PublishedTenantAgent creation path happens to set it explicitly. Tightened defensively for the same reason and via the same pre-deployment backfill.
	[EnableSpinUpAgents] BIT NOT NULL DEFAULT 0, -- Same as EnableLTM above.
	[AIModelId] INT NULL,
	[IconUrl] NVARCHAR(500) NULL, -- Absolute URL of the agent's icon. Populated on MarketplaceAgent rows (edited directly, or copied from the source TenantAgent's AgentChatSetting.ChatbotIconPath at share-time — see ShareTenantAgentCommandHandler); currently unused on TenantAgent/PublishedTenantAgent rows
	[TenantId] INT NOT NULL,
	[PublishedTenantAgentId] INT NULL, -- Indicates the tenant published agent linked to a tenant agent
	[SourcePublishedAgentId] INT NULL, -- Indicates the published tenant agent that was used to share the marketplace agent
	[CreatedByTenantAgentId] INT NULL, -- Indicates the tenant agent that was used to publish a published tenant agent
	[MarketplaceAgentId] INT NULL,     -- Indicates the maketplace tenant agent that was created from a published tenant agent
	[InstalledFromMarketplaceAgentId] INT NULL, -- Indicates the MarketplaceAgent this row was installed from (install direction, distinct from MarketplaceAgentId above which is the share direction). Used to dedupe installs per tenant and compute MarketplaceAgent's live installation count.
	[IsDeleted] BIT NOT NULL,
	[LastRunAt] DATETIMEOFFSET NULL,
	[MaxToolCallsPerTurn] INT NULL DEFAULT(3),
	[AdditionalInstructions] NVARCHAR(2000) NULL, -- Admin-supplied text appended after the platform SystemPrompt at runtime (WI 7318). Only meaningful on RunnableAgent rows (TenantAgent/PublishedTenantAgent); never set on MarketplaceAgent rows, so share/install cannot carry tenant-specific text.
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_Agent_ToAIModel] FOREIGN KEY ([AIModelId]) REFERENCES [AIModel]([Id]),
	CONSTRAINT [FK_Agent_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id]),
	CONSTRAINT [FK_Agent_ToPublishedTenantAgent] FOREIGN KEY ([PublishedTenantAgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_Agent_ToSourcePublishedAgent] FOREIGN KEY ([SourcePublishedAgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_Agent_ToCreatedByTenantAgent] FOREIGN KEY ([CreatedByTenantAgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_Agent_ToMarketplaceAgent] FOREIGN KEY ([MarketplaceAgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_Agent_ToInstalledFromMarketplaceAgent] FOREIGN KEY ([InstalledFromMarketplaceAgentId]) REFERENCES [Agent]([Id])
)

GO

CREATE INDEX IX_Agent_AgentType ON [dbo].[Agent] ([AgentType]);

GO

CREATE INDEX IX_Agent_Name ON [dbo].[Agent] ([Name]);

GO

CREATE INDEX IX_Agent_Description ON [dbo].[Agent] ([Description]);

GO

CREATE INDEX IX_Agent_AIModelId ON [dbo].[Agent] ([AIModelId]);

GO

CREATE INDEX IX_Agent_TenantId ON [dbo].[Agent] ([TenantId]);

GO

CREATE INDEX IX_Agent_PublishedTenantAgentId ON [dbo].[Agent] ([PublishedTenantAgentId]);

GO

CREATE INDEX IX_Agent_SourcePublishedAgentId ON [dbo].[Agent] ([SourcePublishedAgentId]);

GO

CREATE INDEX IX_Agent_CreatedByTenantAgentId ON [dbo].[Agent] ([CreatedByTenantAgentId]);

GO

CREATE INDEX IX_Agent_MarketplaceAgentId ON [dbo].[Agent] ([MarketplaceAgentId]);

GO

CREATE INDEX IX_Agent_InstalledFromMarketplaceAgentId ON [dbo].[Agent] ([InstalledFromMarketplaceAgentId]);