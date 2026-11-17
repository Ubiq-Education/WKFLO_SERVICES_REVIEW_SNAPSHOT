CREATE TABLE [dbo].[AICredit]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[AIModelId] INT NOT NULL,
	[TokensIn] BIGINT NOT NULL,
	[CostInMTok] DECIMAL(18,6) NOT NULL, -- $ per million input tokens, kept for reference/traceability only
	[TokensOut] BIGINT NOT NULL,
	[CostOutMTok] DECIMAL(18,6) NOT NULL, -- $ per million output tokens, kept for reference/traceability only
	[TotalCost] AS (CAST(ROUND([CostInMTok] + [CostOutMTok], 2) AS DECIMAL(18,2))) PERSISTED,
	[MultiplierIn] DECIMAL(9,4) NOT NULL,
	[MultiplierOut] DECIMAL(9,4) NOT NULL,
	-- Credits intentionally excludes cost - it's already baked into MultiplierIn/MultiplierOut.
	[Credits] AS (CAST(ROUND((CAST([TokensIn] AS DECIMAL(28,4)) * [MultiplierIn] / 1000) + (CAST([TokensOut] AS DECIMAL(28,4)) * [MultiplierOut] / 1000), 2) AS DECIMAL(18,2))) PERSISTED,
	[IsDeleted] BIT NOT NULL,
	[CreatedOn] DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastUpdatedOn] DATETIMEOFFSET NULL,
	CONSTRAINT [FK_AICredit_ToAIModel] FOREIGN KEY ([AIModelId]) REFERENCES [AIModel]([Id]),
	CONSTRAINT [CK_AICredit_MultiplierIn_Positive] CHECK ([MultiplierIn] > 0),
	CONSTRAINT [CK_AICredit_MultiplierOut_Positive] CHECK ([MultiplierOut] > 0)
)

GO

CREATE UNIQUE INDEX IX_AICredit_AIModelId ON [dbo].[AICredit] ([AIModelId]) WHERE [IsDeleted] = 0;

GO

CREATE INDEX IX_AICredit_IsDeleted ON [dbo].[AICredit] ([IsDeleted]);
