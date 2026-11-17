CREATE TABLE [dbo].[TenantSetting]
(
    [Id]             INT               NOT NULL PRIMARY KEY IDENTITY,
    [TenantId]       INT               NOT NULL REFERENCES [dbo].[Tenant]([Id]),
    [TimeZone]       NVARCHAR(100)     NOT NULL DEFAULT 'UTC',
    [TimeFormat]     NVARCHAR(10)      NOT NULL DEFAULT '24h',
    [DateFormat]     NVARCHAR(32)      NOT NULL DEFAULT 'yyyy-MM-dd',
    [IsDeleted]      BIT               NOT NULL DEFAULT 0,
    [CreatedOn]      DATETIMEOFFSET    NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn]  DATETIMEOFFSET    NULL,
    CONSTRAINT [UQ_TenantSetting_TenantId] UNIQUE ([TenantId])
)
