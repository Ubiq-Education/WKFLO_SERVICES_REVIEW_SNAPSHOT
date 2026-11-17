CREATE TABLE [dbo].[EntityTag]
(
    [TagId]      INT NOT NULL,
    [EntityType] NVARCHAR(100) NOT NULL,
    [EntityId]   INT NOT NULL,
    CONSTRAINT [PK_EntityTag] PRIMARY KEY ([TagId], [EntityType], [EntityId]),
    CONSTRAINT [FK_EntityTag_ToTag] FOREIGN KEY ([TagId]) REFERENCES [Tag]([Id])
)

GO

CREATE INDEX IX_EntityTag_TagId ON [dbo].[EntityTag] ([TagId]);

GO

CREATE INDEX IX_EntityTag_EntityType_EntityId ON [dbo].[EntityTag] ([EntityType], [EntityId]);
