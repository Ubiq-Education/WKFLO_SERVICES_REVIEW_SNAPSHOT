CREATE TABLE [dbo].[UserInstall]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[Email] NVARCHAR(500) NOT NULL,
	[Type] NVARCHAR(100) NOT NULL,
	[TypeId] INT NOT NULL,
	[UserId] UNIQUEIDENTIFIER NULL,
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_UserInstall_ToUser] FOREIGN KEY ([UserId]) REFERENCES [User]([Id]) ON DELETE SET NULL
)

GO

CREATE INDEX IX_UserInstall_Email ON [dbo].[UserInstall] ([Email]);

GO

CREATE INDEX IX_UserInstall_Type ON [dbo].[UserInstall] ([Type]);

GO

CREATE INDEX IX_UserInstall_TypeId ON [dbo].[UserInstall] ([TypeId]);

GO

CREATE INDEX IX_UserInstall_UserId ON [dbo].[UserInstall] ([UserId]);