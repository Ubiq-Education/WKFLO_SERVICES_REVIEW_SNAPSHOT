CREATE TABLE [dbo].[ClassificationJob]
(
    [Id]                INT             NOT NULL PRIMARY KEY IDENTITY,
    [TenantId]          INT             NOT NULL,
    [AgentId]           INT             NOT NULL,
    [JobType]           NVARCHAR(100)   NOT NULL,
    [PreparedAt]        DATETIMEOFFSET  NOT NULL,
    [QueuedAt]          DATETIMEOFFSET  NULL,
    [ProcessingStartAt] DATETIMEOFFSET  NULL,
    [FinishedAt]        DATETIMEOFFSET  NULL,
    [IsSuccess]         BIT             NULL,
    [AIModelName]       NVARCHAR(300)   NULL,
    [TotalInputTokens]  INT             NULL,
    [TotalOutputTokens] INT             NULL,
    [TotalConversations] INT            NULL,
    [TotalMessages]     INT             NULL,
    [ErrorMessage]      NVARCHAR(2000)  NULL,
    [IsDeleted]         BIT             NOT NULL DEFAULT 0,
    [CreatedOn]         DATETIMEOFFSET  NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn]     DATETIMEOFFSET  NULL,
    CONSTRAINT [FK_ClassificationJob_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id]),
    CONSTRAINT [FK_ClassificationJob_ToAgent]  FOREIGN KEY ([AgentId])  REFERENCES [Agent]([Id])
)

GO

CREATE INDEX IX_ClassificationJob_TenantId_AgentId_JobType ON [dbo].[ClassificationJob] ([TenantId], [AgentId], [JobType]);
