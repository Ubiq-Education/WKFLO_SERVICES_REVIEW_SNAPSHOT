CREATE TABLE [dbo].[TenantAccess]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[TenantId] INT NOT NULL,
	[Email] NVARCHAR(320) NOT NULL,
	[CreatedBy] NVARCHAR(320) NULL,
	[IsDeleted] BIT NOT NULL DEFAULT 0,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_TenantAccess_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id])
)

GO

CREATE UNIQUE INDEX IX_TenantAccess_TenantId_Email ON [dbo].[TenantAccess] ([TenantId], [Email]);

GO

CREATE INDEX IX_TenantAccess_Email ON [dbo].[TenantAccess] ([Email]);
