CREATE TABLE [dbo].[DxpIntegration]
(
    [Id]             INT               NOT NULL PRIMARY KEY IDENTITY,
    [TenantId]       INT               NOT NULL REFERENCES [dbo].[Tenant]([Id]),
    [DxpTenantName]  NVARCHAR(500)     NOT NULL,
    [Domain]         NVARCHAR(1000)    NOT NULL,
    [ApiKey]         NVARCHAR(500)     NOT NULL,
    [IsActive]       BIT               NOT NULL,
    [IsDeleted]      BIT               NOT NULL,
    [CreatedOn]      DATETIMEOFFSET    NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn]  DATETIMEOFFSET    NULL,
    CONSTRAINT [UQ_DxpIntegration_TenantId] UNIQUE ([TenantId])
)
