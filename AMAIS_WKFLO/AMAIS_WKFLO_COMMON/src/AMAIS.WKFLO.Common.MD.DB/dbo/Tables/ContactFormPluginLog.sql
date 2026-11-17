CREATE TABLE [dbo].[ContactFormPluginLog]
(
    [Id]              INT            NOT NULL IDENTITY(1,1),
    [TenantId]        INT            NOT NULL,
    [AgentId]         INT            NOT NULL,
    [SubjectId]       NVARCHAR(500)  NOT NULL,
    [SubjectIdSource] NVARCHAR(100)  NOT NULL,
    [Action]          NVARCHAR(100)  NOT NULL,
    [IsDeleted]       BIT            NOT NULL DEFAULT 0,
    [CreatedOn]       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    [LastUpdatedOn]   DATETIMEOFFSET NULL,
    CONSTRAINT [PK_ContactFormPluginLog] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ContactFormPluginLog_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenant]([Id]),
    CONSTRAINT [FK_ContactFormPluginLog_Agent]  FOREIGN KEY ([AgentId])  REFERENCES [dbo].[Agent]([Id])
)

GO

CREATE NONCLUSTERED INDEX [IX_ContactFormPluginLog_SubjectId_TenantId_IsDeleted]
    ON [dbo].[ContactFormPluginLog] ([SubjectId] ASC, [TenantId] ASC, [IsDeleted] ASC);
