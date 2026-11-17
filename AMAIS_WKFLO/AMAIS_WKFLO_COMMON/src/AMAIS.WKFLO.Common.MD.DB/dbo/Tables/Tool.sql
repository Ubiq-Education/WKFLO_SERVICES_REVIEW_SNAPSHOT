CREATE TABLE [dbo].[Tool]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[ToolType] NVARCHAR(100) NOT NULL,
	[Name] NVARCHAR (300) NOT NULL,
	[Description] NVARCHAR (MAX) NOT NULL,
	[Type] NVARCHAR(100) NULL, -- HTTP, Database, Email, Text, etc.
	[Config] NVARCHAR(MAX) NULL, -- JSON string for configuration
	[IsActive] BIT NULL,
	[TenantId] INT NOT NULL,
	[PublishedTenantToolId] INT NULL, -- Indicates the tenant published tool linked to a tenant tool
	[SourcePublishedToolId] INT NULL, -- Indicates the published tenant tool that was used to share the marketplace tool
	[CreatedByTenantToolId] INT NULL, -- Indicates the tenant tool that was used to publish a published tenant tool
	[MarketplaceToolId] INT NULL,     -- Indicates the maketplace tenant tool that was created from a published tenant tool
	[InstalledFromMarketplaceToolId] INT NULL, -- Indicates the MarketplaceTool this row was installed from (install direction, distinct from MarketplaceToolId above which is the share direction). Used to dedupe installs per tenant and compute MarketplaceTool's live installation count.
	[McpOAuthConnectionId] UNIQUEIDENTIFIER NULL, -- Shared MCP OAuth2 connection identity; same value on a TenantTool and its PublishedTenantTool
	[MaxToolOutputLength] INT NULL, -- Max output chars before SemanticKernelLargeToolOutputFilter truncates; NULL = no cap for this tool
	[PermissionsEnabled] BIT NOT NULL CONSTRAINT [DF_Tool_PermissionsEnabled] DEFAULT 0, -- WI 7575: opt-in flag (off for every tool by default) - whether agent tool permissions (Allow/Ask/Block) apply to this tool; off = tool behaves as before. The schema sync adds it with 0 for existing rows
	[DiscoveredFunctions] NVARCHAR(MAX) NULL, -- WI 7447: MCP only. JSON snapshot of the server's tools/list: [{name, description, inputSchema, isNew}]
	[FunctionsDiscoveredOn] DATETIMEOFFSET NULL, -- WI 7447: MCP only. When DiscoveredFunctions was last refreshed
	[EnabledFunctions] NVARCHAR(MAX) NULL, -- WI 7447: MCP only. JSON string array of enabled MCP function names; NULL or [] = none enabled
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_Tool_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id]),
	CONSTRAINT [FK_Tool_ToPublishedTenantTool] FOREIGN KEY ([PublishedTenantToolId]) REFERENCES [Tool]([Id]),
	CONSTRAINT [FK_Tool_ToSourcePublishedTool] FOREIGN KEY ([SourcePublishedToolId]) REFERENCES [Tool]([Id]),
	CONSTRAINT [FK_Tool_ToCreatedByTenantTool] FOREIGN KEY ([CreatedByTenantToolId]) REFERENCES [Tool]([Id]),
	CONSTRAINT [FK_Tool_ToCMarketplaceTool] FOREIGN KEY ([MarketplaceToolId]) REFERENCES [Tool]([Id]),
	CONSTRAINT [FK_Tool_ToInstalledFromMarketplaceTool] FOREIGN KEY ([InstalledFromMarketplaceToolId]) REFERENCES [Tool]([Id])
)

GO

CREATE INDEX IX_Tool_ToolType ON [dbo].[Tool] ([ToolType]);

GO

CREATE INDEX IX_Tool_Name ON [dbo].[Tool] ([Name]);

GO

CREATE INDEX IX_Tool_TenantId ON [dbo].[Tool] ([TenantId]);

GO

CREATE INDEX IX_Tool_PublishedTenantToolId ON [dbo].[Tool] ([PublishedTenantToolId]);

GO

CREATE INDEX IX_Tool_SourcePublishedToolId ON [dbo].[Tool] ([SourcePublishedToolId]);

GO

CREATE INDEX IX_Tool_CreatedByTenantToolId ON [dbo].[Tool] ([CreatedByTenantToolId]);

GO

CREATE INDEX IX_Tool_MarketplaceToolId ON [dbo].[Tool] ([MarketplaceToolId]);

GO

CREATE INDEX IX_Tool_McpOAuthConnectionId ON [dbo].[Tool] ([McpOAuthConnectionId]);

GO

CREATE INDEX IX_Tool_InstalledFromMarketplaceToolId ON [dbo].[Tool] ([InstalledFromMarketplaceToolId]);
