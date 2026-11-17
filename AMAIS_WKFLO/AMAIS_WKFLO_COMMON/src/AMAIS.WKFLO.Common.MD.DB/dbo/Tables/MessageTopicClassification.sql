CREATE TABLE [dbo].[MessageTopicClassification]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[TopicId] UNIQUEIDENTIFIER NULL,
	[TopicName] NVARCHAR(300) NULL ,
	[SessionId] NVARCHAR(500) NOT NULL,
	[MessageId] UNIQUEIDENTIFIER NOT NULL,
	[AgentId] INT NOT NULL,
	[MessageDate] DATETIMEOFFSET NOT NULL,
	[IsResolved] BIT NULL,
	[TopicVersion] NVARCHAR(50) NULL,
	[AnswerMessageId] UNIQUEIDENTIFIER NULL,
	[IsDeleted] BIT NOT NULL DEFAULT 0,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
	[LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_MessageTopicClassification_ToAgent] FOREIGN KEY ([AgentId]) REFERENCES [Agent]([Id])
)

GO

CREATE INDEX IX_MessageTopicClassification_SessionId ON [dbo].[MessageTopicClassification] ([SessionId]);

GO

CREATE INDEX IX_MessageTopicClassification_TopicId ON [dbo].[MessageTopicClassification] ([TopicId]);

GO

CREATE INDEX IX_MessageTopicClassification_AgentId_MessageDate ON [dbo].[MessageTopicClassification] ([AgentId], [MessageDate]) INCLUDE ([TopicName], [IsDeleted]);

GO

CREATE UNIQUE INDEX IX_MessageTopicClassification_MessageId ON [dbo].[MessageTopicClassification] ([MessageId]);

GO

CREATE INDEX IX_MessageTopicClassification_TopicId_MessageDate ON [dbo].[MessageTopicClassification] ([TopicId], [MessageDate]);

GO

CREATE INDEX IX_MessageTopicClassification_AnswerMessageId ON [dbo].[MessageTopicClassification] ([AnswerMessageId]);
