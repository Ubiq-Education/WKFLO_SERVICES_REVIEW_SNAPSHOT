CREATE TABLE [dbo].[AgentTool]
(
	[AgentId] INT NOT NULL,
	[ToolId] INT NOT NULL,
	[ToolPermission] NVARCHAR(10) NULL, -- WI 7575: Allow|Ask|Block for non-MCP tools (whole tool); NULL = Block when the tool's PermissionsEnabled is on
	[FunctionPermissions] NVARCHAR(MAX) NULL, -- WI 7575: MCP only. JSON object { "<mcpFunctionName>": "Allow|Ask|Block" }; a missing function = Block when the tool's PermissionsEnabled is on
	CONSTRAINT [PK_AgentTool] PRIMARY KEY ([AgentId], [ToolId]),
	CONSTRAINT [FK_AgentTool_ToAgent] FOREIGN KEY ([AgentId]) REFERENCES [Agent]([Id]),
	CONSTRAINT [FK_AgentTool_ToTool] FOREIGN KEY ([ToolId]) REFERENCES [Tool]([Id])
)

GO

CREATE INDEX IX_AgentTool_AgentId ON [dbo].[AgentTool] ([AgentId]);

GO

CREATE INDEX IX_AgentTool_ToolId ON [dbo].[AgentTool] ([ToolId]);