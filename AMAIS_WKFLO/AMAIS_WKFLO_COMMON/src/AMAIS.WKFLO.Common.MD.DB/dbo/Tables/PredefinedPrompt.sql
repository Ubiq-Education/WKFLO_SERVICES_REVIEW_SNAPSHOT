CREATE TABLE [dbo].[PredefinedPrompt]
(
    [Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [Title] NVARCHAR(200) NULL,
    [Prompt] NVARCHAR(MAX) NULL,
    [AgentChatSettingId] INT NOT NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,

    CONSTRAINT [FK_PredefinedPrompt_ToAgentChatSetting] FOREIGN KEY ([AgentChatSettingId]) REFERENCES [AgentChatSetting]([Id])
)

GO

CREATE INDEX IX_PredefinedPrompt_AgentChatSettingId ON [dbo].[PredefinedPrompt] ([AgentChatSettingId]);

GO

CREATE INDEX IX_PredefinedPrompt_IsDeleted ON [dbo].[PredefinedPrompt] ([IsDeleted]);
