CREATE TABLE [dbo].[AuditLogEntry]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[TenantGuid] NVARCHAR(64) NOT NULL,
	[EntityType] NVARCHAR(200) NOT NULL,
	[EntityId] NVARCHAR(450) NOT NULL,
	[Action] NVARCHAR(20) NOT NULL,
	[ActorUserGuid] NVARCHAR(64) NULL,
	[ActorUserName] NVARCHAR(300) NULL,
	[ActorUserEmail] NVARCHAR(320) NULL,
	[Timestamp] DATETIMEOFFSET NOT NULL,
	[Changes] NVARCHAR(MAX) NULL,
	[CorrelationId] NVARCHAR(100) NULL,
	[IsDeleted] BIT NOT NULL DEFAULT 0,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
	[LastUpdatedOn] DATETIMEOFFSET NULL
)

GO

CREATE INDEX IX_AuditLogEntry_TenantGuid_Timestamp ON [dbo].[AuditLogEntry] ([TenantGuid], [Timestamp] DESC);

GO

CREATE INDEX IX_AuditLogEntry_TenantGuid_EntityType ON [dbo].[AuditLogEntry] ([TenantGuid], [EntityType]);
