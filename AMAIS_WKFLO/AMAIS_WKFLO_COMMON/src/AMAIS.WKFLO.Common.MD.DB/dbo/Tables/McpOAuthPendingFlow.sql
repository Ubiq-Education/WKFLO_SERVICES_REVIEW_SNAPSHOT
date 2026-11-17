CREATE TABLE [dbo].[McpOAuthPendingFlow]
(
	[State] NVARCHAR(256) NOT NULL PRIMARY KEY, -- the OAuth `state` value; the only thing a callback request carries to look this row up by
	[ConnectionId] UNIQUEIDENTIFIER NOT NULL, -- RunableTool.McpOAuthConnectionId this flow belongs to
	[RequestingUserId] UNIQUEIDENTIFIER NULL, -- set when McpServerConfig.OAuthScope == PerUser (e.g. Canva); null for shared/tenant-wide connections (e.g. Humantic)
	[CodeVerifier] NVARCHAR(MAX) NOT NULL, -- PKCE (RFC 7636) code_verifier; encrypted at rest
	[ClientId] NVARCHAR(500) NOT NULL,
	[ClientSecret] NVARCHAR(MAX) NULL, -- encrypted at rest; null for a public client (PKCE-only, no secret)
	[TokenEndpointAuthMethod] NVARCHAR(100) NOT NULL,
	[TokenEndpoint] NVARCHAR(1000) NOT NULL,
	[AuthorizationServerIssuer] NVARCHAR(1000) NULL, -- for RFC 9207 `iss` validation on callback; null when discovery had no explicit issuer
	[Resource] NVARCHAR(1000) NULL, -- RFC 8707 resource indicator; also the MCP endpoint used for CompleteAsync's post-token handshake check
	[Scope] NVARCHAR(500) NULL,
	[RedirectUri] NVARCHAR(1000) NOT NULL,
	[CreatedOnUtc] DATETIMEOFFSET NOT NULL,
	[ExpiresOnUtc] DATETIMEOFFSET NOT NULL -- BeginAsync opportunistically deletes rows past this; CompleteAsync's lookup excludes them. No separate cleanup job.
)

GO

-- Backs both BeginAsync's opportunistic "DELETE ... WHERE ExpiresOnUtc < now" cleanup and CompleteAsync's
-- "WHERE State = @state AND ExpiresOnUtc >= now" lookup filter.
CREATE INDEX IX_McpOAuthPendingFlow_ExpiresOnUtc ON [dbo].[McpOAuthPendingFlow] ([ExpiresOnUtc]);
