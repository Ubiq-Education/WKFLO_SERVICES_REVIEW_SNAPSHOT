CREATE TABLE [dbo].[ToolOAuthToken]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[McpOAuthConnectionId] UNIQUEIDENTIFIER NOT NULL, -- shared by a TenantTool and its PublishedTenantTool (see Tool.McpOAuthConnectionId); not an FK to Tool
	[UserId] UNIQUEIDENTIFIER NULL, -- set when McpServerConfig.OAuthScope == PerUser (e.g. Canva); null for shared/tenant-wide connections (e.g. Humantic)
	[AccessToken] NVARCHAR(MAX) NOT NULL, -- encrypted at rest
	[RefreshToken] NVARCHAR(MAX) NULL, -- encrypted at rest
	[TokenType] NVARCHAR(50) NULL,
	[ExpiresAtUtc] DATETIMEOFFSET NULL,
	[Scope] NVARCHAR(500) NULL,
	[ClientId] NVARCHAR(500) NULL,
	[ClientSecret] NVARCHAR(MAX) NULL, -- encrypted at rest; only set for confidential clients from dynamic registration
	[TokenEndpointAuthMethod] NVARCHAR(100) NULL,
	[AuthorizationServer] NVARCHAR(500) NULL,
	[ConnectedOnUtc] DATETIMEOFFSET NOT NULL,
	[TokenObtainedAtUtc] DATETIMEOFFSET NOT NULL,
	[LastCheckedOnUtc] DATETIMEOFFSET NULL,
	[LastCheckSucceeded] BIT NULL,
	[LastError] NVARCHAR(500) NULL,
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL
)

GO

-- At most one shared (tenant-wide) token row per connection. SQL Server treats NULLs as distinct by
-- default, so this filtered index (not a plain unique index on the column) is what actually enforces
-- "at most one" — an unfiltered composite unique index on (McpOAuthConnectionId, UserId) would not.
--
-- Together with the index below, these two enforce "at most one shared row" and "at most one row per
-- user" independently — NOT that a connection can't have both a shared row and per-user rows at once
-- (e.g. left over after a tool's OAuthScope switches Shared -> PerUser or back). That would need a
-- database-level mechanism this schema doesn't have (a check constraint can't reference other rows; would
-- need a trigger). The operational guarantee instead lives in application code: McpOAuthScopeResolver
-- never resolves a PerUser tool's requesting-user id to null, so a PerUser tool's reads/writes can never
-- accidentally hit a stray Shared row even if one exists for the same connection id.
CREATE UNIQUE INDEX IX_ToolOAuthToken_McpOAuthConnectionId ON [dbo].[ToolOAuthToken] ([McpOAuthConnectionId]) WHERE [UserId] IS NULL;

GO

-- At most one row per (connection, user) for per-user mode (e.g. Canva). See the remarks above this
-- index's sibling: this doesn't prevent per-user rows coexisting with a shared row for the same connection.
CREATE UNIQUE INDEX IX_ToolOAuthToken_McpOAuthConnectionId_UserId ON [dbo].[ToolOAuthToken] ([McpOAuthConnectionId], [UserId]) WHERE [UserId] IS NOT NULL;
