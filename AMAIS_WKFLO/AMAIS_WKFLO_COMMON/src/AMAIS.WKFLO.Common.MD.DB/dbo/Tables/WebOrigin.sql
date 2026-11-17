CREATE TABLE [dbo].[WebOrigin]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[WebOriginType] NVARCHAR(100) NOT NULL,
	[Name] NVARCHAR (500) NOT NULL,
	[IsActive] BIT NOT NULL,
	[TenantId] INT NOT NULL,
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_WebOrigin_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id])
)

GO

CREATE INDEX IX_WebOrigin_WebOriginType ON [dbo].[WebOrigin] ([WebOriginType]);


GO

CREATE INDEX IX_WebOrigin_TenantId ON [dbo].[WebOrigin] ([TenantId]);
