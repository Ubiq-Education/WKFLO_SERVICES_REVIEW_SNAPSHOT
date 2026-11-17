CREATE TABLE [dbo].[ConversationAnalysis]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[ConversationId] INT NOT NULL,
	[AnalysisType] NVARCHAR(100) NOT NULL,
	[AnalyzedAt] DATETIMEOFFSET NOT NULL,
	[IsResolved] BIT NULL,
	[IsDeleted] BIT NOT NULL DEFAULT 0,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
	[LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_ConversationAnalysis_ToConversation] FOREIGN KEY ([ConversationId]) REFERENCES [Conversation]([Id])
)

GO

CREATE INDEX IX_ConversationAnalysis_ConversationId ON [dbo].[ConversationAnalysis] ([ConversationId]);
