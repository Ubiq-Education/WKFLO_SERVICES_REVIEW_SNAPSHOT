CREATE TABLE [dbo].[AIModel]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[Name] NVARCHAR (300) NOT NULL,
	[Model] NVARCHAR (100) NOT NULL, -- e.g. GPT-3.5, GPT-4
	[DeploymentName] NVARCHAR (100) NULL, -- e.g. GPT-3.5, GPT-4
	[Provider] NVARCHAR(100) NOT NULL, -- openai, azure, google, anthropic, cohere
	[Endpoint] NVARCHAR(500) NULL, -- For AI endpoints
	[ApiVersion] NVARCHAR(100) NULL, -- 2024-12-01-preview
	[Type] NVARCHAR(100) NOT NULL, -- text, chat, vision, analytics, etc.
	[AuthMode] NVARCHAR(50) NULL, -- apiKey, managedIdentity (azure only; null defaults to apiKey behavior)
	[SecretId] INT NULL,
	[IsActive] BIT NOT NULL,
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_AIModel_ToSecret] FOREIGN KEY ([SecretId]) REFERENCES [Secret]([Id])
)

GO

CREATE INDEX IX_AIModel_SecretId ON [dbo].[AIModel] ([SecretId]);

GO

CREATE INDEX IX_AIModel_Name ON [dbo].[AIModel] ([Name]);

GO

CREATE INDEX IX_AIModel_Model ON [dbo].[AIModel] ([Model]);

GO

CREATE INDEX IX_AIModel_IsActive ON [dbo].[AIModel] ([IsActive]);

GO

CREATE INDEX IX_AIModel_IsDeleted ON [dbo].[AIModel] ([IsDeleted]);