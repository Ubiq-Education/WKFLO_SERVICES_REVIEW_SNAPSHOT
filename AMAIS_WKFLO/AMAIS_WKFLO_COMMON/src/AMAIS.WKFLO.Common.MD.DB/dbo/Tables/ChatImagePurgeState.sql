-- US 7974 / Task 7980: the local date on which chat images were last purged, per tenant.
-- TenantId NULL is the single row for chat images that belong to no known tenant (legacy blob names), which are
-- purged at UTC midnight. The UNIQUE constraint allows exactly one NULL in SQL Server, so there is one such row.
CREATE TABLE [dbo].[ChatImagePurgeState]
(
    [Id]                  INT               NOT NULL PRIMARY KEY IDENTITY,
    [TenantId]            INT               NULL REFERENCES [dbo].[Tenant]([Id]),
    [LastPurgeLocalDate]  DATE              NOT NULL,
    [IsDeleted]           BIT               NOT NULL DEFAULT 0,
    [CreatedOn]           DATETIMEOFFSET    NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn]       DATETIMEOFFSET    NULL,
    CONSTRAINT [UQ_ChatImagePurgeState_TenantId] UNIQUE ([TenantId])
)
