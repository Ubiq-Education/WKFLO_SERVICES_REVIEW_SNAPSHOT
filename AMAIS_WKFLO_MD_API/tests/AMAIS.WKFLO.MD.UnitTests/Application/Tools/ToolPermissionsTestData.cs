using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools
{
    /// <summary>Shared builders for the WI 7447/7575 tool function/permission tests.</summary>
    internal static class ToolPermissionsTestData
    {
        public const string TenantName = "TestTenant";

        public static Tenant Tenant { get; } = new Tenant { Id = 1, Name = TenantName };

        public static string Snapshot(params (string Name, bool IsNew)[] functions) =>
            ToolPermissionJson.SerializeDiscoveredFunctions(functions.Select(f => new McpFunctionDescriptor
            {
                Name = f.Name,
                Description = $"{f.Name} description",
                InputSchema = JObject.Parse("{\"type\":\"object\"}"),
                IsNew = f.IsNew
            }));

        public static string Enabled(params string[] names) => ToolPermissionJson.SerializeEnabledFunctions(names);

        public static TenantTool McpTool(int id, string snapshot = null, string enabled = null, bool permissionsEnabled = true) => new()
        {
            Id = id,
            Name = $"MCP tool {id}",
            Description = "An MCP tool",
            Type = "MCP",
            Config = "{}",
            IsActive = true,
            TenantId = Tenant.Id,
            Tenant = Tenant,
            DiscoveredFunctions = snapshot,
            FunctionsDiscoveredOn = snapshot == null ? null : new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EnabledFunctions = enabled,
            PermissionsEnabled = permissionsEnabled,
            AgentTools = new List<AgentTool>()
        };

        public static TenantTool OtherTool(int id, string type, bool permissionsEnabled = true, bool isActive = true) => new()
        {
            Id = id,
            Name = $"{type} tool {id}",
            Description = $"A {type} tool",
            Type = type,
            Config = "{}",
            IsActive = isActive,
            TenantId = Tenant.Id,
            Tenant = Tenant,
            PermissionsEnabled = permissionsEnabled,
            AgentTools = new List<AgentTool>()
        };
    }
}
