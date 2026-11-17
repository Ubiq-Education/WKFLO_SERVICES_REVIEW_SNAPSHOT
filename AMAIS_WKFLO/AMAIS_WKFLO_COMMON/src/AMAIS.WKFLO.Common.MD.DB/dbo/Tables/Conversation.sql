CREATE TABLE [dbo].[Conversation]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SessionId] NVARCHAR(500) NOT NULL,
	[Name] NVARCHAR(300) NULL,
	[PreviewLine] NVARCHAR(300) NULL,
	[AgentId] INT NOT NULL,
	[TenantId] INT NOT NULL,
	[ConversationDate] DATETIMEOFFSET NULL,
	[UserId] NVARCHAR(500) NULL,
	[SubjectId] NVARCHAR(500) NULL,
	[TotalThumbsUp] INT NOT NULL DEFAULT 0,
	[TotalThumbsDown] INT NOT NULL DEFAULT 0,
	[LastActivityAt] DATETIMEOFFSET NULL,
	[IsResolved] BIT NOT NULL DEFAULT 1,
	[AnalysisStatus] INT NOT NULL DEFAULT 0,
	[TitleStatus] INT NOT NULL DEFAULT 0,
	[IsTestChat] BIT NOT NULL DEFAULT 0,
	[IsDeleted] BIT NOT NULL DEFAULT 0,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
	[LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_Conversation_ToAgent] FOREIGN KEY ([AgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_Conversation_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id])
)

GO

CREATE INDEX IX_Conversation_AgentId ON [dbo].[Conversation] ([AgentId]);

GO

CREATE INDEX IX_Conversation_TenantId ON [dbo].[Conversation] ([TenantId]);

GO

CREATE INDEX IX_Conversation_SessionId ON [dbo].[Conversation] ([SessionId])
	INCLUDE ([AgentId], [TenantId], [IsDeleted], [LastActivityAt], [LastUpdatedOn], [CreatedOn]);

GO

-- US 7507: MAISY conversation list - a caller's sessions for one root agent (see ListPublicAgentConversationsQueryHandler).
CREATE INDEX IX_Conversation_AgentId_UserId ON [dbo].[Conversation] ([AgentId], [UserId])
	INCLUDE ([SessionId], [TenantId], [IsTestChat], [IsDeleted]);
