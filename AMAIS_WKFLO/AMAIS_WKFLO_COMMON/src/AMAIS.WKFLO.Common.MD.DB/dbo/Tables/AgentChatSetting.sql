CREATE TABLE [dbo].[AgentChatSetting]
(
    [Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [HeaderTitle] NVARCHAR(200) NULL,
    [ChatWidth] NVARCHAR(50) NOT NULL DEFAULT 'standard',
    [PrimaryColor] NVARCHAR(50) NOT NULL DEFAULT '#3b82f6',
    [ForegroundColor] NVARCHAR(50) NULL,
    [BubbleStyle] NVARCHAR(50) NOT NULL DEFAULT 'rounded',
    [FloatingPosition] NVARCHAR(50) NOT NULL DEFAULT 'right',
    [LauncherStyle] NVARCHAR(50) NOT NULL DEFAULT 'circle',
    [LauncherText] NVARCHAR(500) NULL,
    [BubbleIconPath] NVARCHAR(500) NULL,
    [ChatbotIconPath] NVARCHAR(500) NULL,
    [CssUrl] NVARCHAR(500) NULL,
    [WelcomeMessage] NVARCHAR(500) NULL,
    [FooterMessage] NVARCHAR(4000) NULL,
    [ShowDescription] BIT NOT NULL DEFAULT 1,
	[ChainOfThought] BIT NOT NULL DEFAULT 0,
    [ApiEndpoint] NVARCHAR(500) NULL,
    [SecurityConfiguration] NVARCHAR(MAX) NULL, -- JSON configuration
	[IsWidget] BIT NOT NULL DEFAULT 1,
	[IsShareable] BIT NOT NULL DEFAULT 0,
    [ShareId] NVARCHAR(100) NULL,
    [AgentId] INT NOT NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
    
    CONSTRAINT [FK_AgentChatSetting_ToAgent] FOREIGN KEY ([AgentId]) REFERENCES [Agent]([Id])
)

GO

CREATE INDEX IX_AgentChatSetting_AgentId ON [dbo].[AgentChatSetting] ([AgentId]);

GO

CREATE INDEX IX_AgentChatSetting_ShareId ON [dbo].[AgentChatSetting] ([ShareId]);

GO

CREATE INDEX IX_AgentChatSetting_IsDeleted ON [dbo].[AgentChatSetting] ([IsDeleted]);