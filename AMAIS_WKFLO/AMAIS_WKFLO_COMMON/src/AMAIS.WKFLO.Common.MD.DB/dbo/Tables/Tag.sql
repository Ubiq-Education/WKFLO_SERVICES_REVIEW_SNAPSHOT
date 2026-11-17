CREATE TABLE [dbo].[Tag]
(
    [Id]            INT NOT NULL PRIMARY KEY IDENTITY,
    [Name]          NVARCHAR(100) NOT NULL,
    [Color]         NVARCHAR(20) NULL,
    [TenantId]      INT NOT NULL,
    [IsDeleted]     BIT NOT NULL DEFAULT 0,
    [CreatedOn]     DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
    CONSTRAINT [FK_Tag_ToTenant] FOREIGN KEY ([TenantId]) REFERENCES [Tenant]([Id])
)

GO

CREATE UNIQUE INDEX IX_Tag_Name_TenantId ON [dbo].[Tag] ([Name], [TenantId]) WHERE [IsDeleted] = 0;

GO

CREATE INDEX IX_Tag_TenantId ON [dbo].[Tag] ([TenantId]);
