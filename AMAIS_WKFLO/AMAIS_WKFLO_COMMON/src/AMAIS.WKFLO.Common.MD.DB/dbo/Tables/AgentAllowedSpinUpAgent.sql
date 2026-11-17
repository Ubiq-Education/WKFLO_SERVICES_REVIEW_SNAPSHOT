CREATE TABLE [dbo].[AgentAllowedSpinUpAgent]
(
	[AgentId] INT NOT NULL,
	[AllowedSpinUpAgentId] INT NOT NULL,
	CONSTRAINT [PK_AgentAllowedSpinUpAgent] PRIMARY KEY ([AgentId], [AllowedSpinUpAgentId]),
	CONSTRAINT [FK_AgentAllowedSpinUpAgent_ToAgent] FOREIGN KEY ([AgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_AgentAllowedSpinUpAgent_ToAllowedSpinUpAgent] FOREIGN KEY ([AllowedSpinUpAgentId]) REFERENCES [Agent]([Id])
)

GO

CREATE INDEX IX_AgentAllowedSpinUpAgent_AgentId ON [dbo].[AgentAllowedSpinUpAgent] ([AgentId]);

GO

CREATE INDEX IX_AgentAllowedSpinUpAgent_AllowedSpinUpAgentId ON [dbo].[AgentAllowedSpinUpAgent] ([AllowedSpinUpAgentId]);
