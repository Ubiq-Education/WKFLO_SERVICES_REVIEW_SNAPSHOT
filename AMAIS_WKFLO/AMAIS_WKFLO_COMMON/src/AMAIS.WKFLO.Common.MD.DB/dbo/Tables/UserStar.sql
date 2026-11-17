CREATE TABLE [dbo].[UserStar]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[Email] NVARCHAR(500) NOT NULL,
	[Type] NVARCHAR(100) NOT NULL,
	[TypeId] INT NOT NULL,
	[Value] INT NOT NULL DEFAULT (0),
	[UserId] UNIQUEIDENTIFIER NULL,
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_UserStar_ToUser] FOREIGN KEY ([UserId]) REFERENCES [User]([Id]) ON DELETE SET NULL
)

GO

CREATE INDEX IX_UserStar_Email ON [dbo].[UserStar] ([Email]);

GO

CREATE INDEX IX_UserStar_Type ON [dbo].[UserStar] ([Type]);

GO

CREATE INDEX IX_UserStar_TypeId ON [dbo].[UserStar] ([TypeId]);

GO

CREATE INDEX IX_UserStar_Value ON [dbo].[UserStar] ([Value]);

GO

CREATE INDEX IX_UserStar_UserId ON [dbo].[UserStar] ([UserId]);